using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Decides when/where base invasions happen — a population-ramped random interval (CombatBalanceConfig.
// GetInvasionWaitRangeSeconds/RollInvasionGroupSize). Every raid now has two phases (see
// Combat_DesignDoc.md's Gate Siege section): enemies spawn onto EntranceGate's outside siege spots and
// besiege the gate (TrySpawnInvasion), and only once it breaks does HandleGateBreached walk the SAME
// surviving enemies into whichever room PickTargetRoom's chokepoint logic picks, claiming that room's
// EnemySpots.
//
// Also drives the room-defense auto-interrupt (Combat_DesignDoc.md's Guard Room section, revised): any
// bunny already Working/Relaxing in the targeted room gets routed to a CombatSpot via NPCBunny.
// BeginDefending — only once enemies actually reach the interior room, not during the gate-siege phase
// (bunnies can't defend the gate itself). Positioning only — actual targeting/firing is CombatEngagement's
// job, ticked by NPCBunny (while Defending) and EnemyInstance (via GetEnemiesInRoom below) independently.
public class InvasionManager : MonoBehaviour
{
    public static InvasionManager Instance { get; private set; }

    [Tooltip("Pest enemy types this invader can draw from. Filtered at spawn time to those with a prefab assigned — types without art are simply excluded, same pattern as WildBunnySpawner.GetAvailableTypes.")]
    [SerializeField] private List<EnemyDefinition> enemyTypes;
    [SerializeField] private bool autoSpawnEnabled = true;

    private readonly Dictionary<RoomBase, List<EnemyInstance>> activeInvasions = new Dictionary<RoomBase, List<EnemyInstance>>();

    // Gate-siege phase bookkeeping — not keyed by RoomBase like activeInvasions above, since siege
    // enemies aren't in any room yet (see Combat_DesignDoc.md's Gate Siege section). Every enemy here
    // survives to breach by definition (siege enemies are invulnerable — bunnies can't defend the gate,
    // and the gate never attacks back), so unlike activeInvasions this never needs per-enemy death
    // handling while a siege is ongoing.
    private readonly List<EnemyInstance> gateSiegeGroup = new List<EnemyInstance>();
    private bool gateBreachSubscribed;

    public event Action<RoomBase, List<EnemyInstance>> OnEnemyGroupSpawned;
    public event Action<RoomBase> OnInvasionCleared;
    // Fires once the ENTIRE raid — gate siege plus whatever made it inside — is fully resolved, distinct
    // from OnInvasionCleared (which fires per interior room). EntranceGate subscribes to this, not
    // OnInvasionCleared, for its full-HP repair.
    public event Action OnSiegeFullyCleared;

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

    // Entry point for every raid — spawns onto EntranceGate's outside siege spots rather than a room's
    // EnemySpots. Enemies fight the gate only (see EnemyInstance.HandleSiegeUpdate); once it breaks,
    // HandleGateBreached takes the SAME surviving enemies inward via the unchanged PickTargetRoom logic.
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

        EntranceGate gate = EntranceGate.Instance;
        if (gate == null)
        {
            DebugLog.Log("InvasionManager: no EntranceGate in scene — skipping invasion.");
            return;
        }

        List<RoomSpot> rangedSpots = gate.RangedSiegeSpots != null
            ? gate.RangedSiegeSpots.Where(s => !s.IsOccupied).ToList()
            : new List<RoomSpot>();
        RoomSpot meleeSpot = gate.MeleeSiegeSpot != null && !gate.MeleeSiegeSpot.IsOccupied ? gate.MeleeSiegeSpot : null;

        int totalAvailable = rangedSpots.Count + (meleeSpot != null ? 1 : 0);
        if (totalAvailable == 0)
        {
            DebugLog.Log("InvasionManager: gate siege spots are all occupied — skipping invasion.");
            return;
        }

        int groupSize = Mathf.Min(CombatBalanceConfig.Instance.RollInvasionGroupSize(), totalAvailable);
        List<EnemyInstance> spawnedGroup = new List<EnemyInstance>();
        int rangedUsed = 0;

        for (int i = 0; i < groupSize; i++)
        {
            EnemyDefinition chosenType = PickTypeWithAvailableSpot(availableTypes, rangedSpots.Count - rangedUsed, meleeSpot != null);
            if (chosenType == null) break; // both pools this wave could still roll from are exhausted

            bool isMelee = chosenType.attackSource != null && chosenType.attackSource.isMelee;
            RoomSpot spot = isMelee ? meleeSpot : rangedSpots[rangedUsed];

            GameObject spawnedObject = Instantiate(chosenType.prefab, spot.transform.position, spot.transform.rotation);
            EnemyInstance enemy = spawnedObject.GetComponent<EnemyInstance>();
            if (enemy == null)
            {
                Debug.LogWarning($"InvasionManager: {chosenType.displayName}'s prefab has no EnemyInstance component.");
                Destroy(spawnedObject);
                continue;
            }

            spot.TryClaim(enemy);
            enemy.InitializeForSiege(chosenType, spot);

            if (isMelee) meleeSpot = null; else rangedUsed++;
            spawnedGroup.Add(enemy);
        }

        if (spawnedGroup.Count == 0) return;

        // Merge into an ongoing siege rather than replacing it — same reasoning as room invasions already
        // merging (see HandleGateBreached's own interior-room merge below): a second wave while the gate
        // still stands just reinforces the existing siege, bounded by remaining free spots.
        gateSiegeGroup.AddRange(spawnedGroup);
        DebugLog.Log($"InvasionManager: spawned {spawnedGroup.Count} enemy(ies) into the gate siege.");

        if (!gateBreachSubscribed)
        {
            gate.OnDefeated += HandleGateBreached;
            gateBreachSubscribed = true;
        }

        // OnEnemyGroupSpawned is NOT fired here — no room is known yet at siege-spawn time. It fires from
        // HandleGateBreached once survivors actually have a target room.
    }

    // Filters to types whose pool still has room BEFORE rolling, rather than rolling then rejecting —
    // avoids dead-looping when e.g. the melee pool (capacity 1) is already full but ranged still has
    // room, or vice versa. A type with no attackSource assigned is treated as ranged, matching
    // EnemyInstance.Update's own existing null-tolerant convention.
    private EnemyDefinition PickTypeWithAvailableSpot(List<EnemyDefinition> availableTypes, int rangedRemaining, bool meleeAvailable)
    {
        List<EnemyDefinition> eligible = availableTypes.Where(d =>
            (d.attackSource != null && d.attackSource.isMelee) ? meleeAvailable : rangedRemaining > 0
        ).ToList();

        return eligible.Count > 0 ? eligible[UnityEngine.Random.Range(0, eligible.Count)] : null;
    }

    // Called once EntranceGate.OnDefeated fires (HP hit 0). Every enemy in gateSiegeGroup survives to
    // this point by definition (siege enemies are invulnerable — see the field's own comment), so this
    // resolves ONE target room for the whole surviving group and walks each of them in.
    private void HandleGateBreached()
    {
        List<EnemyInstance> survivors = new List<EnemyInstance>(gateSiegeGroup);
        gateSiegeGroup.Clear();
        gateBreachSubscribed = false;

        RoomBase targetRoom = PickTargetRoom(); // unchanged chokepoint logic, resolved once for the group
        if (targetRoom == null)
        {
            DebugLog.Log("InvasionManager: gate breached but no interior room has a free EnemySpot — survivors stall outside.");
            // Known gap, not solved here: stalled survivors have nothing to walk toward and simply idle
            // forever near the broken gate (their HandleSiegeUpdate no-ops once the gate is dead). Rare —
            // needs every room's EnemySpots simultaneously full — revisit with a retry timer or a
            // capacity-ignoring force-place if it turns out to matter in practice.
            if (activeInvasions.Count == 0) OnSiegeFullyCleared?.Invoke(); // gate still repairs even if nobody got in
            return;
        }

        List<EnemyInstance> walkingGroup = new List<EnemyInstance>();
        foreach (EnemyInstance enemy in survivors)
        {
            RoomSpot claimedSpot = targetRoom.EnemySpots?.FirstOrDefault(s => !s.IsOccupied);
            if (claimedSpot == null) break; // room filled by earlier survivors in this same group

            claimedSpot.TryClaim(enemy);
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(
                null, null, null, targetRoom, claimedSpot, BaseLayoutManager.Instance.EntranceFloorIndex);

            RoomBase room = targetRoom;
            EnemyInstance walkingEnemy = enemy;
            RoomSpot spot = claimedSpot;
            enemy.OnDefeated += () => HandleEnemyDefeated(room, walkingEnemy, spot); // first OnDefeated subscription for this enemy — same pattern interior spawns already use

            enemy.BeginWalkToRoom(path, targetRoom, claimedSpot); // also releases this enemy's outside siege spot internally
            walkingGroup.Add(enemy);
        }

        if (walkingGroup.Count == 0) return;

        if (activeInvasions.TryGetValue(targetRoom, out List<EnemyInstance> existingGroup))
            existingGroup.AddRange(walkingGroup);
        else
            activeInvasions[targetRoom] = walkingGroup;

        TriggerAutoDefend(targetRoom); // fires once, here — not during the siege phase, since the gate has no defenders
        OnEnemyGroupSpawned?.Invoke(targetRoom, walkingGroup);
    }

    // Chokepoint targeting (Combat_DesignDoc.md's Guard Room section) — whatever room (of any type) sits
    // immediately next to the Entrance always absorbs the raid first, as long as it still has an open
    // EnemySpot. Purely positional, not Guard-Room-specific: placement is the strategic decision, not
    // what's built there. Falls back to today's random pick — now restricted to floor 1 only, since
    // raiders don't reach upper floors yet (lifts/multi-floor raider pathing explicitly deferred).
    private RoomBase PickTargetRoom()
    {
        RoomBase chokepoint = BaseLayoutManager.Instance.GetRoomAdjacentToEntrance();
        if (chokepoint != null && chokepoint.EnemySpots != null && chokepoint.EnemySpots.Any(s => !s.IsOccupied))
            return chokepoint;

        List<RoomBase> candidates = BaseLayoutManager.Instance.GetAllRoomsOnFloor(BaseLayoutManager.Instance.EntranceFloorIndex)
            .Where(r => r.EnemySpots != null && r.EnemySpots.Any(s => !s.IsOccupied))
            .ToList();

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

        // The whole raid (gate siege plus every interior room it fed into) is only "fully cleared" once
        // no rooms and no ongoing siege remain — EntranceGate listens for this specifically, not
        // OnInvasionCleared above, since that fires per-room and a raid can spread across more than one.
        if (activeInvasions.Count == 0 && gateSiegeGroup.Count == 0)
            OnSiegeFullyCleared?.Invoke();
    }
}
