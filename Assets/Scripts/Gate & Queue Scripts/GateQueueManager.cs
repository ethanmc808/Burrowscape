using UnityEngine;
using System.Collections.Generic;

public class GateQueueManager : MonoBehaviour
{
    public static GateQueueManager Instance { get; private set; }

    [SerializeField] private List<Transform> queueSpots; // ordered: index 0 = front (closest to gate)
    [SerializeField] private Transform gateExitPoint; // where a bunny walks to once the gate lets it through
    [SerializeField] private Transform rejectExitPoint; // where a rejected bunny walks to before despawning
    [SerializeField] private RoomBase entranceRoom; // the room gateExitPoint physically sits in, so bunnies know where they are after passing throug
    [SerializeField] private bool autoRequestGatePassage = true; // turn off to test queuing/waiting without the gate auto-opening

    private List<NPCBunny> currentQueue = new List<NPCBunny>();
    private Queue<NPCBunny> waitingBunnies = new Queue<NPCBunny>(); // bunnies waiting for a queue spot to free up
    private NPCBunny pendingRequestBunny; // front-of-queue bunny we've already requested passage for
    private bool frontBunnyCleared; // true once the CURRENT front bunny specifically has been cleared to walk through

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Adds a bunny to the back of the queue, returns the spot it should walk to.
    // If the queue is full, the bunny is placed in a backlog and admitted automatically
    // once a spot frees up (see TryAdmitFromWaitingBacklog).
    public Transform Enqueue(NPCBunny bunny)
    {
        if (currentQueue.Count >= queueSpots.Count)
        {
            waitingBunnies.Enqueue(bunny);
            Debug.Log("Gate queue is full — bunny added to the waiting backlog.");
            return null;
        }

        currentQueue.Add(bunny);
        return queueSpots[currentQueue.Count - 1];
    }

    // Removes a bunny from the queue (approved, rejected, or passed through), shifts everyone
    // forward, and admits the next waiting bunny (if any) into the newly freed spot.
    public void Dequeue(NPCBunny bunny)
    {
        int index = currentQueue.IndexOf(bunny);
        if (index == -1) return;

        currentQueue.RemoveAt(index);
        ShiftQueueForward(index);
        TryAdmitFromWaitingBacklog();
    }

    private void ShiftQueueForward(int fromIndex)
    {
        for (int i = fromIndex; i < currentQueue.Count; i++)
        {
            currentQueue[i].MoveToQueueSpot(queueSpots[i]); // NPCBunny needs this method
        }
    }

    private void TryAdmitFromWaitingBacklog()
    {
        if (waitingBunnies.Count == 0 || currentQueue.Count >= queueSpots.Count) return;

        NPCBunny nextBunny = waitingBunnies.Dequeue();
        currentQueue.Add(nextBunny);
        nextBunny.MoveToQueueSpot(queueSpots[currentQueue.Count - 1]);
    }

    public NPCBunny GetFrontOfQueue()
    {
        return currentQueue.Count > 0 ? currentQueue[0] : null;
    }

    // Called by NPCBunny once it's fully walked past the gate
    public void NotifyBunnyPassedGate(NPCBunny bunny)
    {
        EntranceGate.Instance.NotifyPassageComplete();
    }
    private void Update()
    {
        NPCBunny front = GetFrontOfQueue();

        if (front != pendingRequestBunny)
        {
            pendingRequestBunny?.SetAwaitingApproval(false);
            pendingRequestBunny = front;
            frontBunnyCleared = false; // a new bunny at the front needs its own clearance, regardless of gate state

            if (front != null)
            {
                if (autoRequestGatePassage)
                {
                    EntranceGate.Instance.RequestPassage();
                    frontBunnyCleared = true;
                }
                else
                {
                    front.SetAwaitingApproval(true);
                }
            }
        }

        if (front != null && frontBunnyCleared && EntranceGate.Instance.CurrentState == GateState.Open)
        {
            Dequeue(front);
            front.SetAwaitingApproval(false);
            pendingRequestBunny = null;
            frontBunnyCleared = false;

            // This is the single point every bunny passes through to become (or resume being) a base
            // resident, regardless of whether clearance came from auto-approval or the player clicking
            // Approve — so it's the right spot to update the population count exactly once per bunny.
            if (front.ArrivalType == BunnyArrivalType.Wild)
            {
                // First time ever entering the colony.
                PopulationManager.Instance.AddNewResident(ResidentCategory.InBase);
            }
            else if (front.ArrivalType == BunnyArrivalType.ReturningFromQuest)
            {
                // Already counted (as Questing) when it left — just shift the bucket, don't re-add it.
                PopulationManager.Instance.MoveResident(ResidentCategory.Questing, ResidentCategory.InBase);
            }
            // NOTE: there's no foraging-return case yet since the foraging system doesn't exist. When
            // it's built, either add a BunnyArrivalType.ReturningFromForaging case here, or call
            // PopulationManager.Instance.MoveResident(ResidentCategory.Foraging, ResidentCategory.InBase)
            // directly from wherever that system reintroduces the bunny.

            front.ProceedThroughGate(gateExitPoint, entranceRoom);
        }
    }

    public void ApproveFrontBunny()
    {
        NPCBunny front = GetFrontOfQueue();
        if (front == null) return;

        front.SetAwaitingApproval(false);
        frontBunnyCleared = true;
        EntranceGate.Instance.RequestPassage();
    }

    public void RejectFrontBunny()
    {
        NPCBunny front = GetFrontOfQueue();
        if (front == null) return;

        Dequeue(front);
        front.SetAwaitingApproval(false);
        pendingRequestBunny = null;
        front.RejectAndDespawn(rejectExitPoint);
    }
}