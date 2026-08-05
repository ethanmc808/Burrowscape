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

    private void OnEnable()
    {
        if (PopulationManager.Instance != null)
            PopulationManager.Instance.OnPopulationChanged += CheckForNewUnlocks;
    }

    private void OnDisable()
    {
        if (PopulationManager.Instance != null)
            PopulationManager.Instance.OnPopulationChanged -= CheckForNewUnlocks;
    }

    private void Start()
    {
        SeedAlreadyUnlocked();
    }

    // Silently seeds unlockedLocations with whatever population ALREADY clears at this point (e.g. a
    // populationThreshold of 0, or the starting population) — these were never genuinely "new," so they
    // must NOT fire a reveal notification. Only a location crossed by population growth AFTER this point
    // goes through IsUnlocked's normal notify-on-first-unlock path below. Mirrors
    // RoomTypeUnlockAnnouncer.SeedAlreadyUnlocked's exact pattern for the identical class of bug.
    //
    // Public — also called explicitly by SaveManager.LoadGame() right after it restores the real
    // unlockedLocations set (ImportUnlockedLocationNames, via LoadUnlocks), for the same reason
    // RoomTypeUnlockAnnouncer/WildBunnySpawner's own equivalents need their explicit call: Unity gives no
    // ordering guarantee between different components' Start() methods, and LoadPopulation's
    // OnPopulationChanged fires BEFORE LoadUnlocks restores the real set — without an explicit,
    // correctly-ordered reseed, CheckForNewUnlocks below (subscribed via OnEnable, active well before any
    // Start()) could see an under-seeded unlockedLocations against the now-current population and
    // spuriously re-fire "New Foraging Location Unlocked!" during load for content the player already
    // had. Calling this twice is harmless — HashSet.Add on an already-present entry is a no-op.
    public void SeedAlreadyUnlocked()
    {
        if (ForagingManager.Instance == null) return;
        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
        {
            if (location != null && population >= location.populationThreshold)
                unlockedLocations.Add(location);
        }
    }

    // The missing piece that made the reported bug possible: nothing previously re-checked foraging
    // locations proactively as population grew — IsUnlocked was only ever called on-demand (the dispatch
    // UI building its list, or ForagingManager validating an actual send), so the reveal notification
    // could fire arbitrarily late, or not at all until the player happened to open that UI. Mirrors
    // RoomTypeUnlockAnnouncer.CheckForNewUnlocks exactly.
    private void CheckForNewUnlocks()
    {
        // Same guard, same reason as RoomTypeUnlockAnnouncer.CheckForNewUnlocks — SaveManager.LoadGame's
        // LoadPopulation fires OnPopulationChanged synchronously before its explicit SeedAlreadyUnlocked()
        // call (further down the same method) has corrected unlockedLocations. See SaveManager.IsLoading's
        // own comment.
        if (SaveManager.IsLoading) return;
        if (ForagingManager.Instance == null) return;

        foreach (ForagingLocationDefinition location in ForagingManager.Instance.Locations)
            if (location != null)
                IsUnlocked(location); // self-latches + notifies internally, exactly once per location
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
