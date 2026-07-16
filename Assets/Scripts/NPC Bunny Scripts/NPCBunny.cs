using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BunnyState
{
    Idle,
    MovingToSpot,
    Working,
    Eating,
    Wandering,
    PassingGate,
    Despawning,
    WaitingForLift,
    RidingLift,
    DisembarkingLift,
}
public enum BunnyArrivalType
{
    Wild,
    ReturningFromQuest
}
public enum BunnyGender
{
    Male,
    Female
}

[RequireComponent(typeof(Animator))]
public class NPCBunny : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.5f;
    [SerializeField] private float wanderSpeedMultiplier = 0.5f; // wandering bunnies move slower than working/eating bunnies
    [SerializeField] private float arrivalThreshold = 0.05f;

    [Header("Hunger")]
    [SerializeField] private float hunger = 100f; // 0-100
    [SerializeField] private float hungerDecayPerSecond = 0.1f;
    [SerializeField] private float hungerThresholdLow = 35f;
    [SerializeField] private float hungerThresholdFull = 75f;
    [SerializeField] private float hungerGainPerCarrot = 25f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isMovingParam = "IsMoving";
    [SerializeField] private string isWorkingParam = "IsWorking";
    [SerializeField] private string isEatingParam = "IsEating";

    [Header("Facing Direction")]
    [SerializeField] private Transform bunnyScaleRoot;
    [SerializeField] private bool bunnyFacesLeftByDefault = true;

    [Header("Wandering")]
    [SerializeField] private float wanderPauseDuration = 3f; // how long to idle at each stop before moving again
    [SerializeField] private float crossFloorWanderChance = 0.2f; // chance a wander pick is on a different floor (via lift) instead of the current one
    private bool isWanderingEnabled = false;
    private float wanderTimer = 0f;

    [Header("Lift Visual Timing")]
    [Tooltip("Delay after reaching the boarding spot before vanishing, so a doors-closing animation has time to play first.")]
    [SerializeField] private float liftBoardHideDelay = 0.5f;
    [Tooltip("Delay after arriving at the destination floor before reappearing and walking out, so a doors-opening animation has time to play first.")]
    [SerializeField] private float liftDisembarkRevealDelay = 0.5f;

    public BunnyState CurrentState { get; private set; } = BunnyState.Idle;
    public bool IsHungry => hunger < hungerThresholdLow;
    public bool IsAssignedToJob => assignedJobRoom != null;
    public IJobRoom AssignedJobRoom => assignedJobRoom;
    // True once the bunny has actually passed through the entrance gate (set in EnterBaseAndWander,
    // which only ever runs from OnArrivedAtGateExit). False while spawned-but-queued or still awaiting
    // approval — a bunny in that state has no currentRoom yet, so routing it to a job would build a
    // path straight from the base entrance to the job room, skipping the gate entirely.
    public bool HasEnteredBase { get; private set; }
    public bool IsAwaitingApproval { get; private set; }
    public BunnyArrivalType ArrivalType { get; private set; } = BunnyArrivalType.Wild;
    public BunnyGender Gender { get; private set; } = BunnyGender.Male;
    public string BunnyName { get; private set; } = "Unnamed";

    private RoomSpot currentTargetSpot;
    private Queue<Transform> currentPath;
    private Transform currentWaypointTarget;
    private RoomSpot claimedWorkSpot; // remembers the garden spot to return to after eating
    private IJobRoom assignedJobRoom;
    private CafeteriaRoom cafeteriaBeingUsed;
    private RoomBase currentRoom;
    private RoomSpot currentSpot; // the spot bunny is currently occupying, null if none/mid-transit
    private Transform currentWanderPoint; // last wander destination reached, null if occupying a RoomSpot instead

    // The floor the bunny is ACTUALLY on right now. Tracked separately from currentRoom.FloorIndex
    // because currentRoom can be a LiftRoom, whose own FloorIndex only ever represents its primary
    // floor — not necessarily the floor of the specific landing spot the bunny is currently standing at.
    private int currentFloorIndex = 0;
    private RoomBase pendingArrivalRoom; // room to attribute to the bunny once it finishes crossing the gate
    private Transform pendingArrivalPoint; // gate exit point, becomes the bunny's known position on arrival

    // A picked-but-not-yet-reached wander destination. Deliberately NOT committed to currentRoom/
    // currentWanderPoint until the bunny actually arrives (see OnArrivedAtWanderPoint) — committing
    // early would make anything that routes off currentRoom mid-walk (e.g. a job assignment) build a
    // path from a location the bunny hasn't physically reached yet.
    private RoomBase pendingWanderRoom;
    private Transform pendingWanderDestination;

    // Cross-floor (lift) trip bookkeeping — the "real" destination beyond the lift ride itself.
    private LiftRoom pendingLift;
    private int pendingLiftOriginFloor;
    private RoomBase pendingFinalRoom;
    private RoomSpot pendingFinalSpot;
    private Transform pendingFinalWanderPoint;
    private BunnyState pendingFinalState;
    private Transform pendingDisembarkLandingSpot; // where to walk back out to after stepping off the car

    private bool facingRight;
    private bool hasInitializedFacing;
    private bool? pendingFacingOverride;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        SetFacing(!bunnyFacesLeftByDefault);
    }
    private void OnEnable()
    {
        DwellerRoster.Instance?.Register(this);
    }

    private void OnDisable()
    {
        DwellerRoster.Instance?.Unregister(this);
    }

    private void Update()
    {
        // Hunger decay ticks regardless of state
        hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * Time.deltaTime);

        switch (CurrentState)
        {
            case BunnyState.Idle:
                HandleIdle();
                if (isWanderingEnabled && IsHungry)
                    LeaveWanderingForCafeteria();
                break;

            case BunnyState.MovingToSpot:
                HandleMovingToSpot();
                break;

            case BunnyState.Working:
                HandleHungerCheckWhileWorking();
                break;

            case BunnyState.Eating:
                // Eating logic is driven by CafeteriaRoom's coroutine/timer, not here
                break;

            case BunnyState.WaitingForLift:
            case BunnyState.RidingLift:
                // Both driven externally by LiftRoom's own state machine, not here
                break;
        }

        UpdateAnimator();
    }

    // ---------- JOB ASSIGNMENT ----------

    public void AssignToJob(IJobRoom jobRoom)
    {
        if (!HasEnteredBase)
        {
            // Still spawned-but-queued at the gate (or mid-approval) — currentRoom isn't set yet, so
            // routing to a job would path straight from the base entrance to the job room, skipping
            // the gate/queue entirely. Guarded here (not just in the assignment UI) so no caller —
            // present or future — can trigger this by routing around the UI.
            Debug.LogWarning($"{name}: can't assign to a job before entering the base (still awaiting gate approval).");
            return;
        }

        assignedJobRoom = jobRoom;
        RequestNewJobSpot();
    }

    // Used by RoomBase.CanBeDeleted (via DwellerRoster) to check whether a room is safe to demolish —
    // covers physically standing/wandering/working/eating there (currentRoom), being assigned there but
    // still mid-transit toward it (assignedJobRoom, e.g. WaitingForLift/RidingLift/walking), and actively
    // walking toward it as a wander destination but not arrived yet (pendingWanderRoom for a same-floor
    // walk, pendingFinalRoom for a wander OR job trip currently in progress via a lift) — currentRoom is
    // deliberately NOT committed until actual arrival (see OnArrivedAtWanderPoint/ResumeTripAfterLift),
    // so without these a bunny visibly walking into/through a room wouldn't count as occupying it yet.
    public void UnassignFromJob()
    {
        if (assignedJobRoom == null) return;

        // Release the reserved spot. In practice claimedWorkSpot is always set the moment a job is
        // actively pursued (RequestNewJobSpot claims it via assignedJobRoom.RequestSpot before any
        // movement starts), so this covers "currently working" AND "still walking toward the spot."
        // The null check is just a defensive guard.
        if (claimedWorkSpot != null)
        {
            assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
            claimedWorkSpot = null;
        }

        assignedJobRoom = null;

        // wanderTimer accumulates only while Idle and is never reset except when a wander pick actually
        // fires — so a bunny that sat idle for a while before ITS FIRST job (e.g. right after entering
        // the base) can carry a near-threshold timer indefinitely, dormant through Working/Eating, ready
        // to fire on the very next Idle tick no matter how much later that is. Without this reset, going
        // Idle here can trigger an immediate random (possibly cross-floor) wander pick that wins a race
        // against the player's very next reassignment click — the bunny visibly detours through a whole
        // extra lift trip before the real reassignment gets its turn (it's only deferred, not lost — see
        // HandleIdle's assignedJobRoom != null retry — but looks like a broken/random path in the
        // meantime). Resetting here guarantees a full wanderPauseDuration grace window after any
        // unassign before wandering can resume.
        wanderTimer = 0f;

        // Currently working: stop immediately and go idle right where we're standing.
        if (CurrentState == BunnyState.Working)
        {
            CurrentState = BunnyState.Idle;
        }
        // Still walking toward the (now-released) work spot: let the bunny finish that walk rather
        // than snapping it to a mid-path position, but arrive Idle instead of starting work.
        else if (CurrentState == BunnyState.MovingToSpot && pendingStateOnArrival == BunnyState.Working)
        {
            pendingStateOnArrival = BunnyState.Idle;
        }
        // Eating, or mid-lift-trip toward the job: deliberately left alone. FinishEatingAndReturnToWork
        // and ResumeTripAfterLift both already check assignedJobRoom == null and fall back to
        // wandering/idle on their own once that leg finishes.
    }
    public bool IsAssociatedWithRoom(RoomBase room)
    {
        if (currentRoom == room) return true;
        if (assignedJobRoom is RoomBase jobRoomBase && jobRoomBase == room) return true;
        if (pendingWanderRoom == room) return true;
        if (pendingFinalRoom == room) return true;
        return false;
    }

    public string DebugState()
    {
        string jobRoomName = assignedJobRoom is RoomBase jobRoomBase ? jobRoomBase.name : "NULL";
        string finalRoomName = pendingFinalRoom != null ? pendingFinalRoom.name : "NULL";
        return $"{name}: currentRoom={(currentRoom != null ? currentRoom.name : "NULL")}, currentState={CurrentState}, pendingStateOnArrival={pendingStateOnArrival}, assignedJobRoom={jobRoomName}, pendingWanderRoom={(pendingWanderRoom != null ? pendingWanderRoom.name : "NULL")}, pendingFinalRoom={finalRoomName}, currentWaypointTarget={(currentWaypointTarget != null ? currentWaypointTarget.name : "NULL")}, pos={transform.position}";
    }

    public void SetAwaitingApproval(bool value)
    {
        IsAwaitingApproval = value;
    }
    public void SetArrivalType(BunnyArrivalType type)
    {
        ArrivalType = type;
    }
    public void SetIdentity(BunnyGender gender, string name)
    {
        Gender = gender;
        BunnyName = name;
        gameObject.name = name; // keeps Hierarchy/debugging readable too
    }

    private void RequestNewJobSpot()
    {
        if (assignedJobRoom == null) return;

        RoomBase jobRoomBase = (RoomBase)assignedJobRoom;

        // Mid-walk on ANY leg of a lower-priority wander trip: currentRoom/currentWanderPoint only
        // update on FULL arrival at a destination (see OnArrivedAtWanderPoint/ResumeTripAfterLift),
        // never incrementally as the bunny passes through intermediate rooms or walks toward a lift.
        // Building a route now (below) would resume from that stale "start of this leg" point instead
        // of wherever the bunny actually is.
        //
        // IsWanderPacedLeg() (already existed for movement-speed purposes) correctly identifies every
        // leg of a lower-priority wander trip — same-floor AND every stage of a cross-floor one.
        //
        // If there's an actual lift call already in flight (pendingLift != null), try to REDIRECT it to
        // the job's floor first — this is strictly better than deferring: the bunny is already
        // committed to riding this lift, so retargeting where it drops her off costs nothing extra,
        // whereas deferring would make her ride all the way to the now-irrelevant wander destination
        // and back. My first attempt at this fix deferred unconditionally here, which made the
        // already-built TryRedirectInFlightWanderTrip mechanism unreachable for exactly the cases it
        // was designed for (a job landing while mid-transit toward a lift) — a bunny would ride to a
        // stale wander destination floor, then ride BACK to the real job floor, instead of just
        // redirecting the one ride in progress.
        //
        // Only fall back to deferring (let the current leg finish naturally, HandleIdle's existing
        // retry picks the job back up once idle) when there's no lift to redirect at all (a pure
        // same-floor wander) or this specific lift doesn't reach the job's floor.
        if (CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg())
        {
            if (pendingLift != null)
            {
                RoomSpot redirectSpot = assignedJobRoom.RequestSpot(this);
                if (redirectSpot != null)
                {
                    if (TryRedirectInFlightWanderTrip(jobRoomBase, redirectSpot, null, BunnyState.Working))
                    {
                        claimedWorkSpot = redirectSpot;
                        // TEMP DEBUG (round 4) — remove once the Menace round-trip issue is root-caused.
                        Debug.Log($"[JobRouteDebug4] {name}: RequestNewJobSpot redirected in-flight lift trip to floor {jobRoomBase.FloorIndex}.");
                        return;
                    }
                    // This lift doesn't reach the job's floor — don't hold the spot hostage while
                    // waiting for the current leg to finish; release and let the deferred retry
                    // reclaim it once idle.
                    assignedJobRoom.ReleaseSpot(redirectSpot, this);
                }
            }

            // TEMP DEBUG (round 4) — remove once the Menace round-trip issue is root-caused.
            Debug.Log($"[JobRouteDebug4] {name}: RequestNewJobSpot deferred (mid lower-priority wander leg, no redirect possible). {DebugState()}");
            return;
        }

        RoomSpot spot = assignedJobRoom.RequestSpot(this);
        if (spot == null) return;

        claimedWorkSpot = spot;

        // TEMP DEBUG (round 4) — remove once the Menace round-trip issue is root-caused.
        Debug.Log($"[JobRouteDebug4] {name}: RequestNewJobSpot proceeding. {DebugState()}, jobRoomFloor={jobRoomBase.FloorIndex}");

        if (currentRoom != null && currentFloorIndex != jobRoomBase.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(jobRoomBase, spot, BunnyState.Working))
            {
                // No lift connects these floors — don't leave the spot reserved for an unreachable bunny.
                assignedJobRoom.ReleaseSpot(spot, this);
                claimedWorkSpot = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, jobRoomBase, spot, currentFloorIndex);

        // TEMP DEBUG (round 4) — remove once the Pip long-detour issue is root-caused.
        Debug.Log($"[JobRouteDebug4] {name}: same-floor route to {jobRoomBase.name} built with {path.Count} points:");
        foreach (Transform t in path)
            Debug.Log($"[JobRouteDebug4]   - {(t != null ? t.name : "NULL")} at {(t != null ? t.position.ToString() : "N/A")}");

        MoveAlongPath(path, spot, BunnyState.Working);
    }

    private void HandleIdle()
    {
        if (assignedJobRoom != null && claimedWorkSpot == null)
        {
            RequestNewJobSpot();
        }
        else if (isWanderingEnabled)
        {
            wanderTimer += Time.deltaTime;
            if (wanderTimer >= wanderPauseDuration)
            {
                wanderTimer = 0f;
                PickNewWanderDestination();
            }
        }
    }

    private void PickNewWanderDestination()
    {
        int myFloor = currentFloorIndex;
        int targetFloor = myFloor;

        // Only roll for a different floor once we actually have a known current room — a bunny fresh
        // from the base entrance takes its first same-floor wander step before ever using a lift.
        if (currentRoom != null && Random.value < crossFloorWanderChance)
        {
            List<int> allFloors = BaseLayoutManager.Instance.GetAllFloorIndices();
            if (allFloors.Count > 1)
                targetFloor = allFloors[Random.Range(0, allFloors.Count)];
        }

        List<RoomBase> candidateRooms = new List<RoomBase>();
        foreach (RoomBase room in BaseLayoutManager.Instance.GetAllRoomsOnFloor(targetFloor))
        {
            if (!(room is LiftRoom)) // a lift's shared entrance points aren't meaningful wander destinations
                candidateRooms.Add(room);
        }
        if (candidateRooms.Count == 0) return;

        RoomBase target = candidateRooms[Random.Range(0, candidateRooms.Count)];
        List<Transform> wanderPoints = target.GetWanderPoints();
        if (wanderPoints.Count == 0) return;

        Transform destination = wanderPoints[Random.Range(0, wanderPoints.Count)];

        if (targetFloor != myFloor)
        {
            TryBeginCrossFloorTripToWanderPoint(target, destination);
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, target, destination, currentFloorIndex);

        if (path.Count == 0) return;

        currentTargetSpot = null; // wandering has no RoomSpot destination
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Wandering;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();

        // Remembered for OnArrivedAtWanderPoint — NOT committed to currentRoom/currentWanderPoint yet,
        // since the bunny hasn't actually walked there.
        pendingWanderRoom = target;
        pendingWanderDestination = destination;
    }

    private void OnArrivedAtWanderPoint()
    {
        currentRoom = pendingWanderRoom;
        currentSpot = null;
        currentWanderPoint = pendingWanderDestination;
        currentFloorIndex = pendingWanderRoom.FloorIndex;
        pendingWanderRoom = null;
        pendingWanderDestination = null;

        CurrentState = BunnyState.Idle; // pause, then HandleIdle picks the next destination after wanderPauseDuration
    }

    // ---------- MOVEMENT ----------

    private void MoveAlongPath(List<Transform> waypoints, RoomSpot spot, BunnyState stateOnArrival)
    {
        pendingFacingOverride = null;
        currentTargetSpot = spot;
        currentPath = new Queue<Transform>(waypoints);
        pendingStateOnArrival = stateOnArrival;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    private void AdvanceToNextWaypoint()
    {
        currentWaypointTarget = currentPath.Count > 0 ? currentPath.Dequeue() : null;
    }

    private BunnyState pendingStateOnArrival;

    private void HandleMovingToSpot()
    {
        if (currentWaypointTarget == null)
        {
            CurrentState = pendingStateOnArrival;

            if (pendingFacingOverride.HasValue)
            {
                SetFacing(pendingFacingOverride.Value);
                pendingFacingOverride = null;
            }
            else if (currentTargetSpot != null)
            {
                SetFacing(currentTargetSpot.FacesRight);
            }

            if (pendingStateOnArrival == BunnyState.Working)
                OnArrivedAtWorkSpot();
            else if (pendingStateOnArrival == BunnyState.Eating)
                OnArrivedAtEatingSpot();
            else if (pendingStateOnArrival == BunnyState.Wandering)
                OnArrivedAtWanderPoint();
            else if (pendingStateOnArrival == BunnyState.PassingGate)
                OnArrivedAtGateExit();
            else if (pendingStateOnArrival == BunnyState.Despawning)
                OnArrivedAtDespawnPoint();
            else if (pendingStateOnArrival == BunnyState.WaitingForLift)
                OnArrivedAtLiftLanding();
            else if (pendingStateOnArrival == BunnyState.RidingLift)
                OnArrivedAtBoardingSpot();
            else if (pendingStateOnArrival == BunnyState.DisembarkingLift)
                OnArrivedAtDisembarkLanding();

            return;
        }

        Vector3 target = currentWaypointTarget.position;
        Vector3 current = transform.position;
        Vector3 toTarget = target - current;

        if (toTarget.magnitude <= arrivalThreshold)
        {
            transform.position = target;
            AdvanceToNextWaypoint(); // move on to the next waypoint in the queue
            return;
        }

        Vector3 direction = toTarget.normalized;
        float currentMoveSpeed = IsWanderPacedLeg() ? moveSpeed * wanderSpeedMultiplier : moveSpeed;
        transform.position += direction * currentMoveSpeed * Time.deltaTime;

        if (Mathf.Abs(direction.x) > 0.01f)
            SetFacing(direction.x < 0f);
    }

    // True while the bunny should move/animate at wander pace rather than full speed. A direct wander
    // leg always qualifies; the WaitingForLift/RidingLift/DisembarkingLift legs of a lift trip only
    // qualify if the trip's ultimate purpose (pendingFinalState) is itself a wander — otherwise a lift
    // ride to a JOB or the cafeteria would incorrectly slow down too. Without this, a wandering bunny
    // that decides to take the lift walks to the landing spot at full (job) speed, then drops back to
    // wander speed once it resumes wandering on the new floor — a visible, unintended speed-up.
    private bool IsWanderPacedLeg()
    {
        if (pendingStateOnArrival == BunnyState.Wandering) return true;

        bool isLiftTransitLeg = pendingStateOnArrival == BunnyState.WaitingForLift
            || pendingStateOnArrival == BunnyState.RidingLift
            || pendingStateOnArrival == BunnyState.DisembarkingLift;
        return isLiftTransitLeg && pendingFinalState == BunnyState.Wandering;
    }

    public void EnterBaseAndWander(RoomBase startingRoom = null, Transform startingPoint = null)
    {
        HasEnteredBase = true;
        isWanderingEnabled = true;
        currentRoom = startingRoom;
        currentSpot = null;
        currentWanderPoint = startingPoint;
        currentFloorIndex = startingRoom != null ? startingRoom.FloorIndex : 0;
        CurrentState = BunnyState.Idle; // triggers HandleIdle -> picks first wander destination
    }
    public void MoveToQueueSpot(Transform queueSpot)
    {
        List<Transform> path = new List<Transform> { queueSpot };
        currentTargetSpot = null; // queue spots aren't RoomSpots, just plain waypoints
        pendingFacingOverride = true; // queued bunnies always face right, toward the base interior
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Idle; // just stand there once arrived, waiting in queue
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }
    public void ProceedThroughGate(Transform gateExitPoint, RoomBase arrivalRoom)
    {
        List<Transform> path = new List<Transform> { gateExitPoint };
        currentTargetSpot = null;
        pendingFacingOverride = null; // let facing follow travel direction through the gate
        pendingArrivalRoom = arrivalRoom;
        pendingArrivalPoint = gateExitPoint;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.PassingGate;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }
    public void RejectAndDespawn(Transform exitPoint)
    {
        List<Transform> path = new List<Transform> { exitPoint };
        currentTargetSpot = null;
        pendingFacingOverride = null; // let facing follow travel direction back out
        pendingArrivalRoom = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Despawning;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    // ---------- WORKING (GARDEN) ----------

    private void OnArrivedAtWorkSpot()
    {
        currentRoom = (RoomBase)assignedJobRoom;
        currentSpot = claimedWorkSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        assignedJobRoom.NotifyBunnyReadyToWork(this);
    }

    private void OnArrivedAtEatingSpot()
    {
        currentRoom = cafeteriaBeingUsed;
        currentSpot = currentTargetSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        cafeteriaBeingUsed.NotifyBunnyReadyToEat(this);
    }

    private void OnArrivedAtGateExit()
    {
        GateQueueManager.Instance.NotifyBunnyPassedGate(this);
        EnterBaseAndWander(pendingArrivalRoom, pendingArrivalPoint);
    }

    private void OnArrivedAtDespawnPoint()
    {
        Destroy(gameObject);
    }

    // ---------- CROSS-FLOOR (LIFT) ----------

    // Routes to the nearest floor-appropriate lift, then hands the trip off to it. Returns false
    // if no lift services both floors (or a trip is already in flight), so the caller can back out
    // cleanly (release a claimed spot, etc).
    private bool TryBeginCrossFloorTripToSpot(RoomBase targetRoom, RoomSpot targetSpot, BunnyState finalState)
    {
        if (pendingLift != null)
        {
            // Already mid-trip (waiting for / riding / walking off a lift) from an earlier assignment.
            // If that trip is just an ambient wander (lower priority than a real job or heading to eat),
            // redirect it to this destination instead of making the bunny finish the now-irrelevant leg
            // first — see TryRedirectInFlightWanderTrip. Otherwise (already mid-trip toward a real job
            // or the cafeteria), leave it alone: overwriting pendingFinalRoom/Spot/Lift out from under
            // an equally-important trip would make IT resolve against the wrong destination once it
            // finishes — the bunny would end up "arriving" somewhere that doesn't match where it is.
            if (TryRedirectInFlightWanderTrip(targetRoom, targetSpot, null, finalState))
                return true;

            Debug.LogWarning($"{name}: already mid-lift-trip, ignoring new cross-floor request to floor {targetRoom.FloorIndex}.");
            return false;
        }

        int myFloor = currentFloorIndex;
        LiftRoom lift = BaseLayoutManager.Instance.FindLiftServicing(myFloor, targetRoom.FloorIndex);
        if (lift == null)
        {
            Debug.LogWarning($"{name}: no lift services floor {myFloor} -> {targetRoom.FloorIndex}.");
            return false;
        }

        pendingFinalRoom = targetRoom;
        pendingFinalSpot = targetSpot;
        pendingFinalWanderPoint = null;
        pendingFinalState = finalState;
        BeginTripToLift(lift, myFloor, targetRoom.FloorIndex);
        return true;
    }

    // Claims an in-flight trip for a higher-priority destination instead of making the bunny finish an
    // unrelated, lower-priority leg first — e.g. a random wander pick that happened to fire right
    // before the player assigned a job. Only ever preempts a trip whose purpose is ALREADY Wandering;
    // a trip already heading to a real job or the cafeteria is left alone (the caller falls back to the
    // normal defer-and-retry-once-idle path instead), since bumping those would risk losing track of a
    // RoomSpot already claimed on the original destination.
    private bool TryRedirectInFlightWanderTrip(RoomBase targetRoom, RoomSpot targetSpot, Transform targetWanderPoint, BunnyState finalState)
    {
        if (pendingLift == null || pendingFinalState != BunnyState.Wandering) return false;
        if (!pendingLift.TryRedirectCall(this, targetRoom.FloorIndex)) return false;

        pendingFinalRoom = targetRoom;
        pendingFinalSpot = targetSpot;
        pendingFinalWanderPoint = targetWanderPoint;
        pendingFinalState = finalState;

        Debug.Log($"{name}: redirected in-flight wander trip to floor {targetRoom.FloorIndex} for a higher-priority {finalState} trip.");
        return true;
    }

    private bool TryBeginCrossFloorTripToWanderPoint(RoomBase targetRoom, Transform destination)
    {
        if (pendingLift != null)
        {
            // See TryBeginCrossFloorTripToSpot — refuse to clobber an in-flight trip's pending state.
            Debug.LogWarning($"{name}: already mid-lift-trip, ignoring new cross-floor wander request to floor {targetRoom.FloorIndex}.");
            return false;
        }

        int myFloor = currentFloorIndex;
        LiftRoom lift = BaseLayoutManager.Instance.FindLiftServicing(myFloor, targetRoom.FloorIndex);
        if (lift == null)
        {
            Debug.LogWarning($"{name}: no lift services floor {myFloor} -> {targetRoom.FloorIndex}.");
            return false;
        }

        pendingFinalRoom = targetRoom;
        pendingFinalSpot = null;
        pendingFinalWanderPoint = destination;
        pendingFinalState = BunnyState.Wandering;
        BeginTripToLift(lift, myFloor, targetRoom.FloorIndex);
        return true;
    }

    private void BeginTripToLift(LiftRoom lift, int originFloor, int destinationFloor)
    {
        pendingLift = lift;
        pendingLiftOriginFloor = originFloor;

        // Route to the SPECIFIC segment registered on our current floor, not the coordinator directly
        // — a lift's coordinator is just whichever segment happens to own the shared state machine,
        // and it's only actually registered (for pass-through/routing purposes) on its own floor.
        LiftRoom originSegment = lift.GetSegmentForFloor(originFloor);
        Transform landingSpot = lift.GetLandingSpot(originFloor);
        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, originSegment, landingSpot, currentFloorIndex);

        currentTargetSpot = null;
        pendingFacingOverride = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.WaitingForLift;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();

        lift.RequestLift(this, originFloor, destinationFloor);
    }

    private void OnArrivedAtLiftLanding()
    {
        currentRoom = pendingLift.GetSegmentForFloor(pendingLiftOriginFloor);
        currentSpot = null;
        currentWanderPoint = pendingLift.GetLandingSpot(pendingLiftOriginFloor);
        currentFloorIndex = pendingLiftOriginFloor;
        CurrentState = BunnyState.WaitingForLift;
        pendingLift.NotifyArrivedAtLanding(this, pendingLiftOriginFloor);
    }

    // Called by LiftRoom once it's ready to carry this bunny — walks the short distance from the
    // landing/waiting spot to the boarding point (near the shaft), then actually starts riding.
    public void BoardLift(Transform boardingSpot)
    {
        // TEMP DEBUG (round 5) — remove once the Buckshot invisibility issue is root-caused.
        Debug.Log($"[VisibilityDebug] {name}: BoardLift called, boardingSpot={(boardingSpot != null ? boardingSpot.name : "NULL")}, time={Time.time:F2}");

        if (boardingSpot == null)
        {
            CurrentState = BunnyState.RidingLift;
            StartCoroutine(HideAfterDelay(liftBoardHideDelay));
            return;
        }

        List<Transform> path = new List<Transform> { boardingSpot };
        currentTargetSpot = null;
        pendingFacingOverride = null;
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.RidingLift;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();
    }

    private void OnArrivedAtBoardingSpot()
    {
        // Doors are already open (that's why boarding started), but stay visible a moment longer so a
        // doors-closing animation has time to play before the bunny vanishes into the shaft — otherwise
        // it pops out of existence before the doors even start closing.

        // TEMP DEBUG (round 5) — remove once the Buckshot invisibility issue is root-caused.
        Debug.Log($"[VisibilityDebug] {name}: OnArrivedAtBoardingSpot, starting HideAfterDelay({liftBoardHideDelay}), time={Time.time:F2}");

        StartCoroutine(HideAfterDelay(liftBoardHideDelay));
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        // TEMP DEBUG (round 5) — remove once the Buckshot invisibility issue is root-caused.
        Debug.Log($"[VisibilityDebug] {name}: HideAfterDelay coroutine started, will hide at time={Time.time + delay:F2}");

        yield return new WaitForSeconds(delay);
        SetVisible(false);
    }

    // Called by LiftRoom once it's arrived at this bunny's destination floor. Reappears at the
    // boarding point right away (doors just opened here, revealing the bunny standing inside), waits
    // a beat, then walks the short distance back out to the landing/waiting spot before resuming
    // whatever trip was in progress.
    public void DisembarkFromLift(Transform landingSpot, Transform boardingSpot, int floorIndex)
    {
        // TEMP DEBUG (round 5) — remove once the Buckshot invisibility issue is root-caused.
        Debug.Log($"[VisibilityDebug] {name}: DisembarkFromLift called, landingSpot={(landingSpot != null ? landingSpot.name : "NULL")}, boardingSpot={(boardingSpot != null ? boardingSpot.name : "NULL")}, floorIndex={floorIndex}, time={Time.time:F2}");

        currentFloorIndex = floorIndex;
        SetVisible(true);

        Transform arrivalPoint = boardingSpot != null ? boardingSpot : landingSpot;
        if (arrivalPoint != null)
            transform.position = arrivalPoint.position;

        // Clear any leftover "walking to the boarding spot" state from BoardLift before it gets a
        // chance to process again. LiftRoom.BoardArrivedRiders marks a bunny "boarded" (moves it into
        // boardedRiders) the instant it calls BoardLift — without waiting for the bunny to actually
        // finish that short walk. On a near-zero-travel-time ride (e.g. a same-floor redirect, or a
        // shared boarding-grace-period window that's already mostly elapsed for a later-joining
        // bunny), the whole round trip can complete faster than that walk animation does. Without this
        // reset, the teleport above makes the bunny's still-active RidingLift-bound path think it just
        // arrived at the boarding spot on its own, firing OnArrivedAtBoardingSpot() a second, spurious
        // time — starting a stale HideAfterDelay that lands ~0.5s later, right as (or after) THIS
        // disembark's own reveal-and-walk-out sequence has already moved the bunny onto a different
        // leg, hiding it with nothing left to ever reveal it again. CurrentState = Idle (rather than
        // leaving it MovingToSpot with an empty queue) matches what's already true here per the comment
        // above WalkOutAfterDelay — the bunny is standing still during this wait — and stops
        // HandleMovingToSpot from dispatching anything until WalkOutAfterDelay resumes movement itself.
        currentPath = new Queue<Transform>();
        currentWaypointTarget = null;
        CurrentState = BunnyState.Idle;

        StartCoroutine(WalkOutAfterDelay(landingSpot, boardingSpot, liftDisembarkRevealDelay));
    }

    // Waits before actually setting off so a doors-opening animation has time to finish first — the
    // bunny is already visible and standing still during this wait (see DisembarkFromLift), matching
    // the same visible pause boarding gets before it vanishes.
    private IEnumerator WalkOutAfterDelay(Transform landingSpot, Transform boardingSpot, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (boardingSpot != null && landingSpot != null && boardingSpot != landingSpot)
        {
            pendingDisembarkLandingSpot = landingSpot;
            List<Transform> path = new List<Transform> { landingSpot };
            currentTargetSpot = null;
            pendingFacingOverride = null;
            currentPath = new Queue<Transform>(path);
            pendingStateOnArrival = BunnyState.DisembarkingLift;
            CurrentState = BunnyState.MovingToSpot;
            AdvanceToNextWaypoint();
            yield break;
        }

        ResumeTripAfterLift(landingSpot);
    }

    private void OnArrivedAtDisembarkLanding()
    {
        Transform landingSpot = pendingDisembarkLandingSpot;
        pendingDisembarkLandingSpot = null;
        ResumeTripAfterLift(landingSpot);
    }

    private void ResumeTripAfterLift(Transform landingSpot)
    {
        // Resolve to the SPECIFIC segment registered on the floor we actually landed on — currentFloorIndex
        // was already updated to the destination floor by DisembarkFromLift before this runs. The
        // coordinator itself is only registered (for routing purposes) on its own floor, which may
        // not be this one.
        LiftRoom arrivalSegment = pendingLift.GetSegmentForFloor(currentFloorIndex);
        RoomBase finalRoom = pendingFinalRoom;
        RoomSpot finalSpot = pendingFinalSpot;
        Transform finalWanderPoint = pendingFinalWanderPoint;
        BunnyState finalState = pendingFinalState;

        pendingLift = null;
        pendingFinalRoom = null;
        pendingFinalSpot = null;
        pendingFinalWanderPoint = null;

        currentSpot = null;
        currentWanderPoint = landingSpot;

        // We're now physically on the destination floor at the lift's landing spot — resume the
        // original trip as an ordinary same-floor walk from here.
        if (finalSpot != null)
        {
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(arrivalSegment, null, landingSpot, finalRoom, finalSpot, currentFloorIndex);
            MoveAlongPath(path, finalSpot, finalState);
        }
        else if (finalWanderPoint != null)
        {
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(arrivalSegment, null, landingSpot, finalRoom, finalWanderPoint, currentFloorIndex);
            currentTargetSpot = null;
            pendingFacingOverride = null;
            currentPath = new Queue<Transform>(path);
            pendingStateOnArrival = finalState;
            CurrentState = BunnyState.MovingToSpot;
            AdvanceToNextWaypoint();

            currentRoom = finalRoom;
            currentWanderPoint = finalWanderPoint;
            currentFloorIndex = finalRoom.FloorIndex;

            // pendingStateOnArrival == Wandering means OnArrivedAtWanderPoint() fires once this leg's
            // path completes, and THAT method reads pendingWanderRoom/pendingWanderDestination (only
            // ever set by the same-floor wander path, PickNewWanderDestination) rather than finalRoom/
            // finalWanderPoint. Without setting them here too, it dereferences a null pendingWanderRoom
            // and throws — which aborts Update() mid-call and leaves the bunny stuck in
            // BunnyState.Wandering (a state Update()'s switch has no case for) permanently. Harmless to
            // set redundantly alongside the eager currentRoom/currentWanderPoint/currentFloorIndex
            // assignment above — OnArrivedAtWanderPoint() just re-applies the same values.
            pendingWanderRoom = finalRoom;
            pendingWanderDestination = finalWanderPoint;
        }
        else
        {
            currentRoom = arrivalSegment;
            CurrentState = BunnyState.Idle;
        }
    }

    private void HandleHungerCheckWhileWorking()
    {
        if (IsHungry)
        {
            LeaveWorkForCafeteria();
        }
    }

    private void LeaveWorkForCafeteria()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteria(transform.position);
        Debug.Log($"Cafeteria found: {cafeteria}");
        if (cafeteria == null)
            return; // no cafeteria available — stay working, hunger check retries next frame

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        Debug.Log($"Eat spot found: {eatSpot}");
        if (eatSpot == null)
            return; // cafeteria full — stay working, hunger check retries next frame

        // Pause work but keep claimedWorkSpot reserved — otherwise the room could hand
        // this bunny's spot to someone else while it's off eating.
        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        cafeteriaBeingUsed = cafeteria;

        if (departingRoom.FloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                // No lift connects these floors — abandon the eating spot, retry on a later hunger check.
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);

        Debug.Log($"Path built with {path.Count} points:");
        foreach (Transform t in path)
            Debug.Log($" - {(t != null ? t.name : "NULL")} at {(t != null ? t.position.ToString() : "N/A")}");

        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }
    private void LeaveWanderingForCafeteria()
    {
        RoomBase departingRoom = currentRoom;
        RoomSpot departingSpot = currentSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteria(transform.position);
        if (cafeteria == null) return;

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null) return;

        cafeteriaBeingUsed = cafeteria;

        if (departingRoom != null && currentFloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);
        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }

    // Called by CafeteriaRoom each time a carrot-consumption tick happens
    public void ReceiveCarrotNutrition()
    {
        hunger = Mathf.Min(100f, hunger + hungerGainPerCarrot);
    }

    public bool IsFullyFed()
    {
        return hunger >= hungerThresholdFull;
    }

    // Called by CafeteriaRoom once bunny is full (or can't get more carrots)
    public void FinishEatingAndReturnToWork()
    {
        if (cafeteriaBeingUsed != null)
        {
            cafeteriaBeingUsed.ReleaseSpot(currentTargetSpot, this);
            cafeteriaBeingUsed = null;
        }

        if (assignedJobRoom != null)
        {
            if (claimedWorkSpot != null)
            {
                RoomBase jobRoomBase = (RoomBase)assignedJobRoom;

                if (currentRoom != null && currentFloorIndex != jobRoomBase.FloorIndex)
                {
                    if (!TryBeginCrossFloorTripToSpot(jobRoomBase, claimedWorkSpot, BunnyState.Working))
                    {
                        // No lift back to the job floor — release the spot and let HandleIdle retry later.
                        assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
                        claimedWorkSpot = null;
                        CurrentState = BunnyState.Idle;
                    }
                    return;
                }

                // Spot was never released while eating — walk straight back to it.
                List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, jobRoomBase, claimedWorkSpot, currentFloorIndex);
                MoveAlongPath(path, claimedWorkSpot, BunnyState.Working);
            }
            else
            {
                RequestNewJobSpot();
            }
        }
        else if (isWanderingEnabled)
        {
            CurrentState = BunnyState.Idle; // resumes wandering via HandleIdle
        }
        else
        {
            CurrentState = BunnyState.Idle;
        }
    }
    // ---------- ANIMATION ----------

    private void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetBool(isMovingParam, CurrentState == BunnyState.MovingToSpot);
        animator.SetBool(isWorkingParam, CurrentState == BunnyState.Working);
        animator.SetBool(isEatingParam, CurrentState == BunnyState.Eating);

        bool isSlowWander = CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg();
        animator.speed = isSlowWander ? wanderSpeedMultiplier : 1f;
    }

    // ---------- VISIBILITY (used while riding a lift, doors-hidden) ----------

    private void SetVisible(bool visible)
    {
        // TEMP DEBUG (round 5) — remove once the Buckshot invisibility issue is root-caused.
        Debug.Log($"[VisibilityDebug] {name}: SetVisible({visible}) called. CurrentState={CurrentState}, pendingStateOnArrival={pendingStateOnArrival}, time={Time.time:F2}");

        if (bunnyScaleRoot != null)
            bunnyScaleRoot.gameObject.SetActive(visible);
    }

    // ---------- FACING (reused from BunnyMovement) ----------

    private Vector3 GetVisualCenterWorld()
    {
        SpriteRenderer[] renderers = bunnyScaleRoot.GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0) return bunnyScaleRoot.position;

        Bounds bounds = renderers[0].bounds;
        foreach (SpriteRenderer r in renderers)
            bounds.Encapsulate(r.bounds);

        return bounds.center;
    }

    private void SetFacing(bool shouldFaceRight)
    {
        if (bunnyScaleRoot == null) return;

        if (!hasInitializedFacing)
        {
            hasInitializedFacing = true;
        }
        else if (shouldFaceRight == facingRight)
        {
            return;
        }

        facingRight = shouldFaceRight;

        Vector3 worldCenterBefore = GetVisualCenterWorld();

        bool flip = bunnyFacesLeftByDefault ? shouldFaceRight : !shouldFaceRight;
        Vector3 scale = bunnyScaleRoot.localScale;
        scale.x = flip ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        bunnyScaleRoot.localScale = scale;

        Vector3 worldCenterAfter = GetVisualCenterWorld();
        Vector3 correction = worldCenterBefore - worldCenterAfter;
        correction.y = 0f;
        correction.z = 0f;

        bunnyScaleRoot.position += correction;
    }
}