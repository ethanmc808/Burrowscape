using System.Collections.Generic;
using UnityEngine;

// Sticky unlock ledger for bunny types — once population has ever crossed a type's threshold, it stays
// available even if population later drops (deaths, banishment). Mirrors RoomUnlockTracker's permanent-
// flag pattern (Room Scripts/RoomUnlockTracker.cs), but self-latches instead of waiting for an external
// system to call an Unlock method — nothing else currently tracks "type has been unlocked", so
// WildBunnySpawner just asks IsUnlocked before every spawn and this checks+latches on demand.
public class BunnyTypeUnlockTracker : MonoBehaviour
{
    public static BunnyTypeUnlockTracker Instance { get; private set; }

    private readonly HashSet<BunnyType> unlockedTypes = new HashSet<BunnyType>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool IsUnlocked(BunnyTypeDefinition def)
    {
        if (unlockedTypes.Contains(def.type)) return true;

        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        if (population >= def.populationThreshold)
        {
            unlockedTypes.Add(def.type);
            return true;
        }
        return false;
    }
}
