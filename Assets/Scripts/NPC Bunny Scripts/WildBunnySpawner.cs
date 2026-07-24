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
    [SerializeField] private int startingWildBunnyCount = 8; // spawned immediately in Start(), same path as a normal wild arrival
    [SerializeField] private float startingSpawnDelaySeconds = 0.3f; // gap between each starting bunny so they spawn in a visible line instead of a cluster

    [Header("Timed Auto-Spawning")]
    [SerializeField] private bool autoSpawnEnabled = true;

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

        if (startingWildBunnyCount > 0)
        {
            StartCoroutine(SpawnStartingBunnies());
        }

        if (autoSpawnEnabled)
        {
            autoSpawnRoutine = StartCoroutine(AutoSpawnLoop());
        }
    }

    private IEnumerator SpawnStartingBunnies()
    {
        for (int i = 0; i < startingWildBunnyCount; i++)
        {
            SpawnWildBunny();
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

        Transform baseEntrance = BaseLayoutManager.Instance.BaseEntrance;
        if (baseEntrance == null)
        {
            Debug.LogWarning("WildBunnySpawner: BaseLayoutManager has no BaseEntrance assigned.");
            return;
        }

        BunnyTypeDefinition chosenType = availableTypes[Random.Range(0, availableTypes.Count)];

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
        string chosenName = WildBunnyNames.Instance.GetRandomName(gender);
        newBunny.SetIdentity(gender, chosenName);

        int level = RollSpawnLevel();
        BunnyStats stats = BunnyStatCalculator.Resolve(chosenType, level, newBunny.StatGrowthRate);
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
    }
}