using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class WildBunnySpawner : MonoBehaviour
{
    [Tooltip("All BunnyTypeDefinition assets this spawner can choose from. Filtered at spawn time to those with a prefab assigned AND currently unlocked (see BunnyTypeUnlockTracker) — types without art simply never get picked, no code change needed once art ships.")]
    [SerializeField] private List<BunnyTypeDefinition> bunnyTypes;
    // Read by SaveManager to resolve a saved BunnyType enum value back to its BunnyTypeDefinition asset
    // on load — the only list of every type asset that exists anywhere in the project today.
    public IReadOnlyList<BunnyTypeDefinition> BunnyTypes => bunnyTypes;
    [SerializeField] private bool spawnOnStart = false;
    [SerializeField] private float offscreenSpawnOffsetX = 15f; // positive X = further left (visually) in this project

    [Header("Starting Population")]
    [Tooltip("Exact ordered list of the opening bunnies, spawned in Start() via the same path as a normal wild arrival. Meant to be exactly the base 4 (Neutral/Water/Plant/Shock, population 0) — any type beyond that debuts on its own via the guaranteed-debut-spawn mechanic (see GetPendingDebutType) once population crosses its threshold, not via a hardcoded slot in this list.")]
    [SerializeField] private List<BunnyTypeDefinition> startingBunnyOrder;
    [SerializeField] private float startingSpawnDelaySeconds = 0.3f; // gap between each starting bunny so they spawn in a visible line instead of a cluster

    // The 4 types every playthrough starts with (BunnyTypeSystem_DesignDoc.md Group 1 minus Fire) —
    // spawns of these never trigger NotificationType.NewBunnyType, since they're not a "new type" reveal.
    private static readonly HashSet<BunnyType> baseStartingTypes = new HashSet<BunnyType>
    {
        BunnyType.Neutral, BunnyType.Water, BunnyType.Plant, BunnyType.Shock
    };

    // Every type that has ever spawned this session — first-time entries outside baseStartingTypes
    // fire NotificationType.NewBunnyType (see SpawnBunnyOfType).
    private readonly HashSet<BunnyType> typesEverSpawned = new HashSet<BunnyType>();

    [Header("Timed Auto-Spawning")]
    [SerializeField] private bool autoSpawnEnabled = true;

    // How often AutoSpawnLoop rechecks PopulationManager.HasRoomForNewResident while held at the
    // population cap — short on purpose, so a newly-built Bedroom (or a departure) is picked up quickly
    // instead of the spawner waiting out another full randomized interval before trying again.
    [SerializeField] private float capCheckPollSeconds = 5f;

    // Wait-time range ramps linearly from [startMinWaitMinutes, startMaxWaitMinutes] at startPopulation
    // up to [capMinWaitMinutes, capMaxWaitMinutes] at capPopulation, based on PopulationManager.Instance.TotalResidents.
    [Header("Population-Based Arrival Ramp")]
    [SerializeField] private int startPopulation = 8;
    [SerializeField] private int capPopulation = 200;
    [SerializeField] private float startMinWaitMinutes = 1f;
    [SerializeField] private float startMaxWaitMinutes = 3f;
    [SerializeField] private float capMinWaitMinutes = 90f;
    [SerializeField] private float capMaxWaitMinutes = 110f;

    // Level range ramps the same way, off the same startPopulation/capPopulation knobs above (see
    // GetPopulationRampT) — level 1-5 at startPopulation up to 40-50 at capPopulation by default.
    [Header("Population-Based Level Ramp")]
    [SerializeField] private int startMinLevel = 1;
    [SerializeField] private int startMaxLevel = 5;
    [SerializeField] private int capMinLevel = 40;
    [SerializeField] private int capMaxLevel = 50;

    [Header("Starting Needs Randomization")]
    [SerializeField] private float minStartingNeed = 70f;
    [SerializeField] private float maxStartingNeed = 100f;

    private Coroutine autoSpawnRoutine;

    private void Start()
    {
        // A loaded save already has every bunny it needs — the opening spawn sequence below exists only
        // to seed a BRAND NEW game. SaveManager.HasSaveFile() is a pure file-existence check (no
        // dependency on any other singleton's Awake/Start ordering having already run), so this is safe
        // to call unconditionally regardless of script execution order. Ongoing auto-spawn (below) is
        // untouched — arrivals should keep happening normally whether the game started fresh or loaded.
        bool loadingFromSave = SaveManager.HasSaveFile();

        // Only seed when actually RESUMING a previous session, never on a brand-new game — IsUnlocked()
        // self-latches the moment population meets a type's threshold, regardless of whether a bunny of
        // that type has actually spawned yet. On a fresh game, population can already satisfy a
        // threshold (e.g. from the starting bunnies about to be spawned below) before the starting
        // sequence has gotten around to actually spawning that reveal — seeding unconditionally here
        // marked it "already revealed" before it was ever really revealed, silently swallowing the real
        // first-time notification. On an actual reload this is exactly what we want (see
        // SeedAlreadyRevealedTypes' own comment); on a fresh game there's nothing to seed from yet.
        if (loadingFromSave)
            SeedAlreadyRevealedTypes();

        if (spawnOnStart && !loadingFromSave)
        {
            SpawnWildBunny();
        }

        if (!loadingFromSave && startingBunnyOrder != null && startingBunnyOrder.Count > 0)
        {
            // AutoSpawnLoop is deliberately NOT started here — it waits until the tight opening cluster
            // finishes first, so the base 4 spawn as a visible line rather than interleaving with the
            // ramped auto-spawn wait.
            StartCoroutine(SpawnStartingBunniesThenAutoSpawn());
        }
        else if (autoSpawnEnabled)
        {
            autoSpawnRoutine = StartCoroutine(AutoSpawnLoop());
        }
    }

    private IEnumerator SpawnStartingBunniesThenAutoSpawn()
    {
        yield return StartCoroutine(SpawnStartingBunnies());

        if (autoSpawnEnabled)
        {
            autoSpawnRoutine = StartCoroutine(AutoSpawnLoop());
        }
    }

    // Plain tight-cluster spawn of exactly the listed entries (meant to be the base 4 only — see
    // startingBunnyOrder's own tooltip). Any subsequent type's reveal is handled entirely by
    // GetPendingDebutType inside the normal AutoSpawnLoop that starts right after this finishes, so no
    // held-back-wait special case is needed here anymore.
    private IEnumerator SpawnStartingBunnies()
    {
        foreach (BunnyTypeDefinition entry in startingBunnyOrder)
        {
            SpawnBunnyOfType(entry);
            yield return new WaitForSeconds(startingSpawnDelaySeconds);
        }
    }

    private IEnumerator AutoSpawnLoop()
    {
        while (true)
        {
            GetSpawnIntervalRangeSeconds(out float minSeconds, out float maxSeconds);
            float wait = Random.Range(minSeconds, maxSeconds);
            yield return new WaitForSeconds(wait);

            // Held at the population cap — poll rather than spawning into a backlog outside the gate
            // (see PopulationManager.HasRoomForNewResident). Resumes on its own shortly after a Bedroom
            // is built or a slot frees up, instead of waiting out another full randomized interval.
            while (PopulationManager.Instance != null && !PopulationManager.Instance.HasRoomForNewResident)
                yield return new WaitForSeconds(capCheckPollSeconds);

            SpawnWildBunny();
        }
    }

    // Normalized [0,1] position between startPopulation and capPopulation — shared by the spawn-timer
    // ramp (GetSpawnIntervalRangeSeconds) and the level ramp (RollSpawnLevel) so both stay in sync off
    // one set of population knobs, per the design doc's "Level ramp reuses the existing ramp" decision.
    private float GetPopulationRampT()
    {
        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;

        float denominator = capPopulation - startPopulation;
        return denominator > 0f
            ? Mathf.Clamp01((population - startPopulation) / denominator)
            : (population >= capPopulation ? 1f : 0f);
    }

    private void GetSpawnIntervalRangeSeconds(out float minSeconds, out float maxSeconds)
    {
        float t = GetPopulationRampT();
        minSeconds = Mathf.Lerp(startMinWaitMinutes, capMinWaitMinutes, t) * 60f;
        maxSeconds = Mathf.Lerp(startMaxWaitMinutes, capMaxWaitMinutes, t) * 60f;
    }

    // Spawn-time-only level (see BunnyTypeSystem_DesignDoc.md) — rolled once here and never changes
    // afterward. A future XP system would call NPCBunny.LevelUp separately; this has no connection to it.
    private int RollSpawnLevel()
    {
        float t = GetPopulationRampT();
        int minLevel = Mathf.RoundToInt(Mathf.Lerp(startMinLevel, capMinLevel, t));
        int maxLevel = Mathf.RoundToInt(Mathf.Lerp(startMaxLevel, capMaxLevel, t));
        return Mathf.Clamp(Random.Range(minLevel, maxLevel + 1), 1, 50);
    }

    // Silently seeds typesEverSpawned with whatever's already unlocked (a type unlocked in a PREVIOUS
    // session, restored via BunnyTypeUnlockTracker.ImportUnlockedTypes before this runs) — these were
    // never genuinely "new" THIS session, so a bunny of that type spawning again must NOT re-fire the
    // reveal notification. Mirrors RoomTypeUnlockAnnouncer.SeedAlreadyUnlocked's exact pattern for the
    // identical class of bug (see its own comment) — deliberately NOT solved by adding typesEverSpawned
    // to SaveData directly, since BunnyTypeUnlockTracker.unlockedBunnyTypes already IS the correctly-
    // persisted source of truth this can re-derive from; a second parallel saved set would just be the
    // same state duplicated.
    //
    // Public — also called explicitly by SaveManager.LoadGame() right after it restores the real
    // BunnyTypeUnlockTracker state, for the same Start()-ordering reason RoomTypeUnlockAnnouncer's own
    // version documents: Unity gives no ordering guarantee between two different components' Start()
    // methods, so if THIS Start() ran before SaveManager's own Start() restored unlocks, the seed below
    // would under-seed against a still-empty unlock set. Calling this twice is harmless — HashSet.Add on
    // an already-present entry is a no-op.
    public void SeedAlreadyRevealedTypes()
    {
        if (bunnyTypes == null || BunnyTypeUnlockTracker.Instance == null) return;

        foreach (BunnyTypeDefinition def in bunnyTypes)
        {
            if (def != null && BunnyTypeUnlockTracker.Instance.IsUnlocked(def))
                typesEverSpawned.Add(def.type);
        }
    }

    // Types that are both population-unlocked and have art (prefab != null) — types beyond Group 1 exist
    // as data (base stats, thresholds) ahead of their art, per the design doc, and are simply excluded
    // here until a prefab is assigned. Fails closed (treats as locked) if BunnyTypeUnlockTracker isn't in
    // the scene, matching RoomUnlockCondition's same fail-closed handling of a missing PopulationManager.
    private List<BunnyTypeDefinition> GetAvailableTypes()
    {
        List<BunnyTypeDefinition> available = new List<BunnyTypeDefinition>();
        if (bunnyTypes == null || BunnyTypeUnlockTracker.Instance == null) return available;

        foreach (BunnyTypeDefinition def in bunnyTypes)
        {
            if (def == null || def.prefab == null) continue;
            if (!BunnyTypeUnlockTracker.Instance.IsUnlocked(def)) continue;
            available.Add(def);
        }
        return available;
    }

    // Guaranteed-debut mechanic (see BunnyDebutSpawn_DesignDoc.md) — whichever available type has never
    // actually spawned yet (population crossed its threshold, but no bunny of that type has arrived this
    // session) is guaranteed to be the very next wild arrival, replacing the old approach of hand-placing
    // a reveal at a fixed position in startingBunnyOrder (which only worked by coincidence when that
    // type's threshold happened to be 0). If multiple thresholds were crossed since the last spawn (e.g.
    // a population jump from breeding), only the LOWEST-threshold pending type debuts now — the next
    // becomes the new pending debut and wins the following spawn, cascading one guaranteed debut per
    // spawn cycle rather than bursting all at once. Returns null when nothing is pending, so callers fall
    // through to the normal weighted pick.
    private BunnyTypeDefinition GetPendingDebutType(List<BunnyTypeDefinition> availableTypes)
    {
        BunnyTypeDefinition pending = null;
        foreach (BunnyTypeDefinition def in availableTypes)
        {
            if (typesEverSpawned.Contains(def.type)) continue;
            if (pending == null || def.populationThreshold < pending.populationThreshold)
                pending = def;
        }
        return pending;
    }

    // Weighted fallback used whenever no debut is pending (see BunnyDebutSpawn_DesignDoc.md). Neutral
    // always carries weight 4; every other currently-available type carries weight 1 each, so the ratio
    // self-adjusts as more types unlock (4/7 at the start, 4/8 once one more type unlocks, 4/9 after
    // that, and so on) with no hardcoded weight table to maintain.
    private const int NeutralSpawnWeight = 4;
    private const int OtherTypeSpawnWeight = 1;

    private BunnyTypeDefinition PickWeightedRandomType(List<BunnyTypeDefinition> availableTypes)
    {
        int totalWeight = 0;
        foreach (BunnyTypeDefinition def in availableTypes)
            totalWeight += def.type == BunnyType.Neutral ? NeutralSpawnWeight : OtherTypeSpawnWeight;

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        foreach (BunnyTypeDefinition def in availableTypes)
        {
            cumulative += def.type == BunnyType.Neutral ? NeutralSpawnWeight : OtherTypeSpawnWeight;
            if (roll < cumulative) return def;
        }
        return availableTypes[availableTypes.Count - 1]; // defensive fallback, should never be reached
    }

    [ContextMenu("Spawn Wild Bunny")]
    public void SpawnWildBunny()
    {
        List<BunnyTypeDefinition> availableTypes = GetAvailableTypes();
        if (availableTypes.Count == 0)
        {
            Debug.LogWarning("WildBunnySpawner: no bunny types are both unlocked and have a prefab assigned (or BunnyTypeUnlockTracker isn't in the scene).");
            return;
        }

        BunnyTypeDefinition chosenType = GetPendingDebutType(availableTypes) ?? PickWeightedRandomType(availableTypes);
        SpawnBunnyOfType(chosenType);
    }

    // Shared by the random pick above (AutoSpawnLoop, context-menu) and the explicit
    // startingBunnyOrder entries (SpawnStartingBunnies) — the latter bypass GetAvailableTypes'
    // unlock/prefab filtering since they're an explicit designer-authored sequence, not a random draw.
    private void SpawnBunnyOfType(BunnyTypeDefinition chosenType)
    {
        // Covers every call path uniformly (AutoSpawnLoop, SpawnStartingBunnies, and the context-menu
        // trigger) with a single guard, rather than checking capacity at each call site separately.
        if (PopulationManager.Instance != null && !PopulationManager.Instance.HasRoomForNewResident)
        {
            DebugLog.Log("WildBunnySpawner: population cap reached — skipping spawn.");
            return;
        }

        if (chosenType == null || chosenType.prefab == null)
        {
            Debug.LogWarning("WildBunnySpawner: tried to spawn a null bunny type, or one with no prefab assigned.");
            return;
        }

        Transform baseEntrance = BaseLayoutManager.Instance.BaseEntrance;
        if (baseEntrance == null)
        {
            Debug.LogWarning("WildBunnySpawner: BaseLayoutManager has no BaseEntrance assigned.");
            return;
        }

        Vector3 spawnPosition = baseEntrance.position + new Vector3(offscreenSpawnOffsetX, 0f, 0f);
        GameObject spawnedObject = Instantiate(chosenType.prefab, spawnPosition, baseEntrance.rotation);
        NPCBunny newBunny = spawnedObject.GetComponent<NPCBunny>();
        if (newBunny == null)
        {
            Debug.LogWarning($"WildBunnySpawner: {chosenType.type}'s prefab has no NPCBunny component.");
            Destroy(spawnedObject);
            return;
        }

        newBunny.SetArrivalType(BunnyArrivalType.Wild);

        BunnyGender gender = WildBunnyNames.Instance.GetRandomGender();
        string chosenName = WildBunnyNames.Instance.GetRandomName(chosenType.type, gender);
        newBunny.SetIdentity(gender, chosenName);

        int level = RollSpawnLevel();
        newBunny.RollIndividuality();
        BunnyStats stats = BunnyStatCalculator.Resolve(chosenType, level,
            newBunny.IVHP, newBunny.IVAttack, newBunny.IVDefense, newBunny.IVSpeed, newBunny.IVLuck,
            newBunny.EVHP, newBunny.EVAttack, newBunny.EVDefense, newBunny.EVSpeed, newBunny.EVLuck);
        // Wild spawns are always adults -> 2 traits. A future egg/breeding system would roll 1 for a
        // newly-hatched kid instead — see the design doc's Open Items.
        List<BunnyTraitDefinition> traits = BunnyTraitCatalog.Instance != null
            ? BunnyTraitCatalog.Instance.RollTraits(2)
            : new List<BunnyTraitDefinition>();
        List<BunnyPassiveDefinition> passives = BunnyPassiveResolver.ResolvePassives(chosenType, level);
        newBunny.SetTypeAndProgression(chosenType, level, stats, traits, passives);

        newBunny.RandomizeStartingNeeds(minStartingNeed, maxStartingNeed);

        Transform queueSpot = GateQueueManager.Instance.Enqueue(newBunny);
        if (queueSpot == null)
        {
            Debug.LogWarning("WildBunnySpawner: gate queue is full — bunny spawned but not queued.");
            return;
        }

        newBunny.MoveToQueueSpot(queueSpot);
        DebugLog.Log($"Spawned wild {chosenType.type} bunny {newBunny.name} (Level {level}) and sent it to the queue.");

        NotificationManager.Instance?.Show(NotificationType.WildBunnyArrived, newBunny.BunnyName, level);

        // First-ever spawn of a type outside the base 4 (e.g. Fire) — a "new type" reveal moment.
        // NotificationManager plays the reveal's own SFX (see its NotificationDefinition) — no separate
        // AudioManager.PlayNewBunnyTypeRevealed() call needed here anymore.
        if (typesEverSpawned.Add(chosenType.type) && !baseStartingTypes.Contains(chosenType.type))
        {
            NotificationManager.Instance?.ShowWithIcon(NotificationType.NewBunnyType, chosenType.icon, chosenType.displayName);
        }
    }
}