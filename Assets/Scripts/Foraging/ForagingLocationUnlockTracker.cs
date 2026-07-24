using System.Collections.Generic;
using UnityEngine;

// Sticky unlock ledger for Foraging locations — once population has ever crossed a location's
// threshold, it stays available even if population later drops (deaths, banishment). Identical pattern
// to BunnyTypeUnlockTracker (NPC Bunny Scripts/BunnyTypeUnlockTracker.cs), just keyed by
// ForagingLocationDefinition asset reference instead of the BunnyType enum, since locations have no
// natural enum of their own.
public class ForagingLocationUnlockTracker : MonoBehaviour
{
    public static ForagingLocationUnlockTracker Instance { get; private set; }

    private readonly HashSet<ForagingLocationDefinition> unlockedLocations = new HashSet<ForagingLocationDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool IsUnlocked(ForagingLocationDefinition location)
    {
        if (unlockedLocations.Contains(location)) return true;

        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        if (population >= location.populationThreshold)
        {
            unlockedLocations.Add(location);
            return true;
        }
        return false;
    }
}
