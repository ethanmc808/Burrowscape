using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class WildBunnySpawner : MonoBehaviour
{
    [Tooltip("All BunnyTypeDefinition assets this spawner can choose from. Filtered at spawn time to those with a prefab assigned AND currently unlocked (see BunnyTypeUnlockTracker) — types without art simply never get picked, no code change needed once art ships.")]
    [SerializeField] private List<BunnyTypeDefinition> bunnyTypes;
    [SerializeField] private bool spawnOnStart = false;
    [SerializeField] private float offscreenSpawnOffsetX = 15f; // positive X = further left (visually) in this project

    [Header("Starting Population")]
    [Tooltip("Exact ordered list of the opening bunnies, spawned in Start() via the same path as a normal wild arrival. Any entry whose type is NOT one of the base 4 (Neutral/Water/Plant/Shock) is treated as a 'new type reveal' (e.g. Fire) — it's held back with a real wait (see startMinWaitMinutes/startMaxWaitMinutes below) instead of the tight starting spacing, and triggers NewBunnyTypeNotification the first time it spawns.")]
    [SerializeField] private List<BunnyTypeDefinition> startingBunnyOrder;
    [SerializeField] private float startingSpawnDelaySeconds = 0.3f; // gap between each starting bunny so they spawn in a visible line instead of a cluster

    // The 4 types every playthrough starts with (BunnyTypeSystem_DesignDoc.md Group 1 minus Fire) —
    // spawns of these never trigger NewBunnyTypeNotification, since they're not a "new type" reveal.
    private static readonly HashSet<BunnyType> baseStartingTypes = new HashSet<BunnyType>
    {
        BunnyType.Neutral, BunnyType.Water, BunnyType.Plant, BunnyType.Shock
    };

    // Every type that has ever spawned this session — first-time entries outside baseStartingTypes
    // fire NewBunnyTypeNotification (see SpawnBunnyOfType).
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
        if (spawnOnStart)
        {
            SpawnWildBunny();
        }

        if (startingBunnyOrder != null && startingBunnyOrder.Count > 0)
        {
            // AutoSpawnLoop is deliberately NOT started here — it waits until the starting sequence
            // (including the held-back Fire reveal) fully finishes. Both use the same
            // startMinWaitMinutes/startMaxWaitMinutes range, so running them concurrently let
            // AutoSpawnLoop's random wait occasionally finish first and steal Fire's intended slot.
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

    private IEnumerator SpawnStartingBunnies()
    {
        foreach (BunnyTypeDefinition entry in startingBunnyOrder)
        {
            if (entry != null && !baseStartingTypes.Contains(entry.type))
            {
                // New-type reveal (e.g. Fire) — held back with a real wait instead of the tight
                // starting-cluster spacing, so it reads as its own arrival rather than part of the opening rush.
                float waitMinutes = Random.Range(startMinWaitMinutes, startMaxWaitMinutes);
                yield return new WaitForSeconds(waitMinutes * 60f);
            }

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

    // Equal-weight among types that are both population-unlocked and have art (prefab != null) — types
    // beyond Group 1 exist as data (base stats, thresholds) ahead of their art, per the design doc, and
    // are simply excluded here until a prefab is assigned. Fails closed (treats as locked) if
    // BunnyTypeUnlockTracker isn't in the scene, matching RoomUnlockCondition's same fail-closed
    // handling of a missing PopulationManager.
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

    [ContextMenu("Spawn Wild Bunny")]
    public void SpawnWildBunny()
    {
        List<BunnyTypeDefinition> availableTypes = GetAvailableTypes();
        if (availableTypes.Count == 0)
        {
            Debug.LogWarning("WildBunnySpawner: no bunny types are both unlocked and have a prefab assigned (or BunnyTypeUnlockTracker isn't in the scene).");
            return;
        }

        BunnyTypeDefinition chosenType = availableTypes[Random.Range(0, availableTypes.Count)];
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

        // First-ever spawn of a type outside the base 4 (e.g. Fire) — a "new type" reveal moment.
        if (typesEverSpawned.Add(chosenType.type) && !baseStartingTypes.Contains(chosenType.type))
        {
            NewBunnyTypeNotification.Instance?.Show(chosenType);
            AudioManager.EnsureInstance().PlayNewBunnyTypeRevealed();
        }
    }
}