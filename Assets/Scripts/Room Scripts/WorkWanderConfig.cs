using UnityEngine;

// Central home for the Working-state local wander pacing (see WorkingWanderPoints_DesignDoc.md) —
// a single global tunable rather than per-room/per-prefab fields, since the dwell timing is meant to
// read consistently across every job room (Guard Room, Water Room, Coal Room, etc.), not vary room to
// room. Deliberately NOT part of CombatBalanceConfig — that asset is scoped to combat numbers per its
// own header comment, and wander pacing applies to non-combat job rooms too.
//
// Loaded via Resources (same pattern as CombatBalanceConfig.Instance) rather than threaded through
// every consumer as a serialized field, since NPCBunny has no single natural owner to hold the
// reference. Must live at Assets/Resources/WorkWanderConfig.asset — created via
// Burrowscape > Generate Work Wander Config (Editor/WorkWanderConfigGenerator.cs), not hand-authored,
// same reasoning as every other generator-seeded asset in this project.
[CreateAssetMenu(fileName = "WorkWanderConfig", menuName = "Burrowscape/Work Wander Config")]
public class WorkWanderConfig : ScriptableObject
{
    private const string ResourcesPath = "WorkWanderConfig";
    private static WorkWanderConfig cachedInstance;
    private static bool hasTriedLoad;

    // Logs once and falls back to a transient default-valued instance (rather than throwing) if the
    // asset hasn't been generated yet — same "degrade gracefully, content not yet authored isn't an
    // error state" philosophy as CombatBalanceConfig.Instance.
    public static WorkWanderConfig Instance
    {
        get
        {
            if (!hasTriedLoad)
            {
                cachedInstance = Resources.Load<WorkWanderConfig>(ResourcesPath);
                hasTriedLoad = true;
                if (cachedInstance == null)
                {
                    Debug.LogWarning("WorkWanderConfig: no asset found at Resources/WorkWanderConfig — using untuned defaults. Run Burrowscape > Generate Work Wander Config.");
                    cachedInstance = CreateInstance<WorkWanderConfig>();
                }
            }
            return cachedInstance;
        }
    }

    [Header("Working-State Local Wander (see WorkingWanderPoints_DesignDoc.md)")]
    [Tooltip("Dwell time at a wander point (or the spot itself) before picking the next destination.")]
    public float workWanderMinIntervalSeconds = 3f;
    public float workWanderMaxIntervalSeconds = 6f;
    [Tooltip("Fraction of normal moveSpeed used while walking between wander points — a working bunny puttering around its post should move slower than one traveling cross-room. Also scales Animator playback speed for the walk cycle so feet don't slide.")]
    [Range(0.05f, 1f)] public float workWanderSpeedMultiplier = 0.3f;
}
