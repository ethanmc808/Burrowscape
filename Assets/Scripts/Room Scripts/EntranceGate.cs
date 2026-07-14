using UnityEngine;
using System.Collections;

public enum GateState
{
    Closed,
    Opening,
    Open,
    Closing
}

public class EntranceGate : MonoBehaviour
{
    public static EntranceGate Instance { get; private set; }

    public GateState CurrentState { get; private set; } = GateState.Closed;

    [SerializeField] private float openDuration = 3f;
    [SerializeField] private float closeDuration = 3f;

    private int activeTraffic = 0; // bunnies currently queued/waiting/passing through
    private Coroutine gateRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Call this when a bunny needs to pass through (either direction)
    public void RequestPassage()
    {
        activeTraffic++;

        if (CurrentState == GateState.Closed)
        {
            StartTransition(GateState.Opening);
        }
        else if (CurrentState == GateState.Closing)
        {
            // Let the close finish, then it will detect activeTraffic > 0 and reopen automatically
        }
        // If already Opening or Open, nothing extra needed — bunny just waits for Open state
    }

    // Call this once a bunny has fully cleared the gate (finished walking through)
    public void NotifyPassageComplete()
    {
        activeTraffic = Mathf.Max(0, activeTraffic - 1);

        if (activeTraffic <= 0 && CurrentState == GateState.Open)
        {
            StartTransition(GateState.Closing);
        }
    }

    private void StartTransition(GateState newState)
    {
        if (gateRoutine != null)
            StopCoroutine(gateRoutine);

        gateRoutine = StartCoroutine(TransitionRoutine(newState));
    }

    private IEnumerator TransitionRoutine(GateState toState)
    {
        CurrentState = toState;
        Debug.Log($"Gate: entering {toState}");

        float duration = toState == GateState.Opening ? openDuration : closeDuration;
        yield return new WaitForSeconds(duration);

        if (toState == GateState.Opening)
        {
            CurrentState = GateState.Open;
            Debug.Log("Gate: now Open");
        }
        else if (toState == GateState.Closing)
        {
            // Re-check: did new traffic arrive during the closing animation?
            if (activeTraffic > 0)
            {
                Debug.Log("Gate: traffic arrived during closing, reopening");
                StartTransition(GateState.Opening);
            }
            else
            {
                CurrentState = GateState.Closed;
                Debug.Log("Gate: now Closed");
            }
        }
    }

    // ---------- DEBUG/TEST ----------

    [ContextMenu("Debug: Request Passage")]
    private void DebugRequestPassage()
    {
        RequestPassage();
    }

    [ContextMenu("Debug: Notify Passage Complete")]
    private void DebugNotifyComplete()
    {
        NotifyPassageComplete();
    }
}