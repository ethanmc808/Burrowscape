using UnityEngine;

// TechUnlock is a stub case only: a clear extension point for a future tech-tree system to hook into
// (an enum case rather than an interface, so RoomDefinition's condition list stays a plain
// Inspector-editable array; no tech system exists yet, so it always evaluates to locked).
public enum UnlockConditionType
{
    None,
    PopulationAtLeast,
    PermanentFlag,
    TechUnlock
}

[System.Serializable]
public class RoomUnlockCondition
{
    public UnlockConditionType type;

    [Tooltip("Used only when Type == PopulationAtLeast.")]
    public int populationThreshold;

    // Re-evaluated live every time it's checked (e.g. PopulationAtLeast) vs. checked once true and
    // remembered forever (PermanentFlag, via RoomUnlockTracker) — these behave differently on purpose,
    // per the design doc's distinction between conditional and permanent-once-unlocked conditions.
    public bool IsMet(string roomDefinitionId)
    {
        switch (type)
        {
            case UnlockConditionType.None:
                return true;

            case UnlockConditionType.PopulationAtLeast:
                return PopulationManager.Instance != null
                    && PopulationManager.Instance.TotalResidents >= populationThreshold;

            case UnlockConditionType.PermanentFlag:
                return RoomUnlockTracker.Instance != null
                    && RoomUnlockTracker.Instance.IsPermanentlyUnlocked(roomDefinitionId);

            case UnlockConditionType.TechUnlock:
                // Not implemented yet — no tech system exists. Always locked until that system exists
                // and starts calling RoomUnlockTracker.UnlockPermanently (or a dedicated tech check).
                return false;

            default:
                return false;
        }
    }
}
