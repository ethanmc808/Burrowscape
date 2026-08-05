using System.Collections.Generic;
using UnityEngine;

// Fires NotificationType.RoomTypeUnlocked the moment a RoomDefinition's unlock conditions first become
// true. RoomDefinition.IsUnlocked() is pure/stateless (re-derived from PopulationManager/RoomUnlockTracker
// on every call — see RoomDefinition.cs) unlike BunnyTypeUnlockTracker/ForagingLocationUnlockTracker,
// which self-latch into a HashSet the moment IsUnlocked() first returns true. Nothing else polls
// IsUnlocked() continuously (BuildMenuUI only re-checks when the build menu is opened), so this
// component does the same self-latching by re-checking the whole catalog every time PopulationManager's
// population actually changes — the only thing any current unlock condition (PopulationAtLeast) can key
// off of. If a PermanentFlag/TechUnlock condition starts actually firing later, this would need a second
// trigger source alongside OnPopulationChanged to catch those unlocks too.
public class RoomTypeUnlockAnnouncer : MonoBehaviour
{
    private readonly HashSet<RoomDefinition> announcedUnlocks = new HashSet<RoomDefinition>();

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

    // Silently seeds announcedUnlocks with whatever's already unlocked — starting rooms with no unlock
    // conditions, or ones the current population already clears — these were never genuinely "new," so
    // they must NOT trigger a notification. Only unlocks that happen from here on (population growing
    // past a threshold) go through CheckForNewUnlocks' normal path below.
    //
    // Public — also called explicitly by SaveManager.LoadGame() right after it restores the real
    // population, because Unity gives no ordering guarantee between two different components' own
    // Start() methods. Without this second call, if THIS Start() happened to run before SaveManager's
    // own Start() got around to restoring population, the seed above would run against the population's
    // still-default (0) value, under-seed announcedUnlocks, and then the very next OnPopulationChanged
    // (fired by SaveManager restoring the real count) would wrongly treat every already-unlocked room as
    // brand new and spam a reveal notification for content the player unlocked in a previous session.
    // Calling this twice is harmless — Add() on an already-present entry is a no-op.
    public void SeedAlreadyUnlocked()
    {
        if (BuildMenuUI.Instance == null) return;
        foreach (RoomDefinition definition in BuildMenuUI.Instance.Catalog)
        {
            if (definition != null && definition.IsUnlocked())
                announcedUnlocks.Add(definition);
        }
    }

    private void CheckForNewUnlocks()
    {
        if (BuildMenuUI.Instance == null) return;

        foreach (RoomDefinition definition in BuildMenuUI.Instance.Catalog)
        {
            if (definition == null || announcedUnlocks.Contains(definition)) continue;
            if (!definition.IsUnlocked()) continue;

            announcedUnlocks.Add(definition);
            NotificationManager.Instance?.Show(NotificationType.RoomTypeUnlocked, definition.displayName);
        }
    }
}
