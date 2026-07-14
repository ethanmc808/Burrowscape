using UnityEngine;
using System.Collections.Generic;

public class GateQueueManager : MonoBehaviour
{
    public static GateQueueManager Instance { get; private set; }

    [SerializeField] private List<Transform> queueSpots; // ordered: index 0 = front (closest to gate)
    [SerializeField] private Transform gateExitPoint; // where a bunny walks to once the gate lets it through
    [SerializeField] private RoomBase entranceRoom; // the room gateExitPoint physically sits in, so bunnies know where they are after passing throug
    [SerializeField] private bool autoRequestGatePassage = true; // turn off to test queuing/waiting without the gate auto-opening

    private List<NPCBunny> currentQueue = new List<NPCBunny>();
    private NPCBunny pendingRequestBunny; // front-of-queue bunny we've already requested passage for

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Adds a bunny to the back of the queue, returns the spot it should walk to
    public Transform Enqueue(NPCBunny bunny)
    {
        if (currentQueue.Count >= queueSpots.Count)
        {
            Debug.LogWarning("Gate queue is full.");
            return null;
        }

        currentQueue.Add(bunny);
        return queueSpots[currentQueue.Count - 1];
    }

    // Removes a bunny from the queue (approved, rejected, or passed through) and shifts everyone forward
    public void Dequeue(NPCBunny bunny)
    {
        int index = currentQueue.IndexOf(bunny);
        if (index == -1) return;

        currentQueue.RemoveAt(index);
        ShiftQueueForward(index);
    }

    private void ShiftQueueForward(int fromIndex)
    {
        for (int i = fromIndex; i < currentQueue.Count; i++)
        {
            currentQueue[i].MoveToQueueSpot(queueSpots[i]); // NPCBunny needs this method
        }
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
        if (front == null)
        {
            pendingRequestBunny = null;
            return;
        }

        if (autoRequestGatePassage && front != pendingRequestBunny)
        {
            // New bunny reached the front — request the gate open (or reopen if it's mid-close)
            EntranceGate.Instance.RequestPassage();
            pendingRequestBunny = front;
        }

        if (EntranceGate.Instance.CurrentState == GateState.Open)
        {
            Dequeue(front);
            pendingRequestBunny = null;
            front.ProceedThroughGate(gateExitPoint, entranceRoom);
        }
    }
}