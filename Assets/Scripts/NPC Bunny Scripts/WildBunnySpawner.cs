using UnityEngine;

public class WildBunnySpawner : MonoBehaviour
{
    [SerializeField] private NPCBunny bunnyPrefab;
    [SerializeField] private bool spawnOnStart = false;
    [SerializeField] private float offscreenSpawnOffsetX = 15f; // positive X = further left (visually) in this project

    private void Start()
    {
        if (spawnOnStart)
        {
            SpawnWildBunny();
        }
    }

    [ContextMenu("Spawn Wild Bunny")]
    public void SpawnWildBunny()
    {
        if (bunnyPrefab == null)
        {
            Debug.LogWarning("WildBunnySpawner: no bunny prefab assigned.");
            return;
        }

        Transform baseEntrance = BaseLayoutManager.Instance.BaseEntrance;
        if (baseEntrance == null)
        {
            Debug.LogWarning("WildBunnySpawner: BaseLayoutManager has no BaseEntrance assigned.");
            return;
        }

        Vector3 spawnPosition = baseEntrance.position + new Vector3(offscreenSpawnOffsetX, 0f, 0f);
        NPCBunny newBunny = Instantiate(bunnyPrefab, spawnPosition, baseEntrance.rotation);
        newBunny.SetArrivalType(BunnyArrivalType.Wild);

        Transform queueSpot = GateQueueManager.Instance.Enqueue(newBunny);
        if (queueSpot == null)
        {
            Debug.LogWarning("WildBunnySpawner: gate queue is full — bunny spawned but not queued.");
            return;
        }

        newBunny.MoveToQueueSpot(queueSpot);
        Debug.Log($"Spawned wild bunny {newBunny.name} and sent it to the queue.");
    }
}