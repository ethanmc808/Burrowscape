using UnityEngine;
using System.Collections.Generic;

public class GateQueueManager : MonoBehaviour
{
    public static GateQueueManager Instance { get; private set; }

    [SerializeField] private List<Transform> queueSpots; // ordered: index 0 = front (closest to gate)

    private List<NPCBunny> currentQueue = new List<NPCBunny>();

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
}