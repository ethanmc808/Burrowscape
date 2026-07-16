using UnityEngine;

// Single scene instance exposing DebugLog.Verbose as an Inspector checkbox (flip before pressing Play)
// and an in-Play-mode hotkey (flip instantly without stopping/restarting) — see DebugLog.cs for the
// actual gate every verbose Debug.Log call in the project checks.
public class DebugLogSettings : MonoBehaviour
{
    [SerializeField] private bool verboseLogging = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;

    private void Awake()
    {
        DebugLog.Verbose = verboseLogging;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            DebugLog.Verbose = !DebugLog.Verbose;
            // Deliberately a raw Debug.Log, not DebugLog.Log — always print so you know the toggle
            // actually worked even when you just switched verbose logging off.
            Debug.Log($"Verbose logging {(DebugLog.Verbose ? "ENABLED" : "DISABLED")}.");
        }
    }
}
