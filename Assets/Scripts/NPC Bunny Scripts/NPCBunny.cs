using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BunnyState
{
    Idle,
    MovingToSpot,
    Working,
    Eating,
    Drinking,
    Relaxing,
    Sleeping,
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
    [SerializeField] private float hungerThresholdCritical = 15f; // only this threshold interrupts Sleeping
    [SerializeField] private float hungerGainPerCarrot = 25f;

    [Header("Thirst")]
    [SerializeField] private float thirst = 100f; // 0-100, mechanically clones Hunger
    [SerializeField] private float thirstDecayPerSecond = 0.1f;
    [SerializeField] private float thirstThresholdLow = 35f;
    [SerializeField] private float thirstThresholdFull = 75f;
    [SerializeField] private float thirstThresholdCritical = 15f; // only this threshold interrupts Sleeping
    [SerializeField] private float thirstGainPerDrink = 25f;

    [Header("Energy")]
    [SerializeField] private float energy = 100f; // 0-100
    [SerializeField] private float energyDecayPerSecond = 0.1f; // passive decay, ticks in every state except Sleeping
    [SerializeField] private float energyDecayPerSecondWorking = 0.2f; // faster than passive while actually working
    [SerializeField] private float energyDecayPerSecondQuesting = 0.2f; // Quest state doesn't exist yet — tunable ahead of time
    [SerializeField] private float energyDecayPerSecondForaging = 0.2f; // Foraging state doesn't exist yet — tunable ahead of time
    [SerializeField] private float energyThresholdLow = 35f;
    [SerializeField] private float energyGainPerSecondSleeping = 5f; // regen only goes up to 100, never past

    [Header("Mood")]
    [SerializeField] private float mood = 100f; // 0-100, pure passive stat, no room/travel of its own
    [SerializeField] private float moodDecayPerSecondWorking = 0.05f;
    [SerializeField] private float moodGainPerSecondRelaxing = 0.05f;
    [SerializeField] private float moodGainPerSecondEatingOrDrinking = 0.2f; // fast but short (Eating/Drinking don't last long)
    [SerializeField] private float moodGainPerSecondSleeping = 0.02f; // slow but long
    [SerializeField] private float moodDecayPerSecondIdlePacing = 0.1f; // idle-pacing with no relax spot available

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isMovingParam = "IsMoving";
    [SerializeField] private string isWorkingParam = "IsWorking";
    [SerializeField] private string isEatingParam = "IsEating";
    [SerializeField] private string isDrinkingParam = "IsDrinking";
    [SerializeField] private string isSleepingParam = "IsSleeping";

    [Header("Facing Direction")]
    [SerializeField] private Transform bunnyScaleRoot;
    [SerializeField] private bool bunnyFacesLeftByDefault = true;

    [Header("Idle Pacing (fallback when no Living Room spot is available anywhere on the base)")]
    [SerializeField] private float wanderPauseDuration = 3f; // how long to idle at each stop before moving again
    private float wanderTimer = 0f;

    [Header("Warnings")]
    [SerializeField] private float noSpotWarningCooldown = 5f; // throttles repeated "no spot available" toasts
    private float lastNoEatSpotWarningTime = -999f;
    private float lastNoDrinkSpotWarningTime = -999f;
    private float lastNoSleepSpotWarningTime = -999f;

    [Header("Lift Visual Timing")]
    [Tooltip("Brief pause after teleporting onto the new floor before walking out to the landing spot, leaving room for a future doors-opening animation.")]
    [SerializeField] private float liftDisembarkRevealDelay = 0.5f;

    public BunnyState CurrentState { get; private set; } = BunnyState.Idle;
    public bool IsHungry => hunger < hungerThresholdLow;
    public bool IsThirsty => thirst < thirstThresholdLow;
    public bool IsTired => energy < energyThresholdLow;
    public bool IsCriticallyHungry => hunger < hungerThresholdCritical;
    public bool IsCriticallyThirsty => thirst < thirstThresholdCritical;
    public float HungerValue => hunger;
    public float ThirstValue => thirst;
    public float EnergyValue => energy;
    public float MoodValue => mood;
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
    private WaterRoom waterRoomBeingUsed; // mirrors cafeteriaBeingUsed, for the drinking-spot side of a WaterRoom
    private RoomSpot claimedRelaxSpot; // Living Room spot reserved/occupied while idle; mirrors claimedWorkSpot
    private LivingRoom claimedRelaxRoom; // which room claimedRelaxSpot belongs to
    private RoomSpot claimedSleepSpot; // Bedroom spot reserved/occupied while sleeping; mirrors claimedRelaxSpot
    private BedroomRoom claimedSleepRoom; // which room claimedSleepSpot belongs to
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
        // Hunger/Thirst decay tick regardless of state
        hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * Time.deltaTime);
        thirst = Mathf.Max(0f, thirst - thirstDecayPerSecond * Time.deltaTime);

        // Energy always decays except while Sleeping, which is the only way to regen it (capped at 100).
        // The rate depends on activity — see GetEnergyDecayRate().
        if (CurrentState == BunnyState.Sleeping)
            energy = Mathf.Min(100f, energy + energyGainPerSecondSleeping * Time.deltaTime);
        else
            energy = Mathf.Max(0f, energy - GetEnergyDecayRate() * Time.deltaTime);

        switch (CurrentState)
        {
            case BunnyState.Idle:
                HandleIdle();
                break;

            case BunnyState.MovingToSpot:
                HandleMovingToSpot();
                break;

            case BunnyState.Working:
                HandleNeedsCheckWhileWorking();
                break;

            case BunnyState.Relaxing:
                if (assignedJobRoom != null && claimedWorkSpot == null)
                    RequestNewJobSpot();
                else
                    HandleNeedsCheckWhileRelaxing();
                break;

            // A job assignment never interrupts Sleeping (unlike Relaxing, above) — no job check here
            // at all. Only a critical hunger/thirst threshold or energy reaching 100 ends a sleep.
            case BunnyState.Sleeping:
                if (energy >= 100f)
                    FinishSleepingAndReturnToPrevious();
                else
                    HandleCriticalNeedsCheckWhileSleeping();
                break;

            case BunnyState.Eating:
            case BunnyState.Drinking:
                // Eating/Drinking logic is driven by CafeteriaRoom's/WaterRoom's own coroutine/timer, not here
                break;

            case BunnyState.WaitingForLift:
            case BunnyState.RidingLift:
                // Both driven externally by LiftRoom's own state machine, not here
                break;
        }

        // Ticked after the switch so a state transition that happens this same frame (e.g. Idle ->
        // MovingToSpot the instant a relax spot is claimed) is reflected immediately rather than a
        // frame late.
        TickMood();

        UpdateAnimator();
    }

    // Passive decay applies in every state except Sleeping (handled separately in Update()). Working
    // drains faster than passive; Questing/Foraging will too once those states exist — their rate
    // fields already exist for tuning ahead of time, this switch is the one-line extension point for
    // wiring them in once the states themselves are added.
    private float GetEnergyDecayRate()
    {
        switch (CurrentState)
        {
            case BunnyState.Working:
                return energyDecayPerSecondWorking;
            // case BunnyState.Questing: return energyDecayPerSecondQuesting;
            // case BunnyState.Foraging: return energyDecayPerSecondForaging;
            default:
                return energyDecayPerSecond;
        }
    }

    // Mood is a pure passive stat: no room, no travel, no interrupt of its own. Ticked once per frame
    // based on whatever CurrentState already resolved to this frame.
    private void TickMood()
    {
        switch (CurrentState)
        {
            case BunnyState.Working:
                mood = Mathf.Max(0f, mood - moodDecayPerSecondWorking * Time.deltaTime);
                break;

            case BunnyState.Relaxing:
                mood = Mathf.Min(100f, mood + moodGainPerSecondRelaxing * Time.deltaTime);
                break;

            case BunnyState.Eating:
            case BunnyState.Drinking:
                mood = Mathf.Min(100f, mood + moodGainPerSecondEatingOrDrinking * Time.deltaTime);
                break;

            case BunnyState.Sleeping:
                mood = Mathf.Min(100f, mood + moodGainPerSecondSleeping * Time.deltaTime);
                break;

            default:
                if (IsIdlePacingWithoutRelaxSpot())
                    mood = Mathf.Max(0f, mood - moodDecayPerSecondIdlePacing * Time.deltaTime);
                break;
        }
    }

    // True whenever the bunny has no job and no claimed relax spot, and is either parked Idle waiting
    // for one to free up, or mid-pace between wander points while it waits (see PickNewWanderDestination
    // / IsWanderPacedLeg) — the two states control flow actually visits while no Living Room anywhere
    // has an open spot.
    private bool IsIdlePacingWithoutRelaxSpot()
    {
        if (assignedJobRoom != null) return false;
        if (claimedRelaxSpot != null) return false;
        if (CurrentState == BunnyState.Idle) return true;
        if (CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg()) return true;
        return false;
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
        if (claimedRelaxRoom == room) return true;
        if (claimedSleepRoom == room) return true;
        return false;
    }

    public string DebugState()
    {
        string jobRoomName = assignedJobRoom is RoomBase jobRoomBase ? jobRoomBase.name : "NULL";
        string finalRoomName = pendingFinalRoom != null ? pendingFinalRoom.name : "NULL";
        string relaxRoomName = claimedRelaxRoom != null ? claimedRelaxRoom.name : "NULL";
        return $"{name}: currentRoom={(currentRoom != null ? currentRoom.name : "NULL")}, currentState={CurrentState}, pendingStateOnArrival={pendingStateOnArrival}, assignedJobRoom={jobRoomName}, pendingWanderRoom={(pendingWanderRoom != null ? pendingWanderRoom.name : "NULL")}, pendingFinalRoom={finalRoomName}, claimedRelaxRoom={relaxRoomName}, currentWaypointTarget={(currentWaypointTarget != null ? currentWaypointTarget.name : "NULL")}, pos={transform.position}";
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

        // A fallback pacing leg never leaves the bunny's current room (see PickNewWanderDestination),
        // so if a job assignment interrupts one, the worst case is a short, bounded, same-room hop.
        // That's cheap enough to just defer: let the current leg finish naturally, and HandleIdle's
        // existing retry (once idle) picks the job back up.
        if (CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg())
            return;

        // A relax trip CAN cross floors via lift, unlike fallback pacing — interrupting it mid-flight
        // would corrupt the in-progress lift bookkeeping (pendingLift/pendingFinalRoom etc. are single-
        // slot fields already in use for that trip). Safer to let it finish naturally; the
        // BunnyState.Relaxing case in Update() immediately pulls the bunny back out for the job once it
        // settles in, at worst a single frame late.
        if (IsHeadedToRelaxSpot())
            return;

        // Unlike Relaxing (above), a job assignment never interrupts Sleeping at all — the job is left
        // pending (assignedJobRoom set, claimedWorkSpot never claimed) until the bunny actually wakes.
        // Critically, this path never releases claimedSleepSpot: waking for a critical need loops
        // straight back to the same sleep spot regardless of this pending job (see
        // LeaveSleepingForCafeteria/WaterRoom), and only FinishSleepingAndReturnToPrevious (energy
        // reaching 100) clears claimedSleepSpot, at which point ReturnToPreviousActivity's own
        // job-check calls this method again and it proceeds normally.
        if (IsAsleepOrHeadedToSleepSpot())
            return;

        // Pull the bunny out of any already-claimed Living Room spot — a job takes priority. Mirrors
        // UnassignFromJob's claimedWorkSpot release.
        if (claimedRelaxSpot != null)
        {
            claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
            claimedRelaxSpot = null;
            claimedRelaxRoom = null;
        }

        RoomSpot spot = assignedJobRoom.RequestSpot(this);
        if (spot == null) return;

        claimedWorkSpot = spot;

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

        MoveAlongPath(path, spot, BunnyState.Working);
    }

    // True while the bunny is walking toward, or mid-lift-trip toward, an already-claimed Living Room
    // relax spot — covers every leg of that trip (direct walk, or WaitingForLift/RidingLift/
    // DisembarkingLift/final-walk if it crosses floors). Used by RequestNewJobSpot to avoid interrupting
    // an in-flight lift trip; deliberately excludes the "already arrived and sitting" case (CurrentState
    // == Relaxing), which Update()'s Relaxing case handles directly instead.
    private bool IsHeadedToRelaxSpot()
    {
        bool inTransit = CurrentState == BunnyState.MovingToSpot
            || CurrentState == BunnyState.WaitingForLift
            || CurrentState == BunnyState.RidingLift
            || CurrentState == BunnyState.DisembarkingLift;

        if (!inTransit) return false;

        return pendingStateOnArrival == BunnyState.Relaxing
            || (pendingLift != null && pendingFinalState == BunnyState.Relaxing);
    }

    // Broader than IsHeadedToRelaxSpot on purpose: a job assignment must never pull a bunny out of
    // Sleeping at all (unlike Relaxing, which IS pulled out immediately once settled — see the Relaxing
    // case in Update()), so this needs to stay true for the ENTIRE reservation window, not just mid-
    // transit. claimedSleepSpot is set the instant the spot is claimed (before any travel begins, in
    // LeaveWorkForBedroom/LeaveRelaxingForBedroom) and only ever cleared by
    // FinishSleepingAndReturnToPrevious — so checking it directly (rather than CurrentState) is what
    // makes this correct at the exact moment that method clears it and re-enters RequestNewJobSpot via
    // ReturnToPreviousActivity: CurrentState is still Sleeping for that one call, but claimedSleepSpot is
    // already null, so this must NOT block it.
    private bool IsAsleepOrHeadedToSleepSpot()
    {
        return claimedSleepSpot != null;
    }

    private void HandleIdle()
    {
        // Still queued/awaiting gate approval — MoveToQueueSpot parks a bunny in BunnyState.Idle while
        // it waits, but it has no currentRoom yet and hasn't passed the gate. Without this guard, a
        // queued bunny would immediately claim a Living Room spot and walk straight there, skipping the
        // gate/approval flow entirely.
        if (!HasEnteredBase) return;

        if (assignedJobRoom != null && claimedWorkSpot == null)
        {
            RequestNewJobSpot();
            return;
        }
        if (assignedJobRoom != null) return;

        // No job: always try to claim a Living Room spot this tick (cheap, and lets a bunny grab a
        // spot the moment one frees up rather than waiting out a full wanderPauseDuration first).
        if (TryClaimRelaxSpot()) return;

        // No Living Room anywhere on the base has an open spot — pace within the current room only
        // until one frees up, on the same cadence the warning is throttled to.
        wanderTimer += Time.deltaTime;
        if (wanderTimer >= wanderPauseDuration)
        {
            wanderTimer = 0f;
            NotificationToast.Instance?.Show($"{BunnyName} has no relaxing spot available.");
            PickNewWanderDestination();
        }
    }

    // Finds the nearest Living Room with an open relaxing spot (anywhere on the base, crossing floors
    // via lift if needed — unlike the old wandering system, this is the bunny's actual idle destination,
    // not aimless exploration, so there's no reason to bound it to the current floor). Returns false if
    // no Living Room anywhere has room, so HandleIdle can fall back to same-room pacing.
    private bool TryClaimRelaxSpot()
    {
        LivingRoom room = BaseManager.Instance.FindNearestLivingRoomWithSpot(transform.position);
        if (room == null) return false;

        RoomSpot spot = room.RequestSpot(this);
        if (spot == null) return false; // spot claimed by someone else between the check and this call — retry next tick

        claimedRelaxSpot = spot;
        claimedRelaxRoom = room;

        if (currentRoom != null && currentFloorIndex != room.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(room, spot, BunnyState.Relaxing))
            {
                room.ReleaseSpot(spot, this);
                claimedRelaxSpot = null;
                claimedRelaxRoom = null;
                return false;
            }
            return true;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, room, spot, currentFloorIndex);
        MoveAlongPath(path, spot, BunnyState.Relaxing);
        return true;
    }

    // Fallback for when no Living Room anywhere on the base has an open relax spot (see
    // TryClaimRelaxSpot/HandleIdle) — the bunny paces back and forth within its OWN current room only,
    // never picks a neighboring room. This exists purely so a bunny isn't frozen stiff while waiting for
    // a spot to free up; it deliberately does NOT explore the base the way the old wandering system did.
    private void PickNewWanderDestination()
    {
        if (currentRoom == null) return; // no known room yet — shouldn't normally happen once past first arrival

        List<Transform> wanderPoints = currentRoom.GetWanderPoints();
        if (wanderPoints.Count == 0) return;

        Transform destination = wanderPoints[Random.Range(0, wanderPoints.Count)];

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToWanderPoint(currentRoom, currentSpot, currentWanderPoint, currentRoom, destination, currentFloorIndex);

        if (path.Count == 0) return;

        currentTargetSpot = null; // wandering has no RoomSpot destination
        currentPath = new Queue<Transform>(path);
        pendingStateOnArrival = BunnyState.Wandering;
        CurrentState = BunnyState.MovingToSpot;
        AdvanceToNextWaypoint();

        // Remembered for OnArrivedAtWanderPoint — NOT committed to currentRoom/currentWanderPoint yet,
        // since the bunny hasn't actually walked there.
        pendingWanderRoom = currentRoom;
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

        CurrentState = BunnyState.Idle; // pause, then HandleIdle retries a Living Room spot / paces again
    }

    private void OnArrivedAtRelaxSpot()
    {
        currentRoom = claimedRelaxRoom;
        currentSpot = claimedRelaxSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
    }

    private void OnArrivedAtDrinkingSpot()
    {
        currentRoom = waterRoomBeingUsed;
        currentSpot = currentTargetSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
        waterRoomBeingUsed.NotifyBunnyReadyToDrink(this);
    }

    private void OnArrivedAtSleepingSpot()
    {
        currentRoom = claimedSleepRoom;
        currentSpot = claimedSleepSpot;
        currentWanderPoint = null;
        currentFloorIndex = currentRoom.FloorIndex;
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
            else if (pendingStateOnArrival == BunnyState.Drinking)
                OnArrivedAtDrinkingSpot();
            else if (pendingStateOnArrival == BunnyState.Relaxing)
                OnArrivedAtRelaxSpot();
            else if (pendingStateOnArrival == BunnyState.Sleeping)
                OnArrivedAtSleepingSpot();
            else if (pendingStateOnArrival == BunnyState.Wandering)
                OnArrivedAtWanderPoint();
            else if (pendingStateOnArrival == BunnyState.PassingGate)
                OnArrivedAtGateExit();
            else if (pendingStateOnArrival == BunnyState.Despawning)
                OnArrivedAtDespawnPoint();
            else if (pendingStateOnArrival == BunnyState.WaitingForLift)
                OnArrivedAtLiftLanding();
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

    // True while the bunny should move/animate at wander pace rather than full speed. Used to only be
    // more involved than this single check — it also had to cover the WaitingForLift/RidingLift/
    // DisembarkingLift legs of a lift trip whose ultimate purpose was itself a wander — but wandering
    // never touches a lift at all anymore (see PickNewWanderDestination), so a lift leg can now only
    // ever be for a real job or the cafeteria, never wandering.
    private bool IsWanderPacedLeg()
    {
        return pendingStateOnArrival == BunnyState.Wandering;
    }

    public void EnterBaseAndWander(RoomBase startingRoom = null, Transform startingPoint = null)
    {
        HasEnteredBase = true;
        currentRoom = startingRoom;
        currentSpot = null;
        currentWanderPoint = startingPoint;
        currentFloorIndex = startingRoom != null ? startingRoom.FloorIndex : 0;
        CurrentState = BunnyState.Idle; // triggers HandleIdle -> seeks a Living Room spot
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
            // Already mid-trip (waiting for / riding / walking off a lift) from an earlier job/eating
            // assignment — wandering never touches a lift at all now (see PickNewWanderDestination), so
            // this can only mean an equally-important trip is already in flight. Leave it alone:
            // overwriting pendingFinalRoom/Spot/Lift out from under it would make IT resolve against
            // the wrong destination once it finishes — the bunny would end up "arriving" somewhere that
            // doesn't match where it actually is.
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
    // landing/waiting spot to the boarding point (near the shaft), then actually starts riding. The
    // bunny stays visible for the whole ride (see DisembarkFromLift) — there's no door animation yet to
    // mask a hide/reveal, and the scripted hide/reveal was itself the source of more than one
    // "disappears and never comes back" bug (races between this coroutine and a fast/near-zero-time
    // ride — see git history), so it was removed entirely rather than patched again.
    public void BoardLift(Transform boardingSpot)
    {
        if (boardingSpot == null)
        {
            CurrentState = BunnyState.RidingLift;
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

    // Called by LiftRoom once it's arrived at this bunny's destination floor. Teleports to the new
    // floor's boarding point (the bunny was visibly standing at the origin boarding point the whole
    // ride — see BoardLift), then walks the short distance back out to the landing/waiting spot before
    // resuming whatever trip was in progress.
    public void DisembarkFromLift(Transform landingSpot, Transform boardingSpot, int floorIndex)
    {
        currentFloorIndex = floorIndex;

        Transform arrivalPoint = boardingSpot != null ? boardingSpot : landingSpot;
        if (arrivalPoint != null)
            transform.position = arrivalPoint.position;

        currentPath = new Queue<Transform>();
        currentWaypointTarget = null;
        CurrentState = BunnyState.Idle;

        StartCoroutine(WalkOutAfterDelay(landingSpot, boardingSpot, liftDisembarkRevealDelay));
    }

    // Brief settling pause after teleporting onto the new floor before walking out — leaves room for a
    // doors-opening animation later, once one exists.
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

    // Priority chain: whichever need crosses its threshold first wins, and Update()'s dispatch only
    // calls this again once the bunny is back in Working/Relaxing (satiated) — so within a single leave
    // the highest-priority need in code order (hunger, then thirst, then tired) is the one acted on.
    private void HandleNeedsCheckWhileWorking()
    {
        if (IsHungry)
            LeaveWorkForCafeteria();
        else if (IsThirsty)
            LeaveWorkForWaterRoom();
        else if (IsTired)
            LeaveWorkForBedroom();
    }

    private void HandleNeedsCheckWhileRelaxing()
    {
        if (IsHungry)
            LeaveRelaxingForCafeteria();
        else if (IsThirsty)
            LeaveRelaxingForWaterRoom();
        else if (IsTired)
            LeaveRelaxingForBedroom();
    }

    // Sleeping is only ever interrupted by a CRITICAL hunger/thirst threshold (well below the normal
    // low threshold) — normal hunger/thirst are ignored while asleep, and a job assignment never
    // interrupts it at all (see the Sleeping case in Update()).
    private void HandleCriticalNeedsCheckWhileSleeping()
    {
        if (IsCriticallyHungry)
            LeaveSleepingForCafeteria();
        else if (IsCriticallyThirsty)
            LeaveSleepingForWaterRoom();
    }

    private void WarnNoEatSpot()
    {
        if (Time.time - lastNoEatSpotWarningTime < noSpotWarningCooldown) return;
        lastNoEatSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is hungry, but there are no eating spots available.");
    }

    private void WarnNoDrinkSpot()
    {
        if (Time.time - lastNoDrinkSpotWarningTime < noSpotWarningCooldown) return;
        lastNoDrinkSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is thirsty, but there are no drinking spots available.");
    }

    private void WarnNoSleepSpot()
    {
        if (Time.time - lastNoSleepSpotWarningTime < noSpotWarningCooldown) return;
        lastNoSleepSpotWarningTime = Time.time;
        NotificationToast.Instance?.Show($"{BunnyName} is tired, but there are no sleeping spots available.");
    }

    private void LeaveWorkForCafeteria()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        DebugLog.Log($"Cafeteria found: {cafeteria}");
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return; // no cafeteria available — stay working, hunger check retries next frame
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        DebugLog.Log($"Eat spot found: {eatSpot}");
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return; // cafeteria full — stay working, hunger check retries next frame
        }

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

        DebugLog.Log($"Path built with {path.Count} points:");
        foreach (Transform t in path)
            DebugLog.Log($" - {(t != null ? t.name : "NULL")} at {(t != null ? t.position.ToString() : "N/A")}");

        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }
    private void LeaveRelaxingForCafeteria()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return;
        }

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

    // ---------- THIRST (WATER ROOM DRINKING SIDE) ----------
    // Structural copies of LeaveWorkForCafeteria/LeaveRelaxingForCafeteria — spot stays reserved while
    // away (paused, not released), same as the hunger pair.

    private void LeaveWorkForWaterRoom()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        waterRoomBeingUsed = waterRoom;

        if (departingRoom.FloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    private void LeaveRelaxingForWaterRoom()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        if (departingRoom != null && currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    // ---------- ENERGY (BEDROOM) ----------
    // Structural copies of the relax pair, but the claim persists once arrived — nothing auto-returns
    // the bunny (Bedroom has no coroutine; NPCBunny's own Update() Sleeping case decides when to leave).

    private void LeaveWorkForBedroom()
    {
        RoomBase departingRoom = (RoomBase)assignedJobRoom;
        RoomSpot departingSpot = claimedWorkSpot;

        BedroomRoom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            return;
        }

        assignedJobRoom?.NotifyBunnyLeavingToEat(this);

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        if (departingRoom.FloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    private void LeaveRelaxingForBedroom()
    {
        RoomBase departingRoom = claimedRelaxRoom;
        RoomSpot departingSpot = claimedRelaxSpot;

        BedroomRoom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            return;
        }

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        if (departingRoom != null && currentFloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    // ---------- CRITICAL-NEED WAKE (SLEEPING -> EATING/DRINKING) ----------
    // Departing spot is the SLEEP spot, which stays reserved throughout — unconditionally, regardless
    // of any pending job. FinishEatingAndReturnToWork / FinishDrinkingAndReturnToPrevious route back to
    // it via ReturnToPreviousActivity's top-priority sleep check once satiated.

    private void LeaveSleepingForCafeteria()
    {
        RoomBase departingRoom = claimedSleepRoom;
        RoomSpot departingSpot = claimedSleepSpot;

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            return;
        }

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

    private void LeaveSleepingForWaterRoom()
    {
        RoomBase departingRoom = claimedSleepRoom;
        RoomSpot departingSpot = claimedSleepSpot;

        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        if (departingRoom != null && currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
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

    // Called by WaterRoom each time a water-consumption tick happens
    public void ReceiveWaterHydration()
    {
        thirst = Mathf.Min(100f, thirst + thirstGainPerDrink);
    }

    public bool IsFullyHydrated()
    {
        return thirst >= thirstThresholdFull;
    }

    // Called by CafeteriaRoom once bunny is full (or can't get more carrots)
    public void FinishEatingAndReturnToWork()
    {
        if (cafeteriaBeingUsed != null)
        {
            cafeteriaBeingUsed.ReleaseSpot(currentTargetSpot, this);
            cafeteriaBeingUsed = null;
        }

        // If thirst has ALSO crossed its threshold, chain straight to the Water Room from here instead
        // of walking all the way back to work/relaxing just to immediately leave again — saves a
        // pointless round trip, especially on a large base. Sleep-origin trips only chain on the
        // CRITICAL threshold (matching Sleeping's own critical-only interrupt rule — normal thirst is
        // ignored while asleep); Working/Relaxing-origin trips chain on the normal LOW threshold, since
        // that's what would trigger the interrupt in the first place. claimedSleepSpot/claimedWorkSpot/
        // claimedRelaxSpot all stay reserved through the whole chain, so the eventual
        // ReturnToPreviousActivity() at the end still routes back to the SAME spot regardless of how
        // many stops were chained first.
        bool shouldChainToWater = claimedSleepSpot != null ? IsCriticallyThirsty : IsThirsty;
        if (shouldChainToWater)
        {
            ChainToWaterRoomFromCurrentSpot();
            return;
        }

        // Hunger and thirst are both settled — if tiredness is ALSO a problem, chain straight to the
        // Bedroom instead of walking back to work/relaxing first. Only when NOT already mid-sleep
        // (claimedSleepSpot == null): a bunny woken from sleep for a critical need always resumes the
        // SAME sleep spot via ReturnToPreviousActivity's own priority below, never a fresh Bedroom trip
        // from here — tiredness is checked last and only matters for a Working/Relaxing-origin chain.
        if (claimedSleepSpot == null && IsTired)
        {
            ChainToBedroomFromCurrentSpot();
            return;
        }

        ReturnToPreviousActivity();
    }

    // Called by WaterRoom once bunny is fully hydrated (or can't get more water)
    public void FinishDrinkingAndReturnToPrevious()
    {
        if (waterRoomBeingUsed != null)
        {
            waterRoomBeingUsed.ReleaseDrinkingSpot(currentTargetSpot, this);
            waterRoomBeingUsed = null;
        }

        // Reverse of FinishEatingAndReturnToWork's chain, above — same critical-vs-low threshold split
        // by origin.
        bool shouldChainToCafeteria = claimedSleepSpot != null ? IsCriticallyHungry : IsHungry;
        if (shouldChainToCafeteria)
        {
            ChainToCafeteriaFromCurrentSpot();
            return;
        }

        // Same tiredness fallback as FinishEatingAndReturnToWork, above — reached here when Thirst was
        // the originally-triggering need instead of Hunger.
        if (claimedSleepSpot == null && IsTired)
        {
            ChainToBedroomFromCurrentSpot();
            return;
        }

        ReturnToPreviousActivity();
    }

    // Structural copy of the LeaveXForWaterRoom methods, but routes from the bunny's CURRENT position
    // (it just finished eating at the Cafeteria) rather than from whatever spot it left behind — used to
    // chain directly from Eating to Drinking without detouring back to work/relaxing/bed first, whatever
    // the origin was. See FinishEatingAndReturnToWork.
    private void ChainToWaterRoomFromCurrentSpot()
    {
        WaterRoom waterRoom = BaseManager.Instance.FindNearestWaterRoomWithDrinkingSpot(transform.position);
        if (waterRoom == null)
        {
            WarnNoDrinkSpot();
            ReturnToPreviousActivity(); // no Water Room available — fall back to sleep; the next critical check retries once actually asleep again
            return;
        }

        RoomSpot drinkSpot = waterRoom.RequestDrinkingSpot(this);
        if (drinkSpot == null)
        {
            WarnNoDrinkSpot();
            ReturnToPreviousActivity();
            return;
        }

        waterRoomBeingUsed = waterRoom;

        if (currentRoom != null && currentFloorIndex != waterRoom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(waterRoom, drinkSpot, BunnyState.Drinking))
            {
                waterRoom.ReleaseDrinkingSpot(drinkSpot, this);
                waterRoomBeingUsed = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, waterRoom, drinkSpot, currentFloorIndex);
        MoveAlongPath(path, drinkSpot, BunnyState.Drinking);
    }

    // Structural copy of the LeaveXForCafeteria methods, but routes from the bunny's CURRENT position
    // (it just finished drinking at the Water Room) rather than from whatever spot it left behind —
    // reverse of ChainToWaterRoomFromCurrentSpot, see FinishDrinkingAndReturnToPrevious.
    private void ChainToCafeteriaFromCurrentSpot()
    {
        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteriaWithSpot(transform.position);
        if (cafeteria == null)
        {
            WarnNoEatSpot();
            ReturnToPreviousActivity();
            return;
        }

        RoomSpot eatSpot = cafeteria.RequestSpot(this);
        if (eatSpot == null)
        {
            WarnNoEatSpot();
            ReturnToPreviousActivity();
            return;
        }

        cafeteriaBeingUsed = cafeteria;

        if (currentRoom != null && currentFloorIndex != cafeteria.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(cafeteria, eatSpot, BunnyState.Eating))
            {
                cafeteria.ReleaseSpot(eatSpot, this);
                cafeteriaBeingUsed = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, cafeteria, eatSpot, currentFloorIndex);
        MoveAlongPath(path, eatSpot, BunnyState.Eating);
    }

    // Structural copy of the LeaveXForBedroom methods, but routes from the bunny's CURRENT position (it
    // just finished eating/drinking) rather than from whatever spot it left behind — chains straight to
    // the Bedroom as the last link in the hunger/thirst/tired chain. Only ever called when
    // claimedSleepSpot == null (see FinishEatingAndReturnToWork/FinishDrinkingAndReturnToPrevious), so
    // this always represents a genuinely NEW sleep claim, never a currently-sleeping bunny.
    private void ChainToBedroomFromCurrentSpot()
    {
        BedroomRoom bedroom = BaseManager.Instance.FindNearestBedroomWithSpot(transform.position);
        if (bedroom == null)
        {
            WarnNoSleepSpot();
            ReturnToPreviousActivity();
            return;
        }

        RoomSpot sleepSpot = bedroom.RequestSpot(this);
        if (sleepSpot == null)
        {
            WarnNoSleepSpot();
            ReturnToPreviousActivity();
            return;
        }

        claimedSleepSpot = sleepSpot;
        claimedSleepRoom = bedroom;

        if (currentRoom != null && currentFloorIndex != bedroom.FloorIndex)
        {
            if (!TryBeginCrossFloorTripToSpot(bedroom, sleepSpot, BunnyState.Sleeping))
            {
                bedroom.ReleaseSpot(sleepSpot, this);
                claimedSleepSpot = null;
                claimedSleepRoom = null;
                ReturnToPreviousActivity();
            }
            return;
        }

        List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, bedroom, sleepSpot, currentFloorIndex);
        MoveAlongPath(path, sleepSpot, BunnyState.Sleeping);
    }

    // Called from Update()'s Sleeping case once energy reaches 100. Releases the sleep claim FIRST —
    // this is what distinguishes this wake path from the critical-need wake
    // (LeaveSleepingForCafeteria/WaterRoom), which deliberately never clears claimedSleepSpot so a
    // return trip after eating/drinking always loops back to sleep no matter what else is pending. Here,
    // clearing it first means ReturnToPreviousActivity's own sleep-check correctly falls through to the
    // job/relax/idle chain instead of looping back to sleep.
    private void FinishSleepingAndReturnToPrevious()
    {
        if (claimedSleepRoom != null)
            claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);

        claimedSleepSpot = null;
        claimedSleepRoom = null;

        ReturnToPreviousActivity();
    }

    // Shared by all three "done with an interrupt, resume whatever I was doing" paths above (eating,
    // drinking, and finishing a sleep). Sleep is checked FIRST, ahead of the job check — a bunny that
    // just finished eating/drinking while still mid-sleep (claimedSleepSpot still reserved — see
    // LeaveSleepingForCafeteria/WaterRoom) always walks straight back to sleep regardless of any pending
    // job. Only once claimedSleepSpot has actually been released (by FinishSleepingAndReturnToPrevious)
    // does the job -> relax -> idle chain below get a turn.
    private void ReturnToPreviousActivity()
    {
        if (claimedSleepSpot != null)
        {
            BedroomRoom sleepRoomBase = claimedSleepRoom;

            if (currentRoom != null && currentFloorIndex != sleepRoomBase.FloorIndex)
            {
                if (!TryBeginCrossFloorTripToSpot(sleepRoomBase, claimedSleepSpot, BunnyState.Sleeping))
                {
                    // No lift back to the sleep floor — release the spot and let a later needs-check retry.
                    claimedSleepRoom.ReleaseSpot(claimedSleepSpot, this);
                    claimedSleepSpot = null;
                    claimedSleepRoom = null;
                    CurrentState = BunnyState.Idle;
                }
                return;
            }

            // Spot was never released while eating/drinking — walk straight back to it.
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, sleepRoomBase, claimedSleepSpot, currentFloorIndex);
            MoveAlongPath(path, claimedSleepSpot, BunnyState.Sleeping);
            return;
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

                // Spot was never released while eating/drinking/sleeping — walk straight back to it.
                List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, jobRoomBase, claimedWorkSpot, currentFloorIndex);
                MoveAlongPath(path, claimedWorkSpot, BunnyState.Working);
            }
            else
            {
                RequestNewJobSpot();
            }
        }
        else if (claimedRelaxSpot != null)
        {
            RoomBase relaxRoomBase = claimedRelaxRoom;

            if (currentRoom != null && currentFloorIndex != relaxRoomBase.FloorIndex)
            {
                if (!TryBeginCrossFloorTripToSpot(relaxRoomBase, claimedRelaxSpot, BunnyState.Relaxing))
                {
                    // No lift back to the relax floor — release the spot and let HandleIdle claim a new one.
                    claimedRelaxRoom.ReleaseSpot(claimedRelaxSpot, this);
                    claimedRelaxSpot = null;
                    claimedRelaxRoom = null;
                    CurrentState = BunnyState.Idle;
                }
                return;
            }

            // Spot was never released while eating/drinking — walk straight back to it.
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, currentWanderPoint, relaxRoomBase, claimedRelaxSpot, currentFloorIndex);
            MoveAlongPath(path, claimedRelaxSpot, BunnyState.Relaxing);
        }
        else
        {
            CurrentState = BunnyState.Idle; // HandleIdle claims a fresh relax spot (or paces) from here
        }
    }
    // ---------- ANIMATION ----------

    private void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetBool(isMovingParam, CurrentState == BunnyState.MovingToSpot);
        animator.SetBool(isWorkingParam, CurrentState == BunnyState.Working);
        animator.SetBool(isEatingParam, CurrentState == BunnyState.Eating);
        animator.SetBool(isDrinkingParam, CurrentState == BunnyState.Drinking);
        animator.SetBool(isSleepingParam, CurrentState == BunnyState.Sleeping);

        bool isSlowWander = CurrentState == BunnyState.MovingToSpot && IsWanderPacedLeg();
        animator.speed = isSlowWander ? wanderSpeedMultiplier : 1f;
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