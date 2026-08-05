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

    private void Start()
    {
        // Silently seeds unlockedLocations with whatever the STARTING population already clears (e.g. a
        // populationThreshold of 0) — these were never genuinely "new," so the very first IsUnlocked()
        // call on them (as soon as the foraging dispatch UI opens) must not fire a reveal notification.
        // Only a location crossed by population growth AFTER this point goes through IsUnlocked's normal
        // notify-on-first-unlock path below.
        if (ForagingManager.Instance == null) return;
        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            if (location != null && population >= location.populationThreshold)
                unlockedLocations.Add(location);
        }
    }

    public bool IsUnlocked(ForagingLocationDefinition location)
    {
        if (unlockedLocations.Contains(location)) return true;

        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        if (population >= location.populationThreshold)
        {
            unlockedLocations.Add(location);
            // Fires exactly once per location — the moment it self-latches into unlockedLocations above,
            // never again for that location afterward.
            NotificationManager.Instance?.Show(NotificationType.ForagingLocationUnlocked, location.displayName);
            return true;
        }
        return false;
    }

    // ---------- Save/load ----------
    // Saved by displayName (the HashSet holds direct asset references, which can't round-trip through
    // JSON) — resolved back against ForagingManager.Instance.Locations on import, same source Start()
    // already reads from.
    public IEnumerable<string> ExportUnlockedLocationNames()
    {
        foreach (ForagingLocationDefinition location in unlockedLocations)
            if (location != null) yield return location.displayName;
    }

    public void ImportUnlockedLocationNames(IEnumerable<string> names)
    {
        unlockedLocations.Clear();
        if (names == null || ForagingManager.Instance == null) return;

        HashSet<string> nameSet = new HashSet<string>(names);
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            if (location != null && nameSet.Contains(location.displayName))
                unlockedLocations.Add(location);
        }
    }
}
