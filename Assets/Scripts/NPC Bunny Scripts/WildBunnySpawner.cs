using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class WildBunnySpawner : MonoBehaviour
{
    [SerializeField] private List<NPCBunny> bunnyPrefabs;
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

    private void GetSpawnIntervalRangeSeconds(out float minSeconds, out float maxSeconds)
    {
        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;

        float denominator = capPopulation - startPopulation;
        float t = denominator > 0f
            ? Mathf.Clamp01((population - startPopulation) / denominator)
            : (population >= capPopulation ? 1f : 0f);

        minSeconds = Mathf.Lerp(startMinWaitMinutes, capMinWaitMinutes, t) * 60f;
        maxSeconds = Mathf.Lerp(startMaxWaitMinutes, capMaxWaitMinutes, t) * 60f;
    }

    [ContextMenu("Spawn Wild Bunny")]
    public void SpawnWildBunny()
    {
        if (bunnyPrefabs == null || bunnyPrefabs.Count == 0)
        {
            Debug.LogWarning("WildBunnySpawner: no bunny prefabs assigned.");
            return;
        }

        Transform baseEntrance = BaseLayoutManager.Instance.BaseEntrance;
        if (baseEntrance == null)
        {
            Debug.LogWarning("WildBunnySpawner: BaseLayoutManager has no BaseEntrance assigned.");
            return;
        }

        NPCBunny chosenPrefab = bunnyPrefabs[Random.Range(0, bunnyPrefabs.Count)];
        if (chosenPrefab == null)
        {
            Debug.LogWarning("WildBunnySpawner: selected prefab slot is empty.");
            return;
        }

        Vector3 spawnPosition = baseEntrance.position + new Vector3(offscreenSpawnOffsetX, 0f, 0f);
        NPCBunny newBunny = Instantiate(chosenPrefab, spawnPosition, baseEntrance.rotation);
        newBunny.SetArrivalType(BunnyArrivalType.Wild);

        BunnyGender gender = WildBunnyNames.Instance.GetRandomGender();
        string chosenName = WildBunnyNames.Instance.GetRandomName(gender);
        newBunny.SetIdentity(gender, chosenName);
        newBunny.RandomizeStartingNeeds(minStartingNeed, maxStartingNeed);

        Transform queueSpot = GateQueueManager.Instance.Enqueue(newBunny);
        if (queueSpot == null)
        {
            Debug.LogWarning("WildBunnySpawner: gate queue is full — bunny spawned but not queued.");
            return;
        }

        newBunny.MoveToQueueSpot(queueSpot);
        DebugLog.Log($"Spawned wild bunny {newBunny.name} and sent it to the queue.");
    }
}