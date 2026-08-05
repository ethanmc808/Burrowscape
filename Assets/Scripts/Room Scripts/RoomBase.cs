using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Which dedicated Animator "Working_*" state a room's workers should play (see
// WorkingWanderPoints_DesignDoc.md's Animation Override section) — a small fixed set of pre-authored
// states/clips, NOT an arbitrary AnimationClip reference. Deliberately replaces an earlier
// AnimatorOverrideController-based design that swapped clips at runtime: that approach corrupted leg
// bone playback for this rig (confirmed via isolating the wrap, the collision fix, and finally ruling
// out everything except the runtime clip-swap itself), so this rework avoids ANY runtime clip
// reassignment — WorkAnimIndex just selects between states that were always part of the graph, the
// same mechanism every other spoke (Eating/Drinking/Sleeping/etc.) already uses reliably. Adding a new
// working animation later means adding one more enum value here AND one more dedicated
// "Working_<Name>" state + transitions in NPC_Rabbit_Neutral_Controller.controller — not a code change
// beyond this enum and NPCBunny's int cast.
public enum WorkAnimationKind
{
    Idle = 0,
    Gardening = 1,
}

public class RoomBase : MonoBehaviour
{
    [Header("Power")]
    [SerializeField] protected bool consumesPower = false;
    [SerializeField] protected float powerConsumptionAmount = 1f;
    [SerializeField] protected float powerConsumptionInterval = 1f;
    [SerializeField] protected bool producesPower = false;
    [SerializeField] protected float powerProductionAmount = 1f;
    [SerializeField] protected float powerProductionInterval = 1f;
    [SerializeField] protected float powerRationingPoolAmount = 0f; // this room's contribution to the global Power Rationing Pool's max capacity — only meaningful when producesPower is true

    [Header("Water")]
    [SerializeField] protected bool consumesWater = false;
    [SerializeField] protected float waterConsumptionAmount = 1f;
    [SerializeField] protected float waterConsumptionInterval = 10f;

    [Header("Carrot Storage")]
    // Whether this room contributes to CarrotManager's storage cap at all — a separate flag rather than
    // just checking carrotStorageCapacityAmount > 0, matching the same explicit-flag-plus-amount shape as
    // consumesPower/producesPower above (Inspector clarity: a checkbox reads better than "leave the
    // number at zero"). Cafeteria is the first room type to use this; Kitchen and Cold Storage can opt in
    // later purely via the Inspector, with no script of their own required, since this lives on RoomBase.
    [SerializeField] protected bool contributesToCarrotStorage = false;
    [SerializeField] protected int carrotStorageCapacityAmount = 0; // this room's contribution to CarrotManager's storage cap — only meaningful when contributesToCarrotStorage is true

    [Header("Worker Decay Rates (while Working in this room)")]
    [SerializeField] protected float workerEnergyDecayPerSecond = 0.2f; // matches NPCBunny's prior flat default
    [SerializeField] protected float workerMoodDecayPerSecond = 0f;

    // Per-room selector for which dedicated Working_* Animator state this room's workers play — see
    // WorkAnimationKind's own comment above. Idle (0) is the default and needs no authoring; only rooms
    // wanting a distinct working pose (Garden Room -> Gardening) need to set this.
    [SerializeField] protected WorkAnimationKind workingAnimationKind = WorkAnimationKind.Idle;
    public WorkAnimationKind WorkingAnimationKind => workingAnimationKind;

    public bool ConsumesPower => consumesPower;
    public float PowerConsumptionAmount => powerConsumptionAmount;
    public float PowerConsumptionInterval => powerConsumptionInterval;
    public bool ProducesPower => producesPower;
    public float PowerProductionAmount => powerProductionAmount;
    public float PowerProductionInterval => powerProductionInterval;
    public float PowerRationingPoolAmount => powerRationingPoolAmount;
    public bool ConsumesWater => consumesWater;
    public float WaterConsumptionAmount => waterConsumptionAmount;
    public float WaterConsumptionInterval => waterConsumptionInterval;
    public bool ContributesToCarrotStorage => contributesToCarrotStorage;
    public int CarrotStorageCapacityAmount => carrotStorageCapacityAmount;
    public float WorkerEnergyDecayPerSecond => workerEnergyDecayPerSecond;
    public float WorkerMoodDecayPerSecond => workerMoodDecayPerSecond;

    // Power and Water are each arbitrated by a single global manager (PowerManager / WaterRationingManager)
    // that centrally decides, per floor, who stays on — see those classes for the rationing-pool +
    // distance-ranked shutoff logic. Both default true so a room that doesn't opt into either
    // (consumesPower/consumesWater both false) is always operational, matching pre-Power-system behavior.
    public bool IsPowered { get; private set; } = true;
    public bool IsWatered { get; private set; } = true; // irrelevant when consumesWater is false
    public bool IsOperational => IsPowered && (!consumesWater || IsWatered);

    private bool wasOperational = true;
    private PowerManager powerManager;
    private WaterRationingManager waterRationingManager;
    private GameObject powerWarningIcon;
    private GameObject waterWarningIcon;

    // Called only by the global PowerManager.
    public void SetPowered(bool powered)
    {
        if (IsPowered == powered) return;
        IsPowered = powered;
        GetComponent<RoomLightFlicker>()?.OnPowerChanged(powered);
        powerWarningIcon?.SetActive(!powered);
        RecheckOperational();
    }

    // Called only by the global WaterRationingManager (previously set internally by this room's own
    // water-consumption coroutine — that coroutine is gone; operational water consumption is now
    // centrally arbitrated the same way Power is).
    public void SetWatered(bool watered)
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

    [Header("Entrances")]
    [SerializeField] protected Transform leftEntrance;
    [SerializeField] protected Transform rightEntrance;
    [SerializeField] protected Transform middleLeft;
    [SerializeField] protected Transform middleRight;

    [Header("Pass-Through Path (for bunnies just walking through this room)")]
    [SerializeField] protected List<Transform> passThroughWaypoints; // ordered left-to-right

    [Header("Spot Paths")]
    [SerializeField] protected List<RoomPath> paths; // entrance-to-spot paths, shared by Garden/Cafeteria

    // Combat scaffolding only (see Combat_DesignDoc.md) — every room can be invaded, not just Guard
    // Rooms, so this lives on RoomBase rather than GuardRoom. 3 per room per the design doc. Nothing
    // reads this yet; the invasion/enemy-positioning system itself isn't built.
    [SpotNamePrefix("EnemySpot")]
    [SerializeField] protected List<RoomSpot> enemySpots;
    public List<RoomSpot> EnemySpots => enemySpots;

    // Defender posting spots (see Combat_DesignDoc.md, later revised away from a flat 6/room — bunnies
    // have no line-of-sight sense and would shoot through walls from an arbitrary position, so spots are
    // instead hand-authored per room type to match that room's own job-spot count, e.g. Garden's 4 farm
    // spots -> 4 CombatSpots). Lives on RoomBase (not just GuardRoom) since any room's own workers can be
    // auto-interrupted to defend where they stand, and a deployed Guard Room bunny can be sent to any
    // room's CombatSpots too. Same auto-populator convention as every other spot list.
    [SpotNamePrefix("CombatSpot")]
    [SerializeField] protected List<RoomSpot> combatSpots;
    public List<RoomSpot> CombatSpots => combatSpots;

    public RoomSpot ClaimCombatSpot(NPCBunny bunny)
    {
        if (combatSpots == null) return null;
        foreach (RoomSpot spot in combatSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseCombatSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableCombatSpot()
    {
        return combatSpots != null && combatSpots.Any(s => !s.IsOccupied);
    }

    public Transform LeftEntrance => leftEntrance;
    public Transform RightEntrance => rightEntrance;
    public Transform MiddleLeft => middleLeft;
    public Transform MiddleRight => middleRight;
    public List<Transform> PassThroughWaypoints => passThroughWaypoints;

    // Working-state local wander chain for a job spot (see WorkingWanderPoints_DesignDoc.md) — the spot
    // itself is index 0, followed by its "SpotName_WanderLocation_NN" children in name order, forming a
    // strictly linear chain (no branching). Resolved by name rather than a serialized list, same
    // reasoning as the doorway pieces above: authoring is just placing/naming child Transforms, no
    // per-spot Inspector wiring needed. Returns a single-element list (just the spot) if no wander
    // locations have been authored yet — callers treat that as "stand still", the same behavior as
    // before this feature existed, so rooms can be retrofitted one at a time.
    public List<Transform> GetWorkWanderChain(RoomSpot spot)
    {
        List<Transform> chain = new List<Transform> { spot.transform };

        string prefix = spot.name + "_WanderLocation_";
        List<Transform> matches = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith(prefix))
                matches.Add(t);
        }
        matches.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        chain.AddRange(matches);
        return chain;
    }

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

    // Save-load (and anything else that needs a room's REAL floor generically) should read this, not
    // FloorIndex directly — LiftRoom deliberately never populates FloorIndex at all (see its own
    // OnEnable override: it can't derive its floor from OnEnable the way every other room does, since
    // Unity gives no ordering guarantee across segments' Start() calls yet), tracking its true floor in
    // a separate DetectedFloorIndex property instead and overriding this to expose it. Every other room
    // just returns the normal FloorIndex.
    public virtual int EffectiveFloorIndex => FloorIndex;
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

    // Scales whatever this specific room type produces/restores — what it actually multiplies depends
    // on the room: production amount per tick for a work room (Garden/Coal/Water), or the restoration
    // rate for whichever stat(s) that room type grants (mood for Living Room; energy + mood for Bedroom;
    // hunger + mood for Cafeteria/Kitchen; thirst + mood for Water Room's drinking side). Grade 1
    // defaults to 1 (no change); Grade 2/3 prefabs are meant to be authored with a higher value (e.g.
    // 1.5/2) so upgrading a room has a real effect even when its footprint and spot count don't change.
    // Deliberately a plain per-instance value, not auto-derived from Grade, so it stays independently
    // tunable per prefab for balance.
    [SerializeField] protected float gradeMultiplier = 1f;
    public float GradeMultiplier => gradeMultiplier;

    // ---------- WORK ROOM XP (see WorkRoomXP_DesignDoc.md) ----------
    // Deliberately SEPARATE fields from gradeMultiplier/recommendedTypes above (Ethan's call — XP and
    // production are allowed to diverge in pacing later even though they start at the same shape).
    // Only meaningful on GardenRoom/WaterRoom/CoalRoom (the IJobRoom types), but lives here on RoomBase
    // since the coroutine that grants XP is shared by all three rather than tripled per room script.
    [Header("Work XP (separate from production tuning — see WorkRoomXP_DesignDoc.md)")]
    [Tooltip("Broadcast across every work room via Burrowscape > Work Room Production Tuner, same as diminishingReturnsRate/typeMatchProductionBonus.")]
    [SerializeField] protected float baseXPPerSecond = 0.25f;
    [Tooltip("Broadcast across every work room via the Tuner. 0.25 = 1.25x XP while bunny.Type is in xpRecommendedTypes.")]
    [SerializeField] protected float typeMatchXPBonus = 0.25f;
    [Tooltip("Authored per-prefab, same as gradeMultiplier (1 / 1.5 / 2 for Grade 1/2/3) — NOT broadcast by the Tuner, since it's meant to be tunable independently per room instance.")]
    [SerializeField] protected float xpGradeMultiplier = 1f;
    [Tooltip("Edited via the Tuner's per-room XP grid — its own list, separate from recommendedTypes (production's type-match list).")]
    [HideInInspector] [SerializeField] protected List<BunnyType> xpRecommendedTypes = new List<BunnyType>();

    // Fixed tick, deliberately NOT tied to any room's productionInterval/PowerProductionInterval — see
    // the design doc's "the actual fix for the tick-rate mismatch" note. One dict per room instance,
    // separate from each room's own activeProductionRoutines, so XP accrual can be started/stopped at
    // the same lifecycle points as production without sharing its clock.
    private const float WorkXPTickInterval = 1f;
    private readonly Dictionary<NPCBunny, Coroutine> activeXPRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Called by GardenRoom/WaterRoom/CoalRoom at the exact same point they start their own production
    // coroutine (NotifyBunnyReadyToWork) — see each room's own call site for why.
    protected void StartWorkXPRoutine(NPCBunny bunny)
    {
        if (activeXPRoutines.ContainsKey(bunny)) return;
        activeXPRoutines[bunny] = StartCoroutine(GrantWorkXPRoutine(bunny));
    }

    // Called by GardenRoom/WaterRoom/CoalRoom at the exact same points they stop their own production
    // coroutine (StopProductionRoutine and each room's own inline self-stop when CurrentState drifts
    // away from Working) — an eager stop rather than waiting for this routine's own next tick to notice,
    // mirroring why production does the same (avoids a stray XP tick after the bunny's already left).
    protected void StopWorkXPRoutine(NPCBunny bunny)
    {
        if (activeXPRoutines.TryGetValue(bunny, out Coroutine routine))
        {
            StopCoroutine(routine);
            activeXPRoutines.Remove(bunny);
        }
    }

    // Called by OnRoomShutdown (Power/Water cutting out) — mirrors how each room's own OnRoomShutdown
    // iterates and clears activeProductionRoutines directly rather than one bunny at a time.
    protected void StopAllWorkXPRoutines()
    {
        foreach (KeyValuePair<NPCBunny, Coroutine> kvp in activeXPRoutines)
            StopCoroutine(kvp.Value);
        activeXPRoutines.Clear();
    }

    private IEnumerator GrantWorkXPRoutine(NPCBunny bunny)
    {
        while (true)
        {
            yield return new WaitForSeconds(WorkXPTickInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeXPRoutines.Remove(bunny);
                yield break;
            }

            // NOT multiplied by bunny.XPGainMultiplier here — AddExperience itself applies that
            // universally to every XP source, this room's raw amount included. See NPCBunny.
            // AddExperience's own comment for why.
            bool typeMatch = xpRecommendedTypes != null && xpRecommendedTypes.Contains(bunny.Type);
            float rate = baseXPPerSecond * xpGradeMultiplier * (typeMatch ? 1f + typeMatchXPBonus : 1f);
            bunny.AddExperience(rate * WorkXPTickInterval);
        }
    }

    // Identifies a room's TYPE (e.g. "Garden", "Kitchen", "Storage Room") independent of which
    // MonoBehaviour subclass it uses — needed because purely decorative room types share the bare
    // RoomBase class with no way to tell them apart via GetType(). Authored per-prefab, same pattern
    // as footprintWidth/grade. Merge eligibility and the upgrade Swap-step catalog lookup both key off
    // this rather than the concrete component type.
    [SerializeField] protected string roomTypeId;
    public string RoomTypeId => roomTypeId;

    // Stable per-INSTANCE identifier (distinct from roomTypeId above, which identifies the room's TYPE,
    // shared by every room built from the same definition). Nothing needed this before the save system —
    // rooms were only ever referenced by live object reference at runtime. Generated once, the first time
    // it's empty (a fresh build), and never regenerated afterward so it survives re-serialization. A
    // save file uses this to let a bunny's room/spot assignment round-trip ("bunny X works in THIS
    // specific Garden Room"), and to key a room's own save record.
    [SerializeField] protected string instanceId;
    public string InstanceId => instanceId;

    // Save-load only — overwrites whatever Awake() just auto-generated with the exact id from a save
    // file, so a reconstructed room keeps the same identity a bunny's save record may reference. Safe to
    // call any time after Instantiate; nothing reads instanceId before SaveManager does.
    public void SetInstanceId(string id) => instanceId = id;

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
        if (string.IsNullOrEmpty(instanceId))
            instanceId = System.Guid.NewGuid().ToString();

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
            powerManager = PowerManager.EnsureInstance();
            if (consumesPower) powerManager.RegisterConsumer(this);
            if (producesPower) powerManager.RegisterProducer(this);
        }

        if (consumesWater)
        {
            waterRationingManager = WaterRationingManager.EnsureInstance();
            waterRationingManager.RegisterConsumer(this);
        }

        if (contributesToCarrotStorage)
        {
            if (CarrotManager.Instance != null)
                CarrotManager.Instance.RegisterCapacityContributor(this);
            else
                Debug.LogWarning($"{name}: CarrotManager.Instance was null during OnEnable.");
        }
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

        if (waterRationingManager != null)
        {
            waterRationingManager.UnregisterConsumer(this);
            waterRationingManager = null;
        }

        if (contributesToCarrotStorage && CarrotManager.Instance != null)
            CarrotManager.Instance.UnregisterCapacityContributor(this);
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

        // No path authored in this exact direction. If fromEntrance is itself a RoomSpot (e.g. routing
        // FROM a CombatSpot back TO a GuardSpot after StopDefendingAndReturn), try the path authored the
        // OTHER way (GuardSpot -> CombatSpot, which RoomSpotPathAutoPopulator generates automatically for
        // every occupancy-spot/CombatSpot pairing) and walk it backward — same "reuse the forward path in
        // reverse" trick GetPathFromSpotToEntrance already uses for entrance-bound trips, just generalized
        // to any spot-to-spot pairing so only one direction ever needs authoring.
        RoomSpot fromSpot = fromEntrance.GetComponent<RoomSpot>();
        if (fromSpot != null)
        {
            List<Transform> reverseMatch = FindMatchingPath(fromSpot, spot.transform);
            if (reverseMatch != null)
            {
                reverseMatch.Reverse();
                return reverseMatch;
            }
        }

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

        HashSet<Transform> referencedWaypoints = new HashSet<Transform>();

        if (passThroughWaypoints != null)
        {
            foreach (Transform wp in passThroughWaypoints)
            {
                DrawPointGizmo(wp, Color.cyan);
                if (wp != null) referencedWaypoints.Add(wp);
            }
        }

        if (paths != null)
        {
            foreach (RoomPath path in paths)
            {
                if (path?.waypoints == null) continue;
                foreach (Transform wp in path.waypoints)
                {
                    DrawPointGizmo(wp, Color.magenta);
                    if (wp != null) referencedWaypoints.Add(wp);
                }
            }
        }

        // Every "Waypoint_*" child draws even when not currently referenced by any path/pass-through
        // list, so RoomSpotPathAutoPopulator wiping paths.waypoints (by design — see that tool's header
        // comment) can no longer make these markers vanish. The Transforms themselves were never touched
        // by that tool; only RoomBase's references to them were. Drawn gray to visually distinguish
        // "exists but not wired into any path yet" from the colored/referenced states above.
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (!t.name.StartsWith("Waypoint")) continue;
            if (referencedWaypoints.Contains(t)) continue;
            DrawPointGizmo(t, Color.gray);
        }

        // "<SpotName>_WanderLocation_NN" children (see GetWorkWanderChain/WorkingWanderPoints_DesignDoc.md)
        // are deliberately plain Transforms with NO RoomSpot component — GetWorkWanderChain resolves them
        // purely by name at runtime, and a RoomSpot on one would make RoomSpotPathAutoPopulator's
        // GetComponentsInChildren<RoomSpot> scan mistake it for a real spot (its name always starts with
        // the same [SpotNamePrefix] as the spot it belongs to) and wrongly generate entrance paths for it.
        // Drawn blue to stay visually distinct from every spot/waypoint color above.
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform) continue;
            if (!t.name.Contains("_WanderLocation_")) continue;
            DrawPointGizmo(t, Color.blue);
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