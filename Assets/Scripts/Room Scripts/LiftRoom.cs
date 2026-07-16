using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public enum LiftState
{
    Idle,
    MovingToPickup,
    BoardingAtPickup,
    MovingToDropoff,
    DoorsOpenAtDropoff
}

// One bunny's request to ride from originFloor to destinationFloor.
public class LiftCall
{
    public readonly NPCBunny bunny;
    public readonly int originFloor;
    public readonly int destinationFloor;
    public bool hasArrived; // physically standing at the origin floor's landing spot, ready to board

    public LiftCall(NPCBunny bunny, int originFloor, int destinationFloor)
    {
        this.bunny = bunny;
        this.originFloor = originFloor;
        this.destinationFloor = destinationFloor;
    }
}

// A single-floor SEGMENT of a lift shaft. Place one of these per floor you want a lift to reach and
// stack them directly on top of each other — same X position, floors immediately adjacent, no gaps —
// and they automatically link up into one shared lift at runtime. There's no manual wiring between
// segments: every segment auto-detects its own floor from its Y position (same as any normal room),
// and whichever segment ends up on the LOWEST floor number in a contiguous stack becomes the
// "coordinator" that actually runs the shared state machine (queue, boarding) — every other segment
// in that stack just contributes its own floor + landing/boarding spot + doors to the coordinator.
// A lone segment with nothing stacked above or below it is still valid (just a one-floor, not very
// useful, lift). This is what lets the player build/extend a lift modularly, floor by floor, without
// the lift needing a predetermined size or position.
//
// There's no visible "car" — each segment has its own doors (purely visual, optional), and the
// coordinator opens/closes the doors on whichever specific floor it's currently stopped at. A bunny
// hides itself the moment it steps behind closed doors and reappears when they open on its
// destination floor, so the player never sees it mid-transit and there's nothing that needs a single
// shared moving model spanning the whole shaft.
//
// Pickup phase: floor calls are served oldest-first (FIFO). On arrival, EVERY bunny already waiting
// at that floor's landing spot boards together, regardless of individual destination. Latecomers get
// a grace period to still make it; after that the doors close and their floor is re-queued so the
// lift eventually comes back for them instead of stranding them.
//
// Drop-off phase: once boarding closes, the lift serves the boarded riders' distinct destination
// floors nearest-first, recomputed from its current position after every stop.
public class LiftRoom : RoomBase
{
    private static readonly List<LiftRoom> allSegments = new List<LiftRoom>();

    [Header("This Floor's Boarding Point")]
    [SerializeField] private Transform landingSpot; // where bunnies wait to board / are dropped off on this floor
    [Tooltip("Where bunnies actually step onto the car, near the shaft opening. Leave unset to fall back to Landing Spot.")]
    [SerializeField] private Transform boardingSpot;

    [Header("This Floor's Doors (optional, purely visual)")]
    [SerializeField] private Transform doorTransform;
    [SerializeField] private Vector3 doorClosedLocalPosition;
    [SerializeField] private Vector3 doorOpenLocalPosition;
    [SerializeField] private float doorAnimDuration = 0.5f;

    [Header("Lift-Wide Settings (only used by whichever segment becomes the coordinator)")]
    [SerializeField] private float travelTimePerFloor = 1f; // seconds to move between two adjacent floors
    [SerializeField] private float boardingGracePeriod = 3f; // seconds to wait for stragglers once doors open at pickup
    [SerializeField] private float dropoffDwellTime = .5f; // seconds doors stay open at a drop-off before moving on

    public int DetectedFloorIndex { get; private set; }
    public LiftState CurrentState { get; private set; } = LiftState.Idle;
    public int CurrentFloor { get; private set; }

    // Always exactly 1 unit wide, regardless of any Inspector-set footprintWidth — a Lift segment never
    // has a merged/upgraded variant (it extends vertically, not horizontally), so there's no reason to
    // risk a per-prefab Inspector value drifting from the one width that actually matters for grid math.
    public override float FootprintWidth => 1f;

    private LiftRoom coordinator; // the segment actually running the shared state machine (may be `this`)
    private List<LiftRoom> shaftSegments; // only populated on the coordinator: every segment in this shaft

    private float stateTimer;
    private Coroutine doorRoutine;

    private readonly List<LiftCall> activeCalls = new List<LiftCall>(); // called, not yet boarded
    private readonly List<LiftCall> boardedRiders = new List<LiftCall>(); // currently riding
    private readonly List<int> pickupCallQueue = new List<int>(); // FIFO floor indices awaiting a pickup visit
    private readonly HashSet<int> queuedPickupFloors = new HashSet<int>(); // dedupe guard for pickupCallQueue

    private int pickupTargetFloor;
    private int dropoffTargetFloor;

    private void Awake()
    {
        allSegments.Add(this);
    }

    private void OnDestroy()
    {
        allSegments.Remove(this);

        // Unlike OnEnable's regroup request (which needs DetectedFloorIndex, only known after Start()),
        // a removal doesn't need anything about this segment — the regroup just needs to see that it's
        // gone from allSegments (already true by the time the deferred pass runs next frame) to correctly
        // split/shrink whatever shaft it used to belong to.
        BaseLayoutManager.Instance?.RequestLiftColumnRegroup(GridX);
    }

    protected override void OnEnable()
    {
        // Deliberately NOT calling base.OnEnable(): this segment's real floor isn't known from its
        // manually-typed Floor Index (that's not how lift segments are meant to be configured) —
        // it's auto-detected from Y position in Start(), once every segment has Awake'd and the
        // shaft-grouping pass can see the whole scene.
    }

    protected override void OnDisable()
    {
        BaseLayoutManager.Instance?.UnregisterRoomOnFloor(this, DetectedFloorIndex);
        if (coordinator == this)
            BaseLayoutManager.Instance?.UnregisterLift(this);
    }

    private void Start()
    {
        DetectedFloorIndex = BaseLayoutManager.Instance != null
            ? BaseLayoutManager.Instance.GetFloorIndexForY(transform.position.y)
            : FloorIndex;

        BaseLayoutManager.Instance?.RegisterRoomOnFloor(this, DetectedFloorIndex);

        // Deferred by a frame via BaseLayoutManager (see RequestLiftColumnRegroup): Unity only
        // guarantees every Awake() runs before any Start(), not that Start() itself runs in any
        // particular order across segments. Grouping reads DetectedFloorIndex off every OTHER segment
        // in the column too, so if it ran here (inline, in Start()) whichever segment's Start()
        // happened to fire first would group its siblings before their own Start() had set their
        // DetectedFloorIndex — reading them as the default 0 and fracturing one shaft into several
        // bogus single-floor ones. Waiting a frame guarantees every segment already has its real
        // DetectedFloorIndex by the time any of them actually groups. The same request also fires
        // whenever a segment is later added or removed at runtime (plopped/demolished rooms), not
        // just at scene load — see BaseLayoutManager.RequestLiftColumnRegroup and OnDestroy below.
        BaseLayoutManager.Instance?.RequestLiftColumnRegroup(GridX);
    }

    // Splits every LiftRoom segment CURRENTLY sharing this X position into contiguous floor runs —
    // each run is an independent shaft. Re-runnable: called by BaseLayoutManager (deferred a frame)
    // every time a segment is added or removed anywhere in this column, not just once at scene load —
    // that's what lets a shaft extend, shrink, split, or merge with another as rooms get built/demolished.
    public static void RegroupColumn(int gridX)
    {
        List<LiftRoom> segmentsInColumn = allSegments.Where(s => s.GridX == gridX).ToList();

        // Retire every coordinator CURRENTLY appointed among these segments before recomputing runs.
        // Without this, a segment that stops being a coordinator (e.g. two shafts merge because a new
        // segment fills the gap between them) would leave a stale duplicate entry in
        // BaseLayoutManager's lift list and a stale shaftSegments list behind. Safe against segments
        // that were destroyed as part of this same change — Unity's Object == override makes a
        // reference to a destroyed segment compare equal to null.
        List<LiftRoom> oldCoordinators = segmentsInColumn.Select(s => s.coordinator).Where(c => c != null).Distinct().ToList();
        foreach (LiftRoom oldCoordinator in oldCoordinators)
        {
            BaseLayoutManager.Instance?.UnregisterLift(oldCoordinator);
            oldCoordinator.shaftSegments = null;
        }

        segmentsInColumn.Sort((a, b) => a.DetectedFloorIndex.CompareTo(b.DetectedFloorIndex));

        List<LiftRoom> currentRun = null;
        int previousFloor = int.MinValue;

        foreach (LiftRoom segment in segmentsInColumn)
        {
            if (currentRun == null || segment.DetectedFloorIndex != previousFloor + 1)
            {
                if (currentRun != null)
                    FinalizeShaft(currentRun);
                currentRun = new List<LiftRoom>();
            }
            currentRun.Add(segment);
            previousFloor = segment.DetectedFloorIndex;
        }

        if (currentRun != null)
            FinalizeShaft(currentRun);
    }

    private static void FinalizeShaft(List<LiftRoom> run)
    {
        LiftRoom lead = run[0]; // lowest floor number in the run — deterministic coordinator choice
        foreach (LiftRoom segment in run)
            segment.coordinator = lead;

        lead.shaftSegments = run;
        lead.CurrentFloor = lead.DetectedFloorIndex;
        BaseLayoutManager.Instance?.RegisterLift(lead);

        Debug.Log($"{lead.name}: shaft formed spanning floor(s) {string.Join(", ", run.Select(s => s.DetectedFloorIndex))}.");
    }

    private LiftRoom FindSegment(int floorIndex)
    {
        return shaftSegments?.FirstOrDefault(s => s.DetectedFloorIndex == floorIndex);
    }

    public LiftRoom GetSegmentForFloor(int floorIndex) => FindSegment(floorIndex);

    public bool ServicesFloor(int floorIndex) => FindSegment(floorIndex) != null;

    public Transform GetLandingSpot(int floorIndex) => FindSegment(floorIndex)?.landingSpot;

    // Falls back to the landing spot if no distinct boarding spot is configured for this floor.
    public Transform GetBoardingSpot(int floorIndex)
    {
        LiftRoom segment = FindSegment(floorIndex);
        if (segment == null) return null;
        return segment.boardingSpot != null ? segment.boardingSpot : segment.landingSpot;
    }

    // A bunny mid-trip may not have currentRoom pointing at the specific segment being deleted (e.g.
    // it's invisible and "at" its origin segment while actually riding toward a different floor), so
    // the base per-segment occupancy check alone isn't enough — the whole shaft must be idle too.
    public override bool CanBeDeleted(out string blockedReason)
    {
        if (!base.CanBeDeleted(out blockedReason)) return false;

        LiftRoom shaftCoordinator = coordinator != null ? coordinator : this;
        if (shaftCoordinator.CurrentState != LiftState.Idle
            || shaftCoordinator.activeCalls.Count > 0
            || shaftCoordinator.boardedRiders.Count > 0
            || shaftCoordinator.pickupCallQueue.Count > 0)
        {
            blockedReason = "this lift shaft is currently in use";
            return false;
        }

        blockedReason = null;
        return true;
    }

    private void OpenDoorsOnFloor(int floorIndex) => FindSegment(floorIndex)?.OpenDoors();
    private void CloseDoorsOnFloor(int floorIndex) => FindSegment(floorIndex)?.CloseDoors();

    public void OpenDoors() => AnimateDoors(doorOpenLocalPosition);
    public void CloseDoors() => AnimateDoors(doorClosedLocalPosition);

    private void AnimateDoors(Vector3 targetLocalPosition)
    {
        if (doorTransform == null) return;

        if (doorRoutine != null)
            StopCoroutine(doorRoutine);
        doorRoutine = StartCoroutine(AnimateDoorRoutine(targetLocalPosition));
    }

    private IEnumerator AnimateDoorRoutine(Vector3 targetLocalPosition)
    {
        Vector3 start = doorTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < doorAnimDuration)
        {
            elapsed += Time.deltaTime;
            doorTransform.localPosition = Vector3.Lerp(start, targetLocalPosition, Mathf.Clamp01(elapsed / doorAnimDuration));
            yield return null;
        }

        doorTransform.localPosition = targetLocalPosition;
    }

    // Called once a bunny commits to riding this lift — before it's necessarily arrived at the landing spot.
    // Safe to call on ANY segment in the shaft, not just the coordinator — activeCalls/pickupCallQueue
    // only actually get drained by the coordinator's own Update() (see the `if (coordinator != this)
    // return;` guard there), so a call landing on a non-coordinator segment would otherwise sit in a
    // list nobody's ticking, either failing loudly (RequestLift, via ServicesFloor returning false
    // since shaftSegments is only populated on the coordinator) or silently doing nothing
    // (NotifyArrivedAtLanding, since activeCalls would always be empty on a non-coordinator).
    // Falls back to `this` if called before grouping has assigned a coordinator yet.
    public void RequestLift(NPCBunny bunny, int originFloor, int destinationFloor)
    {
        LiftRoom target = coordinator != null ? coordinator : this;
        if (target != this)
        {
            target.RequestLift(bunny, originFloor, destinationFloor);
            return;
        }

        if (!ServicesFloor(originFloor) || !ServicesFloor(destinationFloor))
        {
            Debug.LogWarning($"{name}: lift requested between floors {originFloor} and {destinationFloor}, but doesn't service both.");
            return;
        }

        activeCalls.Add(new LiftCall(bunny, originFloor, destinationFloor));
        EnqueuePickupFloor(originFloor);
    }

    // Called once a bunny physically reaches its origin floor's landing spot.
    public void NotifyArrivedAtLanding(NPCBunny bunny, int floorIndex)
    {
        LiftRoom target = coordinator != null ? coordinator : this;
        if (target != this)
        {
            target.NotifyArrivedAtLanding(bunny, floorIndex);
            return;
        }

        LiftCall call = activeCalls.FirstOrDefault(c => c.bunny == bunny && c.originFloor == floorIndex);
        if (call != null)
            call.hasArrived = true;
    }

    private void EnqueuePickupFloor(int floorIndex)
    {
        if (queuedPickupFloors.Contains(floorIndex)) return;

        pickupCallQueue.Add(floorIndex);
        queuedPickupFloors.Add(floorIndex);
    }

    private void Update()
    {
        if (coordinator != this) return; // only the coordinator runs the shared state machine

        switch (CurrentState)
        {
            case LiftState.Idle:
                TickIdle();
                break;
            case LiftState.MovingToPickup:
                TickTravel(pickupTargetFloor, OnArrivedAtPickupFloor);
                break;
            case LiftState.BoardingAtPickup:
                TickBoarding();
                break;
            case LiftState.MovingToDropoff:
                TickTravel(dropoffTargetFloor, OnArrivedAtDropoffFloor);
                break;
            case LiftState.DoorsOpenAtDropoff:
                TickDropoffDwell();
                break;
        }
    }

    private void TickIdle()
    {
        if (pickupCallQueue.Count == 0) return;

        pickupTargetFloor = pickupCallQueue[0];
        pickupCallQueue.RemoveAt(0);
        queuedPickupFloors.Remove(pickupTargetFloor);

        BeginTravel(pickupTargetFloor, LiftState.MovingToPickup);
    }

    private void BeginTravel(int targetFloor, LiftState travelState)
    {
        stateTimer = Mathf.Abs(targetFloor - CurrentFloor) * travelTimePerFloor;
        CurrentState = travelState;
        Debug.Log($"{name}: moving from floor {CurrentFloor} to floor {targetFloor} ({stateTimer:F1}s).");
    }

    private void TickTravel(int targetFloor, System.Action onArrived)
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        CurrentFloor = targetFloor;
        onArrived();
    }

    private void OnArrivedAtPickupFloor()
    {
        Debug.Log($"{name}: doors open at floor {CurrentFloor} for boarding.");
        CurrentState = LiftState.BoardingAtPickup;
        stateTimer = boardingGracePeriod;
        OpenDoorsOnFloor(CurrentFloor);
        BoardArrivedRiders();
    }

    private void TickBoarding()
    {
        BoardArrivedRiders();

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        CloseBoardingAndDepart();
    }

    private void BoardArrivedRiders()
    {
        Transform boardingSpotForFloor = GetBoardingSpot(CurrentFloor);

        for (int i = activeCalls.Count - 1; i >= 0; i--)
        {
            LiftCall call = activeCalls[i];
            if (call.originFloor != CurrentFloor || !call.hasArrived) continue;

            activeCalls.RemoveAt(i);
            boardedRiders.Add(call);
            call.bunny.BoardLift(boardingSpotForFloor);
            Debug.Log($"{name}: {call.bunny.name} boarded at floor {CurrentFloor}, heading to floor {call.destinationFloor}.");
        }
    }

    private void CloseBoardingAndDepart()
    {
        // Anyone who called this floor but never arrived in time is left behind — re-queue their
        // floor so the lift eventually swings back for them instead of stranding them forever.
        foreach (LiftCall straggler in activeCalls)
            if (straggler.originFloor == CurrentFloor)
                EnqueuePickupFloor(straggler.originFloor);

        CloseDoorsOnFloor(CurrentFloor);

        if (boardedRiders.Count == 0)
        {
            CurrentState = LiftState.Idle;
            return;
        }

        BeginNextDropoff();
    }

    private void BeginNextDropoff()
    {
        // Nearest-remaining-destination-first, recomputed from the CURRENT floor every time — this
        // is what lets a multi-passenger trip reverse direction between stops when that's shorter.
        dropoffTargetFloor = boardedRiders
            .Select(r => r.destinationFloor)
            .Distinct()
            .OrderBy(f => Mathf.Abs(f - CurrentFloor))
            .First();

        BeginTravel(dropoffTargetFloor, LiftState.MovingToDropoff);
    }

    private void OnArrivedAtDropoffFloor()
    {
        Debug.Log($"{name}: doors open at floor {CurrentFloor} for drop-off.");
        CurrentState = LiftState.DoorsOpenAtDropoff;
        stateTimer = dropoffDwellTime;
        OpenDoorsOnFloor(CurrentFloor);
        DisembarkArrivedRiders();
    }

    private void DisembarkArrivedRiders()
    {
        Transform landingSpotForFloor = GetLandingSpot(CurrentFloor);
        Transform boardingSpotForFloor = GetBoardingSpot(CurrentFloor);

        for (int i = boardedRiders.Count - 1; i >= 0; i--)
        {
            LiftCall rider = boardedRiders[i];
            if (rider.destinationFloor != CurrentFloor) continue;

            boardedRiders.RemoveAt(i);
            rider.bunny.DisembarkFromLift(landingSpotForFloor, boardingSpotForFloor, CurrentFloor);
            Debug.Log($"{name}: {rider.bunny.name} disembarked at floor {CurrentFloor}.");
        }
    }

    private void TickDropoffDwell()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        CloseDoorsOnFloor(CurrentFloor);

        if (boardedRiders.Count > 0)
            BeginNextDropoff();
        else
            CurrentState = LiftState.Idle;
    }
}
