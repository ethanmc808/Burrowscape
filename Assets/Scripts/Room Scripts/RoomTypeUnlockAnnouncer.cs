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
        // Silently seeds announcedUnlocks with whatever's already unlocked at scene start (starting
        // rooms with no unlock conditions, or ones the starting population already clears) — these were
        // never genuinely "new," so they must NOT trigger a notification. Only unlocks that happen from
        // here on (population growing past a threshold) go through CheckForNewUnlocks' normal path below.
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
