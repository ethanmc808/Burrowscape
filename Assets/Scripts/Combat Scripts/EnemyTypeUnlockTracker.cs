using System.Collections.Generic;
using UnityEngine;

// Sticky unlock ledger for enemy types — once population has ever crossed an enemy's threshold, it stays
// available for invasions even if population later drops (deaths, banishment). Mirrors
// BunnyTypeUnlockTracker's exact permanent-latch pattern, just keyed by the EnemyDefinition asset itself
// rather than an enum — enemies have no BunnyType-style identity enum of their own (EnemyDefinition IS the
// unique identity), and Ethan explicitly wants unlock thresholds hand-authored per enemy rather than by
// any type/category grouping. InvasionManager asks IsUnlocked before every spawn roll (both raid types)
// and this checks+latches on demand, same "nothing else tracks this" reasoning as the bunny version.
public class EnemyTypeUnlockTracker : MonoBehaviour
{
    public static EnemyTypeUnlockTracker Instance { get; private set; }

    private readonly HashSet<EnemyDefinition> unlockedTypes = new HashSet<EnemyDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool IsUnlocked(EnemyDefinition def)
    {
        if (unlockedTypes.Contains(def)) return true;

        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        if (population >= def.populationThreshold)
        {
            unlockedTypes.Add(def);
            return true;
        }
        return false;
    }
}
