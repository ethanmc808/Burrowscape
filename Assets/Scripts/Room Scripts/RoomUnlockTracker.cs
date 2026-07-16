using UnityEngine;
using System.Collections.Generic;

// Persistent flag store for "permanent-once-unlocked" room conditions (e.g. a blueprint found).
// Nothing calls UnlockPermanently yet — no blueprint/discovery system exists — this is the store a
// future system writes to; RoomUnlockCondition (PermanentFlag case) is the read side.
public class RoomUnlockTracker : MonoBehaviour
{
    public static RoomUnlockTracker Instance { get; private set; }

    private readonly HashSet<string> permanentlyUnlockedIds = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void UnlockPermanently(string roomDefinitionId)
    {
        permanentlyUnlockedIds.Add(roomDefinitionId);
    }

    public bool IsPermanentlyUnlocked(string roomDefinitionId)
    {
        return permanentlyUnlockedIds.Contains(roomDefinitionId);
    }
}
