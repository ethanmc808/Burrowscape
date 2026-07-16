using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class WildBunnySpawner : MonoBehaviour
{
    [SerializeField] private List<NPCBunny> bunnyPrefabs;
    [SerializeField] private bool spawnOnStart = false;
    [SerializeField] private float offscreenSpawnOffsetX = 15f; // positive X = further left (visually) in this project

    [Header("Timed Auto-Spawning")]
    [SerializeField] private bool autoSpawnEnabled = true;
    [SerializeField] private float minSpawnInterval = 600f; // seconds (10 min)
    [SerializeField] private float maxSpawnInterval = 1200f; // seconds (20 min)

    private Coroutine autoSpawnRoutine;

    private void Start()
    {
        if (spawnOnStart)
        {
            SpawnWildBunny();
        }

        if (autoSpawnEnabled)
        {
            autoSpawnRoutine = StartCoroutine(AutoSpawnLoop());
        }
    }

    private IEnumerator AutoSpawnLoop()
    {
        while (true)
        {
            float wait = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(wait);
            SpawnWildBunny();
        }
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