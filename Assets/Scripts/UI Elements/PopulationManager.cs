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

    [Header("Population Cap")]
    // Flat baseline the colony can house before any Bedroom is built — same "base + sum of registered
    // contributors" shape CarrotManager/WaterManager/PowerManager already use for their own storage caps.
    [SerializeField] private int basePopulationCap = 16;

    // Hard ceiling regardless of how many Bedrooms get built. Deliberately a SEPARATE field from
    // WildBunnySpawner.capPopulation (which only paces that spawner's arrival-interval/level ramp) rather
    // than sharing one number, so the two can be tuned independently if the design calls for it later.
    [SerializeField] private int maxPopulationCap = 300;

    // Any Bedroom currently enabled in the scene — registered/unregistered by Bedroom.OnEnable/OnDisable,
    // same pattern as CarrotManager.capacityContributors. A SEPARATE registration from BaseManager's own
    // bedrooms list (which exists for nearest-with-a-spot lookups) — same rooms, two independent lists
    // for two independent purposes.
    private readonly List<Bedroom> capacityContributors = new List<Bedroom>();

    private int populationCap;
    public int PopulationCap => populationCap;

    // What WildBunnySpawner checks before creating a new Wild bunny at all — the cap is enforced at the
    // spawn source rather than by turning bunnies away at the gate, so the colony never accumulates a
    // backlog of already-spawned bunnies waiting outside for a slot that isn't coming soon.
    public bool HasRoomForNewResident => TotalResidents < populationCap;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        RecomputePopulationCap();
    }

    // Called by Bedroom.OnEnable/OnDisable.
    public void RegisterBedroomCapacity(Bedroom room)
    {
        if (!capacityContributors.Contains(room)) capacityContributors.Add(room);
        RecomputePopulationCap();
    }

    public void UnregisterBedroomCapacity(Bedroom room)
    {
        capacityContributors.Remove(room);
        RecomputePopulationCap();
    }

    // Recomputes from scratch rather than incrementally adding/subtracting — same reasoning as
    // CarrotManager/WaterManager/PowerManager's equivalent methods: avoids float/int drift and naturally
    // handles a Bedroom's SleepingSpotCount changing (e.g. a future upgrade) without special-casing.
    private void RecomputePopulationCap()
    {
        int uncapped = basePopulationCap + capacityContributors.Where(r => r != null).Sum(r => r.PopulationCapContribution);
        populationCap = Mathf.Min(maxPopulationCap, uncapped);
        OnPopulationChanged?.Invoke(); // cap changed — piggyback on the same event UI already listens on
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

    // Save-load only — this ledger is independent, explicitly-maintained state (see class comment), not
    // derivable by counting live bunnies, so a load has to overwrite every bucket directly rather than
    // replaying Add/Move/Remove calls.
    public void SetCounts(int inBase, int questing, int foraging, int egg)
    {
        counts[ResidentCategory.InBase] = Mathf.Max(0, inBase);
        counts[ResidentCategory.Questing] = Mathf.Max(0, questing);
        counts[ResidentCategory.Foraging] = Mathf.Max(0, foraging);
        counts[ResidentCategory.Egg] = Mathf.Max(0, egg);
        OnPopulationChanged?.Invoke();
    }
}