using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Decides when/where base invasions happen — the piece Combat_DesignDoc.md flagged as fully unbuilt.
// Structural mirror of WildBunnySpawner: a population-ramped random interval (CombatBalanceConfig.
// GetInvasionWaitRangeSeconds/RollInvasionGroupSize), spawning enemies straight into a random room's
// EnemySpots (no gate walk-in for now — a documented future addition, not built here).
//
// Also drives the room-defense auto-interrupt (Combat_DesignDoc.md's Guard Room section, revised): any
// bunny already Working/Relaxing in the targeted room gets routed to a CombatSpot via NPCBunny.
// BeginDefending. Positioning only — actual targeting/firing is CombatEngagement's job, ticked by
// NPCBunny (while Defending) and EnemyInstance (via GetEnemiesInRoom below) independently.
public class InvasionManager : MonoBehaviour
{
    public static InvasionManager Instance { get; private set; }

    [Tooltip("Pest enemy types this invader can draw from. Filtered at spawn time to those with a prefab assigned — types without art are simply excluded, same pattern as WildBunnySpawner.GetAvailableTypes.")]
    [SerializeField] private List<EnemyDefinition> enemyTypes;
    [SerializeField] private bool autoSpawnEnabled = true;

    private readonly Dictionary<RoomBase, List<EnemyInstance>> activeInvasions = new Dictionary<RoomBase, List<EnemyInstance>>();

    public event Action<RoomBase, List<EnemyInstance>> OnEnemyGroupSpawned;
    public event Action<RoomBase> OnInvasionCleared;

    private Coroutine invasionRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (autoSpawnEnabled)
            invasionRoutine = StartCoroutine(InvasionLoop());
    }

    private IEnumerator InvasionLoop()
    {
        while (true)
        {
            CombatBalanceConfig.Instance.GetInvasionWaitRangeSeconds(out float minSeconds, out float maxSeconds);
            float wait = UnityEngine.Random.Range(minSeconds, maxSeconds);
            yield return new WaitForSeconds(wait);

            TrySpawnInvasion();
        }
    }

    public bool IsRoomInvaded(RoomBase room)
    {
        return activeInvasions.ContainsKey(room);
    }

    // Query used by EnemyInstance's own combat tick (see CombatEngagement) — every currently-active enemy
    // in `room`, so an enemy only ever considers targets that share its actual room, not the whole base.
    public IReadOnlyList<EnemyInstance> GetEnemiesInRoom(RoomBase room)
    {
        return activeInvasions.TryGetValue(room, out List<EnemyInstance> group) ? group : Array.Empty<EnemyInstance>();
    }

    [ContextMenu("Force Invasion")]
    public void TrySpawnInvasion()
    {
        List<EnemyDefinition> availableTypes = enemyTypes != null
            ? enemyTypes.Where(d => d != null && d.prefab != null).ToList()
            : new List<EnemyDefinition>();

        if (availableTypes.Count == 0)
        {
            DebugLog.Log("InvasionManager: no enemy types have a prefab assigned — skipping invasion.");
            return;
        }

        RoomBase targetRoom = PickTargetRoom();
        if (targetRoom == null)
        {
            DebugLog.Log("InvasionManager: no room currently has a free EnemySpot — skipping invasion.");
            return;
        }

        int unoccupiedSpots = targetRoom.EnemySpots.Count(s => !s.IsOccupied);
        int groupSize = Mathf.Min(CombatBalanceConfig.Instance.RollInvasionGroupSize(), unoccupiedSpots);

        List<EnemyInstance> spawnedGroup = new List<EnemyInstance>();

        for (int i = 0; i < groupSize; i++)
        {
            EnemyDefinition chosenType = availableTypes[UnityEngine.Random.Range(0, availableTypes.Count)];
            RoomSpot spot = targetRoom.EnemySpots.FirstOrDefault(s => !s.IsOccupied);
            if (spot == null) break;

            GameObject spawnedObject = Instantiate(chosenType.prefab, spot.transform.position, spot.transform.rotation);
            EnemyInstance enemy = spawnedObject.GetComponent<EnemyInstance>();
            if (enemy == null)
            {
                Debug.LogWarning($"InvasionManager: {chosenType.displayName}'s prefab has no EnemyInstance component.");
                Destroy(spawnedObject);
                continue;
            }

            spot.TryClaim(enemy);
            enemy.Initialize(chosenType, targetRoom);

            RoomBase room = targetRoom;
            RoomSpot claimedSpot = spot;
            enemy.OnDefeated += () => HandleEnemyDefeated(room, enemy, claimedSpot);

            spawnedGroup.Add(enemy);
        }

        if (spawnedGroup.Count == 0) return;

        activeInvasions[targetRoom] = spawnedGroup;
        DebugLog.Log($"InvasionManager: spawned {spawnedGroup.Count} enemy(ies) into {targetRoom.name}.");

        TriggerAutoDefend(targetRoom);

        OnEnemyGroupSpawned?.Invoke(targetRoom, spawnedGroup);
    }

    private RoomBase PickTargetRoom()
    {
        List<RoomBase> candidates = new List<RoomBase>();
        foreach (int floor in BaseLayoutManager.Instance.GetAllFloorIndices())
        {
            foreach (RoomBase room in BaseLayoutManager.Instance.GetAllRoomsOnFloor(floor))
            {
                if (room.EnemySpots != null && room.EnemySpots.Any(s => !s.IsOccupied))
                    candidates.Add(room);
            }
        }

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // Any bunny already Working/Relaxing in the invaded room breaks off to a CombatSpot without losing
    // its job/relax claim (see NPCBunny.BeginDefending). More residents than CombatSpots is an accepted
    // edge case — the remainder just keep doing what they were doing, same tolerance the design doc
    // already gives flanking-slot races.
    private void TriggerAutoDefend(RoomBase room)
    {
        if (DwellerRoster.Instance == null) return;

        foreach (NPCBunny bunny in DwellerRoster.Instance.GetBunniesCurrentlyInRoom(room))
        {
            RoomSpot spot = room.ClaimCombatSpot(bunny);
            Debug.Log($"[VFXDEBUG] TriggerAutoDefend({room.name}): {bunny.name} ClaimCombatSpot -> {(spot != null ? spot.name : "NULL")}");
            if (spot == null) break; // room's CombatSpots are full — remainder stay put

            bunny.BeginDefending(spot, room);
        }
    }

    private void HandleEnemyDefeated(RoomBase room, EnemyInstance enemy, RoomSpot spot)
    {
        // Gameplay bookkeeping (spot release, invasion tracking) happens immediately — EnemyInstance
        // itself handles the delayed self-destruct so its Dying animation has time to play out.
        spot.Release(enemy);

        if (!activeInvasions.TryGetValue(room, out List<EnemyInstance> group)) return;

        group.Remove(enemy);
        if (group.Count > 0) return;

        activeInvasions.Remove(room);

        // Recall every defender (auto-defenders and any deployed guards) — ReturnToPreviousActivity
        // (inside StopDefendingAndReturn) already knows how to walk each one back to whatever it was
        // doing before, no special-case needed here.
        if (DwellerRoster.Instance != null)
        {
            foreach (NPCBunny bunny in DwellerRoster.Instance.GetBunniesDefendingRoom(room))
                bunny.StopDefendingAndReturn();
        }

        OnInvasionCleared?.Invoke(room);
    }
}
