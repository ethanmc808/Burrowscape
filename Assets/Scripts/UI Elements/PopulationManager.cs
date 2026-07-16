using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

// A resident always belongs to exactly one of these at a time. Bunnies move BETWEEN these buckets
// (see MoveResident) as they head out on quests/foraging or come back, or as an egg hatches — none
// of those events change the total population, only which bucket it's counted under.
public enum ResidentCategory
{
    InBase,
    Questing,
    Foraging,
    Egg
}

// Deliberately NOT derived from counting live NPCBunny GameObjects (that's what DwellerRoster is for,
// and it isn't suited to this job — see design discussion). This is a plain ledger, same pattern as
// CarrotManager: a set of counts that only change when something explicitly tells them to. That means
// every system that changes the colony's population (gate approval, breeding, quests, foraging, death,
// banishment) is responsible for calling the right method here — nothing is inferred automatically.
public class PopulationManager : MonoBehaviour
{
    public static PopulationManager Instance { get; private set; }

    private readonly Dictionary<ResidentCategory, int> counts = new Dictionary<ResidentCategory, int>
    {
        { ResidentCategory.InBase, 0 },
        { ResidentCategory.Questing, 0 },
        { ResidentCategory.Foraging, 0 },
        { ResidentCategory.Egg, 0 },
    };

    public event Action OnPopulationChanged;

    public int TotalResidents => counts.Values.Sum();
    public int GetCount(ResidentCategory category) => counts[category];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Call this the moment a resident joins the colony for the FIRST time — a wild bunny approved
    // through the gate, or a freshly-laid egg (reserving its population slot immediately, before it
    // hatches). Do NOT call this for a bunny returning from a quest/foraging trip or an egg hatching —
    // those bunnies were already counted; use MoveResident for them instead.
    public void AddNewResident(ResidentCategory intoCategory)
    {
        counts[intoCategory]++;
        OnPopulationChanged?.Invoke();
    }

    // Shifts an ALREADY-counted resident from one bucket to another — departing on a quest/foraging
    // trip, returning from one, or an egg hatching into a living bunny (Egg -> InBase). Total
    // population never changes here, only which category it's counted under.
    public void MoveResident(ResidentCategory from, ResidentCategory to)
    {
        counts[from] = Mathf.Max(0, counts[from] - 1);
        counts[to]++;
        OnPopulationChanged?.Invoke();
    }

    // Call this when a resident permanently leaves the colony — death, or (future) banishment.
    public void RemoveResident(ResidentCategory fromCategory)
    {
        counts[fromCategory] = Mathf.Max(0, counts[fromCategory] - 1);
        OnPopulationChanged?.Invoke();
    }
}