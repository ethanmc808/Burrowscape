using UnityEngine;
using System.Collections.Generic;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomBase : MonoBehaviour
{
    [Header("Power")]
    [SerializeField] protected bool consumesPower = false;
    [SerializeField] protected float powerConsumptionAmount = 1f;
    [SerializeField] protected float powerConsumptionInterval = 1f;
    [SerializeField] protected bool producesPower = false;
    [SerializeField] protected float powerProductionAmount = 1f;
    [SerializeField] protected float powerProductionInterval = 1f;

    [Header("Water")]
    [SerializeField] protected bool consumesWater = false;
    [SerializeField] protected float waterConsumptionAmount = 1f;
    [SerializeField] protected float waterConsumptionInterval = 10f;

    [Header("Worker Decay Rates (while Working in this room)")]
    [SerializeField] protected float workerEnergyDecayPerSecond = 0.2f; // matches NPCBunny's prior flat default
    [SerializeField] protected float workerMoodDecayPerSecond = 0f;

    public bool ConsumesPower => consumesPower;
    public float PowerConsumptionAmount => powerConsumptionAmount;
    public float PowerConsumptionInterval => powerConsumptionInterval;
    public bool ProducesPower => producesPower;
    public float PowerProductionAmount => powerProductionAmount;
    public float PowerProductionInterval => powerProductionInterval;
    public bool ConsumesWater => consumesWater;
    public float WorkerEnergyDecayPerSecond => workerEnergyDecayPerSecond;
    public float WorkerMoodDecayPerSecond => workerMoodDecayPerSecond;

    // Power is arbitrated per-floor by PowerManager (distance-from-producer ranked shutoff); Water is
    // gated purely on WaterManager.TryConsumeWater() success/failure via this room's own coroutine
    // below. Both default true so a room that doesn't opt into either (consumesPower/consumesWater both
    // false) is always operational, matching pre-Power-system behavior exactly.
    public bool IsPowered { get; private set; } = true;
    public bool IsWatered { get; private set; } = true; // irrelevant when consumesWater is false
    public bool IsOperational => IsPowered && (!consumesWater || IsWatered);

    private bool wasOperational = true;
    private PowerManager powerManager;
    private Coroutine waterConsumptionRoutine;
    private GameObject powerWarningIcon;
    private GameObject waterWarningIcon;

    // Called only by this room's floor's PowerManager.
    public void SetPowered(bool powered)
    {
        if (IsPowered == powered) return;
        IsPowered = powered;
        GetComponent<RoomLightFlicker>()?.OnPowerChanged(powered);
        powerWarningIcon?.SetActive(!powered);
        RecheckOperational();
    }

    // Called internally by WaterConsumptionRoutine below, based on TryConsumeWater() success/failure.
    private void SetWatered(bool watered)
    {
        if (IsWatered == watered) return;
        IsWatered = watered;
        waterWarningIcon?.SetActive(!watered);
        RecheckOperational();
    }

    // Fires the shared shutdown/restore hook only on an actual IsOperational transition, so Power and
    // Water flipping the same frame (e.g. losing both at once) can never double-fire it.
    private void RecheckOperational()
    {
        bool nowOperational = IsOperational;
        if (nowOperational == wasOperational) return;
        wasOperational = nowOperational;

        if (this is IJobRoom jobRoom)
        {
            if (nowOperational) jobRoom.OnRoomRestored();
            else jobRoom.OnRoomShutdown();
        }
    }

    // Structurally identical to WaterRoom.DrinkingRoutine's TryConsumeWater() tick. Skips its tick (and
    // leaves IsWatered as-is) while IsPowered is false — a room that's already dark shouldn't keep
    // trying to draw water — and resumes attempts the moment power returns.
    private IEnumerator WaterConsumptionRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(waterConsumptionInterval);

            if (!IsPowered) continue;

            int unitsToConsume = Mathf.Max(1, Mathf.RoundToInt(waterConsumptionAmount));
            bool success = true;
            for (int i = 0; i < unitsToConsume; i++)
            {
                if (!WaterManager.Instance.TryConsumeWater())
                {
                    success = false;
                    break;
                }
            }

            SetWatered(success);
        }
    }

    [Header("Entrances")]
    [SerializeField] protected Transform leftEntrance;
    [SerializeField] protected Transform rightEntrance;
    [SerializeField] protected Transform middleLeft;
    [SerializeField] protected Transform middleRight;

    [Header("Pass-Through Path (for bunnies just walking through this room)")]
    [SerializeField] protected List<Transform> passThroughWaypoints; // ordered left-to-right

    [Header("Spot Paths")]
    [SerializeField] protected List<RoomPath> paths; // entrance-to-spot paths, shared by Garden/Cafeteria

    public Transform LeftEntrance => leftEntrance;
    public Transform RightEntrance => rightEntrance;
    public Transform MiddleLeft => middleLeft;
    public Transform MiddleRight => middleRight;
    public List<Transform> PassThroughWaypoints => passThroughWaypoints;
    public List<Transform> GetWanderPoints()
    {
        List<Transform> points = new List<Transform>();
        if (leftEntrance != null) points.Add(leftEntrance);
        if (rightEntrance != null) points.Add(rightEntrance);
        if (middleLeft != null) points.Add(middleLeft);
        if (middleRight != null) points.Add(middleRight);
        return points;
    }
    // Auto-detected from this room's world Y position (see BaseLayoutManager.GetFloorIndexForY) the
    // moment it registers itself — there's no manually-typed field to forget to update, so a room's
    // floor can never silently drift out of sync with where it's actually placed. Rooms must be
    // vertically snapped to a consistent floorHeight grid for this to line up (same requirement
    // LiftRoom already has for stacking its segments).
    public int FloorIndex { get; private set; }
    public int GridX => Mathf.RoundToInt(transform.position.x);

    // How wide this room's footprint is in world-X units. Tracked per-instance (not just read off a
    // RoomDefinition at build time) so a future merge/upgrade system can change a room's width by
    // swapping its prefab without needing to look up catalog data it may no longer have a 1:1 entry in.
    [SerializeField] protected float footprintWidth = 4f;
    public virtual float FootprintWidth => footprintWidth;

    // Grade 1-3, for the future room-upgrade system. Every room starts at Grade 1; upgrading is a
    // separate future system that isn't built yet.
    [SerializeField] protected int grade = 1;
    public int Grade => grade;

    // Identifies a room's TYPE (e.g. "Garden", "Kitchen", "Storage Room") independent of which
    // MonoBehaviour subclass it uses — needed because purely decorative room types share the bare
    // RoomBase class with no way to tell them apart via GetType(). Authored per-prefab, same pattern
    // as footprintWidth/grade. Merge eligibility and the upgrade Swap-step catalog lookup both key off
    // this rather than the concrete component type.
    [SerializeField] protected string roomTypeId;
    public string RoomTypeId => roomTypeId;

    // Floor-aware entrance/waypoint lookups. A normal room only has one set of these regardless of
    // floor (the floorIndex parameter is ignored), but LiftRoom overrides all three to return the
    // correct set for whichever floor is actually being routed through — a lift spans multiple floors
    // and its single inherited FloorIndex only ever represents one of them.
    public virtual Transform GetLeftEntranceForFloor(int floorIndex) => leftEntrance;
    public virtual Transform GetRightEntranceForFloor(int floorIndex) => rightEntrance;
    public virtual List<Transform> GetPassThroughWaypointsForFloor(int floorIndex) => passThroughWaypoints;

    // Doorway filler/frame pieces — resolved by NAME rather than a serialized field, since most room
    // prefabs nest their wall geometry from a shared DefaultRoom instance (filler) or, for the frame,
    // are meant to differ per room type and so live directly on THIS prefab instead. A serialized
    // reference would need re-wiring on every one of the ~76 individual room prefabs the way
    // leftEntrance/rightEntrance already had to be; resolving by name at Awake needs the objects to
    // exist somewhere in this room's hierarchy, nothing more. See Docs/RoomVisualSystems_Design.md.
    private GameObject leftWallFiller;
    private GameObject rightWallFiller;
    private GameObject leftDoorFrame;
    private GameObject rightDoorFrame;

    protected virtual void Awake()
    {
        ResolveDoorwayPieces();
        ApplyRoomTheme();
    }

    private void ResolveDoorwayPieces()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "LeftWall_Filler": leftWallFiller = t.gameObject; break;
                case "RightWall_Filler": rightWallFiller = t.gameObject; break;
                case "LeftDoorFrame": leftDoorFrame = t.gameObject; break;
                case "RightDoorFrame": rightDoorFrame = t.gameObject; break;
                // Optional per-prefab icons (see Power system design) — resolved by name rather than a
                // serialized reference for the same reason as the doorway pieces above. Absent on any
                // prefab that hasn't had one added yet; the null-conditional calls in SetPowered/
                // SetWatered are safe no-ops in that case.
                case "PowerWarning": powerWarningIcon = t.gameObject; break;
                case "WaterWarning": waterWarningIcon = t.gameObject; break;
            }
        }
    }

    // Applies this room's TYPE-level palette (see RoomThemeCatalog) to whichever of its own wall/
    // ceiling/floor renderers it finds, matched by name substring rather than an exhaustive per-piece
    // list — this is what lets the wall filler (added by this same feature) and a split BackWall (an
    // independent, optional change) both pick up the right color automatically with no extra code, and
    // lets LeftDoorFrame/RightDoorFrame (which contain neither "Wall" nor "Ceiling"/"Floor") correctly
    // stay untouched, keeping whatever material their own custom art uses.
    private void ApplyRoomTheme()
    {
        RoomTheme theme = RoomThemeCatalog.Load(roomTypeId);
        if (theme == null) return; // no palette authored yet for this room type — keep the prefab's baked-in default

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            if (n.Contains("Wall") && n.Contains("Upper")) r.sharedMaterial = theme.upperWallMaterial;
            else if (n.Contains("Wall")) r.sharedMaterial = theme.lowerWallMaterial;
            else if (n.Contains("Ceiling")) r.sharedMaterial = theme.ceilingMaterial;
            // Contains rather than an exact "Floor" match — LiftRoom splits its floor into "Floor_Front"/
            // "Floor _Back" (to leave a gap for the elevator car) instead of one plain "Floor" piece like
            // every other room, so an exact match silently themed neither half.
            else if (n.Contains("Floor")) r.sharedMaterial = theme.floorMaterial;
        }
    }

    // open = true means an adjacent room is present on that side: the filler wall piece hides (the
    // doorway becomes a real opening) and the decorative frame shows (dressing the now-real opening).
    // open = false is the sealed/edge-of-base state: filler shows, frame hides. Both `?.` calls are
    // no-ops on a prefab that hasn't had one piece or the other added yet, so the wall filler and door
    // frame features can be rolled out independently without either breaking the other.
    public virtual void SetLeftDoorwayOpen(bool open)
    {
        leftWallFiller?.SetActive(!open);
        leftDoorFrame?.SetActive(open);
    }

    public virtual void SetRightDoorwayOpen(bool open)
    {
        rightWallFiller?.SetActive(!open);
        rightDoorFrame?.SetActive(open);
    }

    protected virtual void OnEnable()
    {
        if (BaseLayoutManager.Instance != null)
        {
            FloorIndex = BaseLayoutManager.Instance.GetFloorIndexForY(transform.position.y);
            BaseLayoutManager.Instance.RegisterRoom(this);
        }
        else
        {
            Debug.LogWarning($"{name}: BaseLayoutManager.Instance was null during OnEnable.");
        }

        if (consumesPower || producesPower)
        {
            powerManager = PowerManager.GetOrCreate(FloorIndex);
            if (consumesPower) powerManager.RegisterConsumer(this);
            if (producesPower) powerManager.RegisterProducer(this);
        }

        if (consumesWater)
            waterConsumptionRoutine = StartCoroutine(WaterConsumptionRoutine());
    }

    protected virtual void OnDisable()
    {
        if (BaseLayoutManager.Instance != null)
            BaseLayoutManager.Instance.UnregisterRoom(this);

        if (powerManager != null)
        {
            if (consumesPower) powerManager.UnregisterConsumer(this);
            if (producesPower) powerManager.UnregisterProducer(this);
            powerManager = null;
        }

        if (waterConsumptionRoutine != null)
        {
            StopCoroutine(waterConsumptionRoutine);
            waterConsumptionRoutine = null;
        }
    }

    // Query only — Unity gives no way to veto an in-progress Destroy(), so whatever future
    // build/demolish system removes rooms must call this BEFORE calling Destroy(), not rely on it to
    // block anything by itself. Default: blocked if any bunny is currently in or assigned to this room.
    // LiftRoom additionally requires the whole shaft (not just this segment) to be idle.
    public virtual bool CanBeDeleted(out string blockedReason)
    {
        if (DwellerRoster.Instance != null && DwellerRoster.Instance.IsRoomOccupied(this))
        {
            blockedReason = "a bunny is inside or assigned to this room";
            return false;
        }

        blockedReason = null;
        return true;
    }

    // Path for a bunny walking straight through this room (not stopping at any spot), on a specific floor.
    public List<Transform> GetPassThroughPath(bool enteringFromLeft, int floorIndex)
    {
        Transform left = GetLeftEntranceForFloor(floorIndex);
        Transform right = GetRightEntranceForFloor(floorIndex);
        List<Transform> waypoints = GetPassThroughWaypointsForFloor(floorIndex);

        List<Transform> path = new List<Transform>();

        if (enteringFromLeft)
        {
            path.Add(left);
            path.AddRange(waypoints);
            path.Add(right);
        }
        else
        {
            path.Add(right);
            List<Transform> reversed = new List<Transform>(waypoints);
            reversed.Reverse();
            path.AddRange(reversed);
            path.Add(left);
        }

        return path;
    }

    // Path from a specific entrance to a specific spot (used when this room is the FINAL destination)
    public List<Transform> GetPathBetweenEntranceAndSpot(RoomSpot spot, Transform fromEntrance)
    {
        List<Transform> match = FindMatchingPath(spot, fromEntrance);
        if (match != null) return match;

        // Fallback if no exact path defined — shouldn't normally happen once paths are set up
        Debug.LogWarning($"No RoomPath found from {fromEntrance.name} to {spot.name} on {name}.");
        return new List<Transform> { fromEntrance, spot.transform };
    }

    // Path from a specific spot OUT to a specific entrance (used when this room is the STARTING room)
    public List<Transform> GetPathFromSpotToEntrance(RoomSpot spot, Transform towardEntrance)
    {
        List<Transform> match = FindMatchingPath(spot, towardEntrance);
        if (match == null)
        {
            Debug.LogWarning($"No RoomPath found from {spot.name} to {towardEntrance.name} on {name}.");
            return new List<Transform> { spot.transform, towardEntrance };
        }

        match.Reverse();
        return match;
    }

    private List<Transform> FindMatchingPath(RoomSpot spot, Transform entrance)
    {
        foreach (RoomPath p in paths)
        {
            if (p.targetSpot == spot && p.entrance == entrance)
            {
                List<Transform> result = new List<Transform> { p.entrance };
                result.AddRange(p.waypoints);
                result.Add(spot.transform);
                return result;
            }
        }
        return null;
    }

#if UNITY_EDITOR
    // Editor-only visualization for every entrance/middle/waypoint Transform this room references —
    // added because Unity's custom-icon overlay (the small billboard icons assigned via the GameObject
    // icon picker) has a known Editor bug where it silently stops rendering until the Editor is
    // restarted. Gizmos.Draw*/Handles.Label calls here are part of the Scene view's own immediate-mode
    // render pass each frame, not the icon-atlas system that bugs out, so they can't go stale the same
    // way. Always-on (not OnDrawGizmosSelected) so these stay visible without having to click each room.
    private void OnDrawGizmos()
    {
        DrawPointGizmo(leftEntrance, Color.green);
        DrawPointGizmo(rightEntrance, Color.green);
        DrawPointGizmo(middleLeft, Color.yellow);
        DrawPointGizmo(middleRight, Color.yellow);

        if (passThroughWaypoints != null)
            foreach (Transform wp in passThroughWaypoints)
                DrawPointGizmo(wp, Color.cyan);

        if (paths != null)
        {
            foreach (RoomPath path in paths)
            {
                if (path?.waypoints == null) continue;
                foreach (Transform wp in path.waypoints)
                    DrawPointGizmo(wp, Color.magenta);
            }
        }
    }

    private static void DrawPointGizmo(Transform point, Color color)
    {
        if (point == null) return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(point.position, 0.15f);
        Gizmos.DrawLine(point.position, point.position + Vector3.up * 0.3f);

        Handles.color = color;
        Handles.Label(point.position + Vector3.up * 0.35f, point.name);
    }
#endif
}