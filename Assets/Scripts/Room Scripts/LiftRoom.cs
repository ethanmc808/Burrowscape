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

    // Each panel's own Transform is a fixed anchor pinned to its outer edge (against the side wall) —
    // its visible mesh is a CHILD offset inward, so animating the anchor's own localScale.x shrinks the
    // panel toward the wall it's pinned to rather than translating it. That's deliberate: a Lift segment
    // is only 1 unit wide, so a door that visually SLIDES sideways to open would have nowhere to go
    // without poking into whatever's built next to the lift; a squish never moves past its own
    // full-closed footprint, so there's nothing to clip regardless of how little clearance there is.
    [Header("This Floor's Doors (optional, purely visual)")]
    [SerializeField] private Transform leftDoorPanel;
    [SerializeField] private Transform rightDoorPanel;
    [Tooltip("Doors only open once a bunny is already standing at the landing spot, so this can be snappy.")]
    [SerializeField] private float doorOpenDuration = 0.2f;
    [Tooltip("Slower than opening on purpose, to give a boarding/disembarking bunny time to fully clear the doorway before it visually closes.")]
    [SerializeField] private float doorCloseDuration = 0.6f;

    private Vector3 leftDoorClosedScale;
    private Vector3 rightDoorClosedScale;

    [Header("Lift-Wide Settings (only used by whichever segment becomes the coordinator)")]
    [SerializeField] private float travelTimePerFloor = 1f; // seconds to move between two adjacent floors
    [Tooltip("Seconds to wait for stragglers once the doors are actually fully open (not from the moment the lift arrives) — ends early if nobody's left to wait for. See TickBoarding's BoardingPhase.")]
    [SerializeField] private float boardingGracePeriod = 3f;
    [Tooltip("Extra pause stacked on top of Boarding Grace Period once it elapses — purely so the doors visibly stay open a beat longer before closing, independent of the straggler wait above.")]
    [SerializeField] private float doorHoldOpenDuration = 1f;
    [SerializeField] private float dropoffDwellTime = .5f; // seconds doors stay open at a drop-off before moving on, once actually open

    public int DetectedFloorIndex { get; private set; }

    // True only once Start() has actually set DetectedFloorIndex and registered with BaseLayoutManager —
    // guards OnDisable() against unregistering using the bogus default value (0) if this instance is
    // destroyed before its own Start() ever ran. This happens specifically when RoomGhostPreview measures
    // a Lift prefab's bounds: it instantiates a temp copy and destroys it synchronously in the same call,
    // before Unity ever gets to that temp object's Start() — without this guard, the temp object's
    // OnDisable() would call UnregisterRoomOnFloor(this, 0), which always fires a "floor 0 changed" event
    // regardless of whether anything was actually registered there, incorrectly signaling that a floor
    // ABOVE the entrance just changed.
    private bool hasRegisteredFloor;
    public LiftState CurrentState { get; private set; } = LiftState.Idle;
    public int CurrentFloor { get; private set; }

    // Per-segment — true once THIS segment's own doors have fully finished an open animation, false once
    // a close animation starts/finishes. Defaults to true if this segment has no door panels wired up at
    // all, so a lift without door visuals authored behaves as it always did (nothing to wait for).
    // Queried by the coordinator (which may be a different segment — see FindSegment) so its own
    // boarding/dropoff timers don't start counting down until the doors at the ACTUAL floor being
    // serviced are visibly open, not just the moment the lift's abstract travel finishes.
    public bool DoorsFullyOpen { get; private set; }

    // Always exactly 1 unit wide, regardless of any Inspector-set footprintWidth — a Lift segment never
    // has a merged/upgraded variant (it extends vertically, not horizontally), so there's no reason to
    // risk a per-prefab Inspector value drifting from the one width that actually matters for grid math.
    public override float FootprintWidth => 1f;

    private LiftRoom coordinator; // the segment actually running the shared state machine (may be `this`)
    private List<LiftRoom> shaftSegments; // only populated on the coordinator: every segment in this shaft

    // Two sub-phases of "boarding window open at a pickup floor": wait for stragglers (but end EARLY the
    // moment nobody's left to wait for — see TickBoarding), then a final fixed cosmetic pause that always
    // runs its full duration regardless. Deliberately NOT gated on the doors having visibly finished
    // opening first — that was tried and caused a real stall: if the bunny who called the lift never
    // physically reaches the landing spot (for any reason, e.g. an unrelated pathing hiccup or a
    // reassignment mid-walk), the doors never open, so a wait gated on "doors are open" never ends —
    // freezing the WHOLE coordinator forever, not just that one bunny, since nothing else can use this
    // shaft while its single state machine is stuck. Both phases below are bounded no matter what.
    private enum BoardingPhase { WaitingForStragglers, HoldingOpen }

    private float stateTimer;
    private BoardingPhase boardingPhase;
    // Unlike boardingPhase, this one is safe to gate on DoorsFullyOpen with no timeout — OnArrivedAtDropoffFloor
    // always opens the door unconditionally (nothing bunny-dependent to wait on), so it's bounded by the
    // door's own fixed animation duration and can never stall the way the boarding wait could.
    private bool dropoffCountdownStarted;
    private Coroutine doorRoutine;

    private readonly List<LiftCall> activeCalls = new List<LiftCall>(); // called, not yet boarded
    private readonly List<LiftCall> boardedRiders = new List<LiftCall>(); // currently riding
    private readonly List<int> pickupCallQueue = new List<int>(); // FIFO floor indices awaiting a pickup visit
    private readonly HashSet<int> queuedPickupFloors = new HashSet<int>(); // dedupe guard for pickupCallQueue

    private int pickupTargetFloor;
    private int dropoffTargetFloor;

    // Must explicitly chain to base.Awake() — Unity's Awake dispatch isn't governed by C# virtual
    // rules the way OnEnable is elsewhere in this hierarchy; a base-class Awake() and a derived-class
    // Awake() are two distinct methods unless the derived one is declared override and calls base
    // itself, so without this RoomBase's doorway-filler/theme resolution (see RoomBase.Awake) would
    // silently never run on any LiftRoom instance.
    protected override void Awake()
    {
        base.Awake();
        allSegments.Add(this);

        // Cache each panel's authored (closed) scale once — "open" is just this same scale with X
        // zeroed, so there's no separate open/closed value to hand-author or keep in sync.
        if (leftDoorPanel != null) leftDoorClosedScale = leftDoorPanel.localScale;
        if (rightDoorPanel != null) rightDoorClosedScale = rightDoorPanel.localScale;

        // No door panels wired up means nothing to visibly wait for — treat as already "open" so the
        // coordinator's boarding/dropoff timers never stall waiting for an animation that will never run.
        DoorsFullyOpen = leftDoorPanel == null || rightDoorPanel == null;
    }

    // FootprintWidth (below) already hardcodes the value every placement/grid check actually reads, so
    // this is purely cosmetic — but the INHERITED footprintWidth FIELD still defaults to RoomBase's 4
    // and nothing ever corrects it, which left the Inspector showing a stale, totally-ignored "4" on
    // every Lift prefab. OnValidate runs automatically in the Editor (on load and on any Inspector
    // change) so the field self-corrects without ever needing to be hand-edited in the prefab.
    private void OnValidate()
    {
        footprintWidth = 1f;
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
        if (hasRegisteredFloor)
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
        hasRegisteredFloor = true;

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

        DebugLog.Log($"{lead.name}: shaft formed spanning floor(s) {string.Join(", ", run.Select(s => s.DetectedFloorIndex))}.");
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

    public void OpenDoors() => AnimateDoors(opening: true);
    public void CloseDoors() => AnimateDoors(opening: false);

    private void AnimateDoors(bool opening)
    {
        if (leftDoorPanel == null || rightDoorPanel == null) return;

        DoorsFullyOpen = false; // true only once the target animation actually finishes, below — covers both directions
        if (doorRoutine != null)
            StopCoroutine(doorRoutine);
        doorRoutine = StartCoroutine(AnimateDoorRoutine(opening));
    }

    // Squishes both panels' anchor scale toward zero-width (open) or back to their authored full width
    // (closed) together, in lockstep — only the X component changes; Y/Z stay at their closed values the
    // whole time, so the panel never gets shorter/thinner, only narrower as it retreats into the wall.
    private IEnumerator AnimateDoorRoutine(bool opening)
    {
        Vector3 leftStart = leftDoorPanel.localScale;
        Vector3 rightStart = rightDoorPanel.localScale;

        Vector3 leftTarget = opening ? new Vector3(0f, leftDoorClosedScale.y, leftDoorClosedScale.z) : leftDoorClosedScale;
        Vector3 rightTarget = opening ? new Vector3(0f, rightDoorClosedScale.y, rightDoorClosedScale.z) : rightDoorClosedScale;

        float duration = opening ? doorOpenDuration : doorCloseDuration;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            leftDoorPanel.localScale = Vector3.Lerp(leftStart, leftTarget, t);
            rightDoorPanel.localScale = Vector3.Lerp(rightStart, rightTarget, t);
            yield return null;
        }

        leftDoorPanel.localScale = leftTarget;
        rightDoorPanel.localScale = rightTarget;
        DoorsFullyOpen = opening;
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
        if (call == null) return;

        call.hasArrived = true;

        // The bunny calls the lift and starts walking toward the landing spot immediately (see
        // TryBeginCrossFloorTripToSpot), well before the lift's own abstract travel-time countdown
        // necessarily finishes — so a bunny reaching the landing spot AFTER the lift already arrived and
        // started boarding is a real, common ordering. Doors must open right here in that case, since
        // OnArrivedAtPickupFloor's own check already ran and found nobody arrived yet. If instead the
        // bunny arrives BEFORE the lift does, this is a no-op (wrong state/floor) and
        // OnArrivedAtPickupFloor's own check picks it up once the lift actually gets there.
        if (CurrentState == LiftState.BoardingAtPickup && floorIndex == CurrentFloor)
            OpenDoorsOnFloor(floorIndex);
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
        DebugLog.Log($"{name}: moving from floor {CurrentFloor} to floor {targetFloor} ({stateTimer:F1}s).");
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
        DebugLog.Log($"{name}: arrived at floor {CurrentFloor}, boarding window open.");
        CurrentState = LiftState.BoardingAtPickup;
        boardingPhase = BoardingPhase.WaitingForStragglers;
        stateTimer = boardingGracePeriod;
        // Doors only open once a bunny is actually AT the landing spot, not the instant the lift itself
        // arrives — a bunny that's still walking there shouldn't see the doors pop open early. If
        // someone already reached the landing spot before the lift's own travel finished (hasArrived set
        // via NotifyArrivedAtLanding), open right away; otherwise NotifyArrivedAtLanding opens them the
        // moment the first bunny actually gets here. Either way, this is purely a VISUAL trigger now —
        // it no longer gates how long the state machine waits (see the enum comment above for why).
        if (activeCalls.Any(c => c.originFloor == CurrentFloor && c.hasArrived))
            OpenDoorsOnFloor(CurrentFloor);
        BoardArrivedRiders();
    }

    private void TickBoarding()
    {
        BoardArrivedRiders();

        switch (boardingPhase)
        {
            case BoardingPhase.WaitingForStragglers:
                {
                    stateTimer -= Time.deltaTime;
                    // End early the moment nobody who called this floor is still out there mid-walk — no
                    // sense burning the full grace period waiting for a straggler who doesn't exist (e.g. a
                    // single bunny that already boarded). The stateTimer <= 0f fallback still applies
                    // regardless, so a genuine straggler who never shows up (or a call that's gone stale for
                    // some other reason) is still capped at boardingGracePeriod and re-queued as before (see
                    // CloseBoardingAndDepart) — the lift moves on instead of freezing forever either way.
                    bool stragglersRemain = activeCalls.Any(c => c.originFloor == CurrentFloor && !c.hasArrived);
                    if (stragglersRemain && stateTimer > 0f) return;
                    boardingPhase = BoardingPhase.HoldingOpen;
                    stateTimer = doorHoldOpenDuration;
                    break;
                }

            case BoardingPhase.HoldingOpen:
                // Fixed, unconditional pause — purely cosmetic (lets the door visibly stay open a beat
                // longer), so unlike the phase above this always runs its full duration regardless of
                // whether anyone's still around.
                stateTimer -= Time.deltaTime;
                if (stateTimer > 0f) return;
                CloseBoardingAndDepart();
                break;
        }
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
            DebugLog.Log($"{name}: {call.bunny.name} boarded at floor {CurrentFloor}, heading to floor {call.destinationFloor}.");
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
        DebugLog.Log($"{name}: doors open at floor {CurrentFloor} for drop-off.");
        CurrentState = LiftState.DoorsOpenAtDropoff;
        dropoffCountdownStarted = false; // see TickDropoffDwell — mirrors the same fix as pickup boarding
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
            DebugLog.Log($"{name}: {rider.bunny.name} disembarked at floor {CurrentFloor}.");
        }
    }

    private void TickDropoffDwell()
    {
        if (!dropoffCountdownStarted)
        {
            // Same reasoning as TickBoarding: don't start dropoffDwellTime until the doors have actually
            // finished opening, not the instant the lift arrives — otherwise the door-open animation's
            // own duration silently eats into the dwell time.
            if (!(FindSegment(CurrentFloor)?.DoorsFullyOpen ?? true)) return;

            dropoffCountdownStarted = true;
            stateTimer = dropoffDwellTime;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        CloseDoorsOnFloor(CurrentFloor);

        if (boardedRiders.Count > 0)
            BeginNextDropoff();
        else
            CurrentState = LiftState.Idle;
    }
}