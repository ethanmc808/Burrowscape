using UnityEngine;
using System.Collections.Generic;

// Fills every currently-unbuilt patch of floor space with a "dirt" visual — Fallout-Shelter-style: the
// whole buildable area starts covered in dirt, and placing a room carves that dirt away under its
// footprint; deleting, merging, or upgrading a room recomputes and fills the gap back in (or reshapes it)
// automatically.
//
// DESIGN: rather than a rigid grid of small fixed-size dirt tiles across the WHOLE buildable area
// (which would need tile-merging logic for multi-width rooms and multi-width merges), dirt is computed
// as the GAPS between built rooms on each floor — walk each floor's rooms left-to-right by GridX, and
// fill each gap with a row of unstretched 1-unit tiles (see SpawnGapTiles). This means merges/upgrades
// never need special handling: they just change room footprints, and the gap computation adapts
// automatically. Tile count still scales only with actual unbuilt gap width, not the full buildable
// area, so already-built floors stay cheap even on a very wide/deep base.
//
// HOOKUP: rather than building a second parallel "something changed" notification system, this listens
// to BaseLayoutManager.OnFloorLayoutChanged (already fired once per floor whenever that floor's room list
// settles — the same signal RefreshDoorwaysForFloor already uses) and OnBuildableBoundsChanged (fired
// when a future tech unlock grows the buildable area). Since building, deleting, merging, and upgrading a
// room all funnel through RoomBase.OnEnable/OnDisable -> BaseLayoutManager's existing registration path,
// all four cases are covered for free with no changes needed to BuildModeController/DeleteModeController/
// RoomTransitionService.
//
// ART: dirtSegmentPrefab must be authored at exactly 1 world unit wide on X — gaps are filled with
// multiple UNSTRETCHED copies of it laid edge-to-edge (see SpawnGapTiles), not one copy stretched to fit.
// This gives genuine texture repetition instead of a smeared/stretched look, and works cleanly because
// every gap width is guaranteed to be a whole number of units: room edges and the buildable-bounds edges
// both snap to this same 1-unit grid (BuildGridUtility.SnapCenterX), so there's never a leftover
// fractional sliver to handle. Use any reasonably seamless-tiling dirt texture on the prefab and adjacent
// tiles will read as one continuous patch rather than an obviously repeated stamp.
public class DirtFillManager : MonoBehaviour
{
    public static DirtFillManager Instance { get; private set; }

    [SerializeField] private GameObject dirtSegmentPrefab;

    // Optional — organizes spawned segments under one Hierarchy node. Falls back to this.transform if
    // left unassigned.
    [SerializeField] private Transform segmentParent;

    // Pushes every dirt tile back along Z relative to layout.EntranceWorldZ (the same Z every room
    // prefab is placed at) — rooms and dirt don't necessarily need to sit at the exact same Z depending
    // on how the dirt prefab's mesh is authored, so this is here as a simple Inspector-tunable correction
    // rather than a hardcoded assumption. Positive pushes away from camera, negative pulls toward it —
    // flip the sign here if 6 goes the wrong direction.
    [SerializeField] private float zOffset = 6f;

    // One list of spawned segment instances per floor, so a regenerate can cleanly destroy exactly what
    // it previously spawned for that floor before recomputing — never touches other floors' segments.
    private readonly Dictionary<int, List<GameObject>> spawnedSegmentsByFloor = new Dictionary<int, List<GameObject>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (dirtSegmentPrefab == null)
            Debug.LogWarning($"{name}: dirtSegmentPrefab is not assigned — no dirt will be generated until it is.");

        BaseLayoutManager.OnFloorLayoutChanged += HandleFloorLayoutChanged;
        BaseLayoutManager.OnBuildableBoundsChanged += HandleBoundsChanged;

        // Full initial sweep — safe to do synchronously here even before any OnFloorLayoutChanged event
        // fires, since every scene-load room has already registered with BaseLayoutManager during its own
        // OnEnable (which runs before any Start(), including this one), so GetAllRoomsOnFloor already
        // reflects the correct starting layout.
        RegenerateAllFloors();
    }

    private void OnDestroy()
    {
        BaseLayoutManager.OnFloorLayoutChanged -= HandleFloorLayoutChanged;
        BaseLayoutManager.OnBuildableBoundsChanged -= HandleBoundsChanged;
        if (Instance == this) Instance = null;
    }

    private void HandleFloorLayoutChanged(int floorIndex)
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        if (layout == null) return;

        // Defensive bounds check — dirt should never exist outside the valid buildable range, regardless
        // of why some upstream system reported a change on a floor out there (e.g. LiftRoom's
        // ghost-preview-measurement edge case, now fixed at the source — see LiftRoom.hasRegisteredFloor
        // — but this stays as a safety net against any similar issue in the future). Actively clears
        // rather than just ignoring, in case dirt already exists there from before this check existed.
        if (floorIndex < layout.EntranceFloorIndex || floorIndex > layout.EntranceFloorIndex + layout.MaxFloorDepth)
        {
            ClearFloor(floorIndex);
            return;
        }

        RegenerateFloor(floorIndex);
    }

    private void HandleBoundsChanged()
    {
        RegenerateAllFloors();
    }

    private void RegenerateAllFloors()
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        if (layout == null) return;

        for (int floor = layout.EntranceFloorIndex; floor <= layout.EntranceFloorIndex + layout.MaxFloorDepth; floor++)
            RegenerateFloor(floor);
    }

    private void RegenerateFloor(int floorIndex)
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        if (layout == null || dirtSegmentPrefab == null) return;

        ClearFloor(floorIndex);

        float xMin = layout.EntranceWorldX - layout.MaxGridXFromEntrance;
        float xMax = layout.EntranceWorldX + layout.MaxGridXFromEntrance;

        // The entrance's own floor can't be built (or dirt-filled) past its outward edge — same cutoff
        // RoomPlacementValidator uses ("can't build past the entrance"), since that's the gate/queue
        // area, not diggable floor space. Every other floor uses the full symmetric range.
        if (floorIndex == layout.EntranceFloorIndex)
            xMax = Mathf.Min(xMax, layout.EntranceLeftEdgeX);

        if (xMax <= xMin) return; // nothing buildable on this floor at all (shouldn't normally happen)

        List<RoomBase> rooms = layout.GetAllRoomsOnFloor(floorIndex); // already ordered by ascending GridX
        List<GameObject> spawned = new List<GameObject>();
        float cursor = xMin;

        foreach (RoomBase room in rooms)
        {
            if (room == null) continue;

            RoomPlacementValidator.GetInterval(room, out float roomMin, out float roomMax);

            // Clamp to the buildable range rather than trusting it blindly — guards against a hand-placed
            // room sitting outside bounds ever producing a negative-width segment below.
            float clampedMin = Mathf.Clamp(roomMin, xMin, xMax);
            float clampedMax = Mathf.Clamp(roomMax, xMin, xMax);

            if (clampedMin > cursor + RoomPlacementValidator.Epsilon)
                SpawnGapTiles(floorIndex, cursor, clampedMin, spawned);

            cursor = Mathf.Max(cursor, clampedMax);
        }

        if (xMax > cursor + RoomPlacementValidator.Epsilon)
            SpawnGapTiles(floorIndex, cursor, xMax, spawned);

        spawnedSegmentsByFloor[floorIndex] = spawned;
    }

    // Fills a gap with individual, UNSTRETCHED 1-unit-wide dirt tiles laid edge-to-edge, rather than one
    // tile stretched to fit — every room edge (and the buildable-bounds edges themselves) already snaps
    // to this same 1-unit grid (see BuildGridUtility.SnapCenterX), so gap widths are always whole numbers
    // and this always divides evenly with no leftover sliver. This is what keeps the dirt texture
    // visually REPEATING at a constant size instead of stretching thinner/wider depending on gap width.
    private void SpawnGapTiles(int floorIndex, float gapMin, float gapMax, List<GameObject> spawnedList)
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        int tileCount = Mathf.RoundToInt(gapMax - gapMin); // rounded, not floored — guards against tiny float drift off a true integer, not an actual fractional gap
        if (tileCount <= 0) return;

        float y = layout.GetWorldYForFloor(floorIndex);
        Transform parent = segmentParent != null ? segmentParent : transform;

        for (int i = 0; i < tileCount; i++)
        {
            float centerX = gapMin + i + 0.5f; // each tile is exactly 1 unit wide, centered on its own grid cell
            Vector3 position = new Vector3(centerX, y, layout.EntranceWorldZ + zOffset);

            GameObject tile = Instantiate(dirtSegmentPrefab, position, Quaternion.identity, parent);
            tile.name = $"DirtTile_Floor{floorIndex}_{centerX:F1}";
            spawnedList.Add(tile);
        }
    }

    private void ClearFloor(int floorIndex)
    {
        if (!spawnedSegmentsByFloor.TryGetValue(floorIndex, out List<GameObject> existing)) return;

        foreach (GameObject go in existing)
            if (go != null) Destroy(go);

        spawnedSegmentsByFloor.Remove(floorIndex);
    }
}