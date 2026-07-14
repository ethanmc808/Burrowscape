using System.Collections.Generic;
using UnityEngine;

public enum BunnyState
{
    Idle,
    MovingToSpot,
    Working,
    Eating
}

[RequireComponent(typeof(Animator))]
public class NPCBunny : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.5f;
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

    public BunnyState CurrentState { get; private set; } = BunnyState.Idle;
    public bool IsHungry => hunger < hungerThresholdLow;
    public bool IsAssignedToJob => assignedJobRoom != null;

    private RoomSpot currentTargetSpot;
    private Queue<Transform> currentPath;
    private Transform currentWaypointTarget;
    private RoomSpot claimedWorkSpot; // remembers the garden spot to return to after eating
    private IJobRoom assignedJobRoom;
    private CafeteriaRoom cafeteriaBeingUsed;
    private RoomBase currentRoom;
    private RoomSpot currentSpot; // the spot bunny is currently occupying, null if none/mid-transit

    private bool facingRight;
    private bool hasInitializedFacing;

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
        }

        UpdateAnimator();
    }

    // ---------- JOB ASSIGNMENT ----------

    public void AssignToJob(IJobRoom jobRoom)
    {
        assignedJobRoom = jobRoom;
        RequestNewJobSpot();
    }

    private void RequestNewJobSpot()
    {
        if (assignedJobRoom == null) return;

        RoomSpot spot = assignedJobRoom.RequestSpot(this);
        if (spot != null)
        {
            claimedWorkSpot = spot;
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(currentRoom, currentSpot, (RoomBase)assignedJobRoom, spot);
            MoveAlongPath(path, spot, BunnyState.Working);
        }
    }

    private void HandleIdle()
    {
        if (assignedJobRoom != null && claimedWorkSpot == null)
        {
            RequestNewJobSpot();
        }
    }

    // ---------- MOVEMENT ----------

    private void MoveAlongPath(List<Transform> waypoints, RoomSpot spot, BunnyState stateOnArrival)
    {
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
            // No more waypoints — fully arrived
            CurrentState = pendingStateOnArrival;

            if (currentTargetSpot != null)
                SetFacing(currentTargetSpot.FacesRight);   // ADD THIS

            if (pendingStateOnArrival == BunnyState.Working)
                OnArrivedAtWorkSpot();
            else if (pendingStateOnArrival == BunnyState.Eating)
                OnArrivedAtEatingSpot();

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
        transform.position += direction * moveSpeed * Time.deltaTime;

        Debug.Log($"direction.x = {direction.x}");

        if (Mathf.Abs(direction.x) > 0.01f)
            SetFacing(direction.x < 0f);
    }

    // ---------- WORKING (GARDEN) ----------

    private void OnArrivedAtWorkSpot()
    {
        currentRoom = (RoomBase)assignedJobRoom;
        currentSpot = claimedWorkSpot;
        assignedJobRoom.NotifyBunnyReadyToWork(this);
    }

    private void OnArrivedAtEatingSpot()
    {
        currentRoom = cafeteriaBeingUsed;
        currentSpot = currentTargetSpot;
        cafeteriaBeingUsed.NotifyBunnyReadyToEat(this);
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

        if (claimedWorkSpot != null)
        {
            assignedJobRoom.ReleaseSpot(claimedWorkSpot, this);
            claimedWorkSpot = null;
        }

        CafeteriaRoom cafeteria = BaseManager.Instance.FindNearestCafeteria(transform.position);
        Debug.Log($"Cafeteria found: {cafeteria}");

        if (cafeteria != null)
        {
            RoomSpot eatSpot = cafeteria.RequestSpot(this);
            Debug.Log($"Eat spot found: {eatSpot}");

            if (eatSpot != null)
            {
                cafeteriaBeingUsed = cafeteria;
                List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(departingRoom, departingSpot, cafeteria, eatSpot);

                Debug.Log($"Path built with {path.Count} points:");
                foreach (Transform t in path)
                    Debug.Log($" - {(t != null ? t.name : "NULL")} at {(t != null ? t.position.ToString() : "N/A")}");

                MoveAlongPath(path, eatSpot, BunnyState.Eating);
                return;
            }
        }

        CurrentState = BunnyState.Idle;
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
        RequestNewJobSpot();
    else
        CurrentState = BunnyState.Idle;
}
// ---------- ANIMATION ----------

private void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetBool(isMovingParam, CurrentState == BunnyState.MovingToSpot);
        animator.SetBool(isWorkingParam, CurrentState == BunnyState.Working);
        animator.SetBool(isEatingParam, CurrentState == BunnyState.Eating);
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
        Debug.Log($"SetFacing called: shouldFaceRight={shouldFaceRight}, current facingRight={facingRight}, hasInitialized={hasInitializedFacing}");

        if (bunnyScaleRoot == null) return;

        if (!hasInitializedFacing)
        {
            hasInitializedFacing = true;
        }
        else if (shouldFaceRight == facingRight)
        {
            Debug.Log("SetFacing returned early - already facing that direction");
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