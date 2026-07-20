using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

    // Called by EntranceRoom.OnEnable() so this reference stays valid across a room-upgrade swap — see
    // BaseLayoutManager.SetEntranceRoom for the full explanation. Without this, a bunny approved through
    // the gate after the Entrance Room is upgraded would get handed a destroyed RoomBase reference here.
    public void SetEntranceRoom(RoomBase room)
    {
        entranceRoom = room;
    }

    // Adds a bunny to the back of the queue, returns the spot it should walk to.
    // If the queue is full, the bunny is placed in a backlog and admitted automatically
    // once a spot frees up (see TryAdmitFromWaitingBacklog).
    public Transform Enqueue(NPCBunny bunny)
    {
        if (currentQueue.Count >= queueSpots.Count)
        {
            waitingBunnies.Enqueue(bunny);
            DebugLog.Log("Gate queue is full — bunny added to the waiting backlog.");
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

    // The fixed scene Transform marking the front queue slot (never moves — occupancy is what changes,
    // via ShiftQueueForward). Lets UI (BunnyApprovalUI) anchor itself to a stable world position instead
    // of tracking whichever bunny currently occupies it.
    public Transform FrontQueueSpot => (queueSpots != null && queueSpots.Count > 0) ? queueSpots[0] : null;

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

    // Takes the bunny explicitly (rather than re-deriving GetFrontOfQueue() internally) so the caller's
    // displayed/clicked bunny is the exact one acted on, not just whichever happens to sit at index 0 —
    // validated against GetFrontOfQueue() as a cheap defensive check, since only the front bunny should
    // ever be approvable.
    public void ApproveFrontBunny(NPCBunny bunny)
    {
        NPCBunny front = GetFrontOfQueue();
        if (front == null || front != bunny) return;

        front.SetAwaitingApproval(false);
        frontBunnyCleared = true;
        EntranceGate.Instance.RequestPassage();
    }

    public void RejectFrontBunny(NPCBunny bunny)
    {
        NPCBunny front = GetFrontOfQueue();
        if (front == null || front != bunny) return;

        Dequeue(front);
        front.SetAwaitingApproval(false);
        pendingRequestBunny = null;
        front.RejectAndDespawn(rejectExitPoint);
    }

#if UNITY_EDITOR
    // Same reasoning as RoomBase/RoomSpot's OnDrawGizmos (see RoomBase for the full explanation) — a
    // real Gizmos/Handles draw call instead of a custom icon, immune to the icon-overlay Editor bug.
    // Queue spots are numbered since their order (index 0 = front, closest to the gate) is functionally
    // meaningful, not just visual.
    private void OnDrawGizmos()
    {
        if (queueSpots != null)
        {
            for (int i = 0; i < queueSpots.Count; i++)
            {
                Transform spot = queueSpots[i];
                if (spot == null) continue;
                DrawPointGizmo(spot, new Color(1f, 0.5f, 0f), i == 0 ? $"{spot.name} (Front)" : $"{spot.name} (#{i})");
            }
        }

        DrawPointGizmo(gateExitPoint, Color.blue, gateExitPoint != null ? gateExitPoint.name : null);
        DrawPointGizmo(rejectExitPoint, Color.red, rejectExitPoint != null ? rejectExitPoint.name : null);
    }

    private static void DrawPointGizmo(Transform point, Color color, string label)
    {
        if (point == null) return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(point.position, 0.15f);
        Gizmos.DrawLine(point.position, point.position + Vector3.up * 0.3f);

        Handles.color = color;
        Handles.Label(point.position + Vector3.up * 0.35f, label);
    }
#endif
}