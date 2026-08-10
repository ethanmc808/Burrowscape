using UnityEngine;

// Centralized on/off switch for the game's verbose informational logging (state transitions, routine
// events like boarding/spawning/UI clicks) — separate from Debug.LogWarning/LogError, which always
// fire regardless of this flag, since those indicate real problems that shouldn't be silenceable.
// Toggle via the checkbox on DebugLogSettings in the scene (before Play) or its hotkey (during Play),
// or just set DebugLog.Verbose directly from anywhere.
public static class DebugLog
{
    public static bool Verbose = true;

    public static void Log(string message)
    {
        if (Verbose) Debug.Log(message);
    }

    public static void Log(string message, Object context)
    {
        if (Verbose) Debug.Log(message, context);
    }
}
