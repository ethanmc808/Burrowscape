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
// job, ticked by NPCBunny (via GetEnemiesInRoom below, while Defending) and EnemyInstance (via
// DwellerRoster's own bunny query) independently.
public class InvasionManager : MonoBehaviour
{
    public static InvasionManager Instance { get; private set; }

    [Tooltip("Pest enemy types this invader can draw from. Filtered at spawn time to those with a prefab assigned — types without art are simply excluded, same pattern as WildBunnySpawner.GetAvailableTypes.")]
    [SerializeField] private List<EnemyDefinition> enemyTypes;
    [SerializeField] private bool autoSpawnEnabled = true;

    private readonly Dictionary<RoomBase, List<EnemyInstance>> activeInvasions = new Dictionary<RoomBase, List<EnemyInstance>>();

    // Set the moment RoomAbandonWatchRoutine finds NO room anywhere on a group's floor with any bunnies
    // in it — cleared the moment a target reappears anywhere, or the group actually relocates. Absolute
    // Time.time deadline (rolled once via CombatBalanceConfig.RollEnemyWanderWaitSeconds), not a countdown
    // float, so the poll interval doesn't need to tick it down itself.
    private readonly Dictionary<RoomBase, float> wanderDeadlineByRoom = new Dictionary<RoomBase, float>();

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

    // Only one invasion (siege + whatever it fed into interior rooms) is ever active at a time (Ethan's
    // explicit ask — more than one at once would be overwhelming early game, and other levers like the
    // level/group-size ramp should drive late-game difficulty instead of raw frequency). Set true the
    // moment TrySpawnInvasion actually spawns/reinforces a wave, cleared the moment OnSiegeFullyCleared
    // fires (both call sites below). InvasionLoop blocks on this before it'll even begin the next
    // cooldown-then-population-wait cycle.
    private bool raidActive;
    // False only until the very first invasion of the playthrough spawns — keeps invasionCooldownMinutes
    // from delaying game-start's first raid on top of its own already-tuned invasionStartMinWaitMinutes
    // wait; the fixed cooldown is meant to space raids apart from EACH OTHER, not from game start.
    private bool hasSpawnedFirstInvasion;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (autoSpawnEnabled)
            invasionRoutine = StartCoroutine(InvasionLoop());

        StartCoroutine(RoomAbandonWatchRoutine());
    }

    private IEnumerator InvasionLoop()
    {
        while (true)
        {
            // Never even start counting toward the next invasion until the current raid has fully
            // resolved — see raidActive's own comment.
            while (raidActive) yield return null;

            if (hasSpawnedFirstInvasion)
                yield return new WaitForSeconds(CombatBalanceConfig.Instance.invasionCooldownMinutes * 60f);

            CombatBalanceConfig.Instance.GetInvasionWaitRangeSeconds(out float minSeconds, out float maxSeconds);
            float wait = UnityEngine.Random.Range(minSeconds, maxSeconds);
            yield return new WaitForSeconds(wait);

            // Exactly one raid type per cycle, never both — same single-active-raid rule raidActive
            // already enforces between cycles, just applied to the choice within a cycle too.
            if (UnityEngine.Random.value < CombatBalanceConfig.Instance.gateSiegeInvasionChance)
                TrySpawnInvasion();
            else
                TrySpawnInRoomInvasion();
        }
    }

    public bool IsRoomInvaded(RoomBase room)
    {
        return activeInvasions.ContainsKey(room);
    }

    // Query used by NPCBunny's own combat tick (see CombatEngagement) — every enemy that's actually
    // arrived and standing in `room` right now, so a bunny only ever targets something it can really reach.
    // activeInvasions[room] itself gets populated the instant the gate breaks (HandleGateBreached), well
    // before survivors finish walking in from the gate — HasArrivedInRoom filters those still mid-walk
    // out, so a melee bunny can no longer flank a target that's still outside and compute a flank point
    // next to wherever it happened to be at that moment (confirmed bug, 2026-08-03: "attack empty air").
    public IReadOnlyList<EnemyInstance> GetEnemiesInRoom(RoomBase room)
    {
        if (!activeInvasions.TryGetValue(room, out List<EnemyInstance> group)) return Array.Empty<EnemyInstance>();
        return group.Where(e => e.HasArrivedInRoom).ToList();
    }

    // Periodically checks every room currently holding an enemy group for whether it still has anyone to
    // fight — if not, moves the whole group on (Ethan's explicit ask: nearest same-floor room with bunnies
    // present if one exists, otherwise wander to a random same-floor room after a tunable wait so the
    // player has time to cross-floor-deploy a guard). Same floor only for now, deliberately — raiders
    // don't use lifts yet (see Combat_DesignDoc.md's deferred multi-floor raider pathing; a future "rival
    // bunny gang" invader type is the one meant to eventually use lifts, cloned off NPCBunny's own
    // already-built lift-riding code, not this system).
    private IEnumerator RoomAbandonWatchRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(CombatBalanceConfig.Instance.enemyRoomAbandonCheckIntervalSeconds);

            foreach (RoomBase room in activeInvasions.Keys.ToList())
            {
                if (!activeInvasions.TryGetValue(room, out List<EnemyInstance> group)) continue; // relocated away already this pass
                group.RemoveAll(e => e == null);
                if (group.Count == 0) { activeInvasions.Remove(room); wanderDeadlineByRoom.Remove(room); continue; }

                // GetBunniesDefendingRoom(room) filtered to CurrentState != Fainted (not just
                // ==Defending) — DefendingRoom is set the instant BeginDefending is called, before the
                // walk to the CombatSpot even starts, so a bunny auto-defended a moment ago but still
                // mid-walk still counts as a real incoming target (this is what closes the race described
                // above ClearRoomInvasion). But DefendingRoom also stays set on a FAINTED bunny until the
                // invasion actually clears (StopDefendingAndReturn revives it) — counting Fainted here
                // would deadlock: the room never looks "abandoned" because its own defeated bunny still
                // reads as a target, so the enemies never leave, so the invasion never clears, so the
                // fainted bunny never gets recalled. CONFIRMED this exact deadlock in Play mode 2026-08-03
                // (Crimson fainted in Living Room, slimes never moved on despite bunnies in Garden Room).
                bool hasTarget = DwellerRoster.Instance != null &&
                    (DwellerRoster.Instance.GetBunniesDefendingRoom(room).Any(b => b.CurrentState != BunnyState.Fainted) ||
                     DwellerRoster.Instance.GetBunniesCurrentlyInRoom(room).Count > 0);
                if (hasTarget) { wanderDeadlineByRoom.Remove(room); continue; }

                // Only enemies that have actually finished walking in are eligible to be redirected —
                // one still mid-WalkingIn is already committed to a destination this frame, same
                // HasArrivedInRoom filter GetEnemiesInRoom uses for targeting eligibility.
                List<EnemyInstance> arrived = group.Where(e => e.HasArrivedInRoom).ToList();
                if (arrived.Count == 0) continue;

                RoomBase destination = PickNearestRoomWithBunnies(room);
                if (destination == null)
                {
                    if (!wanderDeadlineByRoom.TryGetValue(room, out float deadline))
                    {
                        deadline = Time.time + CombatBalanceConfig.Instance.RollEnemyWanderWaitSeconds();
                        wanderDeadlineByRoom[room] = deadline;
                    }
                    if (Time.time < deadline) continue;

                    destination = PickRandomRoomForWander(room);
                    if (destination == null) continue; // no other room even exists on this floor — stay put
                }

                wanderDeadlineByRoom.Remove(room);
                RelocateGroup(room, destination, arrived);
            }
        }
    }

    // Nearest same-floor room (by room-index distance, not raw world distance — same convention
    // BaseLayoutManager's own GridX-ordered lookups use elsewhere) with a free EnemySpot AND at least one
    // bunny currently in it. Null if no such room exists.
    private RoomBase PickNearestRoomWithBunnies(RoomBase fromRoom)
    {
        List<RoomBase> ordered = BaseLayoutManager.Instance.GetAllRoomsOnFloor(fromRoom.FloorIndex);
        int fromIndex = ordered.IndexOf(fromRoom);
        if (fromIndex == -1) return null;

        return ordered
            .Select((r, i) => (room: r, distance: Mathf.Abs(i - fromIndex)))
            .Where(x => x.room != fromRoom && x.distance > 0
                && x.room.EnemySpots != null && x.room.EnemySpots.Any(s => !s.IsOccupied)
                && DwellerRoster.Instance.GetBunniesCurrentlyInRoom(x.room).Count > 0)
            .OrderBy(x => x.distance)
            .Select(x => x.room)
            .FirstOrDefault();
    }

    // Wander fallback — any other same-floor room with a free EnemySpot, regardless of whether it
    // currently has bunnies (there are none anywhere on this floor by the time this is called, per
    // RoomAbandonWatchRoutine's own gating). Uniformly random, no distance preference.
    private RoomBase PickRandomRoomForWander(RoomBase excludeRoom)
    {
        List<RoomBase> candidates = BaseLayoutManager.Instance.GetAllRoomsOnFloor(excludeRoom.FloorIndex)
            .Where(r => r != excludeRoom && r.EnemySpots != null && r.EnemySpots.Any(s => !s.IsOccupied))
            .ToList();

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // Walks as many of `movingEnemies` as toRoom currently has free EnemySpots for (same "accepted edge
    // case" tolerance TriggerAutoDefend's own comment already uses for CombatSpots) — any leftover stay
    // behind in fromRoom's group and get retried next RoomAbandonWatchRoutine pass. Moves the whole group
    // together as a unit (Ethan's explicit ask), so fromRoom/toRoom's activeInvasions entries only ever
    // gain/lose whole batches together, not enemy-by-enemy.
    private void RelocateGroup(RoomBase fromRoom, RoomBase toRoom, List<EnemyInstance> movingEnemies)
    {
        List<RoomSpot> freeSpots = toRoom.EnemySpots.Where(s => !s.IsOccupied).ToList();
        int moveCount = Mathf.Min(movingEnemies.Count, freeSpots.Count);
        if (moveCount == 0) return; // toRoom filled up between selection and here — retried next pass

        List<EnemyInstance> moving = movingEnemies.Take(moveCount).ToList();
        for (int i = 0; i < moveCount; i++)
        {
            EnemyInstance enemy = moving[i];
            RoomSpot spot = freeSpots[i];
            RoomSpot fromSpot = enemy.ClaimedInteriorSpot;

            spot.TryClaim(enemy);
            List<Transform> path = BaseLayoutManager.Instance.GetRouteToSpot(fromRoom, fromSpot, null, toRoom, spot, fromRoom.FloorIndex);
            enemy.BeginWalkToRoom(path, toRoom, spot); // releases fromSpot and adopts toRoom/spot as current immediately (see that method's own comment)
        }

        if (activeInvasions.TryGetValue(fromRoom, out List<EnemyInstance> fromGroup))
        {
            fromGroup.RemoveAll(e => moving.Contains(e));
            // ClearRoomInvasion recalls any bunny still Defending/en-route-to-defend fromRoom — without
            // it, a defender that hadn't yet reached its CombatSpot when this relocation fired would be
            // stranded there forever with nothing left to fight (see that method's own comment).
            if (fromGroup.Count == 0) ClearRoomInvasion(fromRoom);
        }

        if (activeInvasions.TryGetValue(toRoom, out List<EnemyInstance> toGroup))
            toGroup.AddRange(moving);
        else
            activeInvasions[toRoom] = moving;

        // Without this, a bunny already idling in toRoom BEFORE the relocation never gets diverted to
        // Defending at all — TriggerAutoDefend is the only thing that catches an already-Working/Relaxing
        // bunny (OnArrivedAtWorkSpot/OnArrivedAtRelaxSpot's own race-checks only catch a bunny ARRIVING
        // after toRoom is already in activeInvasions, not one already standing there). Same "fires at the
        // decision moment, not on physical arrival" timing HandleGateBreached/TrySpawnInRoomInvasion
        // already use for their own spawns — bunnies get a head start walking to CombatSpots while the
        // enemies are still mid-walk-in themselves.
        TriggerAutoDefend(toRoom);
    }

    // Shared by both raid types' availableTypes filter — fails closed (treats as locked) if
    // EnemyTypeUnlockTracker isn't in the scene, same fail-closed convention WildBunnySpawner.
    // GetAvailableTypes already uses for a missing BunnyTypeUnlockTracker.
    private bool IsEnemyUnlocked(EnemyDefinition def)
    {
        return EnemyTypeUnlockTracker.Instance != null && EnemyTypeUnlockTracker.Instance.IsUnlocked(def);
    }

    // Debug/testing entry point — always a full 3-enemy group regardless of population pacing (Ethan's
    // ask: RollInvasionGroupSize() often returns just 1 at low population, which made manual testing
    // slower than necessary). The automatic InvasionLoop still calls TrySpawnInvasion() directly with no
    // override, so real gameplay pacing is completely unaffected.
    [ContextMenu("Force Invasion")]
    private void DebugForceInvasion() => TrySpawnInvasion(forcedGroupSize: 3);

    // Entry point for every raid — spawns onto EntranceGate's outside siege spots rather than a room's
    // EnemySpots. Enemies fight the gate only (see EnemyInstance.HandleSiegeUpdate); once it breaks,
    // HandleGateBreached takes the SAME surviving enemies inward via the unchanged PickTargetRoom logic.
    // forcedGroupSize: when set, skips CombatBalanceConfig.RollInvasionGroupSize() and uses this exact
    // count instead (still clamped to totalAvailable below) — see DebugForceInvasion above.
    public void TrySpawnInvasion(int? forcedGroupSize = null)
    {
        List<EnemyDefinition> availableTypes = enemyTypes != null
            ? enemyTypes.Where(d => d != null && d.prefab != null && IsEnemyUnlocked(d)).ToList()
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

        // A broken-but-not-yet-repaired gate can't be besieged again — HandleSiegeUpdate no-ops once
        // !gate.IsAlive, so a wave spawned here would just idle at its siege spot forever, never breaching
        // (TakeCombatDamage ignores further hits at 0 HP) and never walking in. That would permanently
        // strand this wave in gateSiegeGroup, which in turn permanently blocks OnSiegeFullyCleared (it
        // requires gateSiegeGroup.Count == 0) — i.e. the gate would never repair again for the rest of the
        // playthrough. Repair only happens once every interior room from the current raid has cleared
        // (HandleSiegeFullyCleared), so waiting for that is correct, not just a workaround.
        if (!((ICombatant)gate).IsAlive)
        {
            DebugLog.Log("InvasionManager: gate is already breached and awaiting repair — skipping invasion until the current raid fully resolves.");
            return;
        }

        List<RoomSpot> rangedSpots = gate.RangedSiegeSpots != null
            ? gate.RangedSiegeSpots.Where(s => !s.IsOccupied).ToList()
            : new List<RoomSpot>();

        // Front spot first, then waiting spots — so a solo melee enemy always gets the one actually
        // close enough to attack, and any extra melee picks this wave fall back to waiting (see
        // EnemyInstance.HandleSiegeUpdate, which only lets the meleeSiegeSpot occupant actually swing).
        List<RoomSpot> meleeSpots = new List<RoomSpot>();
        if (gate.MeleeSiegeSpot != null && !gate.MeleeSiegeSpot.IsOccupied) meleeSpots.Add(gate.MeleeSiegeSpot);
        if (gate.MeleeWaitingSpots != null) meleeSpots.AddRange(gate.MeleeWaitingSpots.Where(s => !s.IsOccupied));

        int totalAvailable = rangedSpots.Count + meleeSpots.Count;
        if (totalAvailable == 0)
        {
            DebugLog.Log("InvasionManager: gate siege spots are all occupied — skipping invasion.");
            return;
        }

        int groupSize = Mathf.Min(forcedGroupSize ?? CombatBalanceConfig.Instance.RollInvasionGroupSize(), totalAvailable);
        List<EnemyInstance> spawnedGroup = new List<EnemyInstance>();
        int rangedUsed = 0;
        int meleeUsed = 0;

        for (int i = 0; i < groupSize; i++)
        {
            EnemyDefinition chosenType = PickTypeWithAvailableSpot(availableTypes, rangedSpots.Count - rangedUsed, meleeSpots.Count - meleeUsed > 0);
            if (chosenType == null) break; // both pools this wave could still roll from are exhausted

            bool isMelee = chosenType.attackSource != null && chosenType.attackSource.isMelee;
            RoomSpot spot = isMelee ? meleeSpots[meleeUsed] : rangedSpots[rangedUsed];

            // Spawn offscreen (if EntranceGate.EnemySpawnPoints is configured) rather than already
            // standing at the siege spot — InitializeForSiege below then walks this enemy in from there.
            // Cycles through the list by how many enemies have actually spawned so far this wave (not the
            // loop index i, which could skip ahead on a bad-prefab continue below) — a multi-enemy wave
            // spreads across separate points instead of everyone stacking on one and clumping together.
            Transform spawnPoint = gate.EnemySpawnPoints != null && gate.EnemySpawnPoints.Count > 0
                ? gate.EnemySpawnPoints[spawnedGroup.Count % gate.EnemySpawnPoints.Count]
                : null;
            Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : spot.transform.position;
            Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : spot.transform.rotation;

            GameObject spawnedObject = Instantiate(chosenType.prefab, spawnPosition, spawnRotation);
            EnemyInstance enemy = spawnedObject.GetComponent<EnemyInstance>();
            if (enemy == null)
            {
                Debug.LogWarning($"InvasionManager: {chosenType.displayName}'s prefab has no EnemyInstance component.");
                Destroy(spawnedObject);
                continue;
            }

            spot.TryClaim(enemy);
            enemy.InitializeForSiege(chosenType, spot, spawnPoint);

            if (isMelee) meleeUsed++; else rangedUsed++;
            spawnedGroup.Add(enemy);
        }

        if (spawnedGroup.Count == 0) return;

        raidActive = true;
        hasSpawnedFirstInvasion = true;

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
    // avoids dead-looping when e.g. the melee pool (1 front spot + however many MeleeWaitingSpots exist)
    // is already full but ranged still has room, or vice versa. A type with no attackSource assigned is
    // treated as ranged, matching EnemyInstance.Update's own existing null-tolerant convention.
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
        // Deliberately NOT resetting gateBreachSubscribed here — this subscription is meant to be
        // permanent for the gate's lifetime, not re-armed per siege. It used to reset here, which meant
        // every subsequent TrySpawnInvasion call re-subscribed this same handler on TOP of the still-live
        // one from last cycle (never unsubscribed), so a later breach fired HandleGateBreached multiple
        // times in a row — harmless in the common case (gateSiegeGroup already cleared by the first call)
        // but an unbounded leak, and a real double-fire risk on OnSiegeFullyCleared in the rare
        // no-free-EnemySpot branch below.

        RoomBase targetRoom = PickTargetRoom(); // unchanged chokepoint logic, resolved once for the group
        if (targetRoom == null)
        {
            DebugLog.Log("InvasionManager: gate breached but no interior room has a free EnemySpot — survivors stall outside.");
            // Known gap, not solved here: stalled survivors have nothing to walk toward and simply idle
            // forever near the broken gate (their HandleSiegeUpdate no-ops once the gate is dead). Rare —
            // needs every room's EnemySpots simultaneously full — revisit with a retry timer or a
            // capacity-ignoring force-place if it turns out to matter in practice.
            if (activeInvasions.Count == 0)
            {
                raidActive = false;
                OnSiegeFullyCleared?.Invoke(); // gate still repairs even if nobody got in
            }
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

            // Reads enemy.CurrentRoom/ClaimedInteriorSpot fresh at invocation time rather than closing
            // over targetRoom/claimedSpot — this enemy may relocate to a different room entirely
            // (RoomAbandonWatchRoutine/RelocateGroup) long before it actually dies, and a captured local
            // here would report the wrong room/spot to HandleEnemyDefeated when that happens.
            EnemyInstance walkingEnemy = enemy;
            walkingEnemy.OnDefeated += () => HandleEnemyDefeated(walkingEnemy.CurrentRoom, walkingEnemy, walkingEnemy.ClaimedInteriorSpot);

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

        return PickRandomRoomWithFreeEnemySpot(restrictToEntranceFloor: true);
    }

    // Any room (any type) with at least one free EnemySpot, uniformly random — no chokepoint preference,
    // unlike PickTargetRoom's gate-siege fallback above ("pests burrow up from underground" has no
    // "breaks through the front line first" narrative the way a gate siege does, so every eligible room is
    // an equally likely target). restrictToEntranceFloor keeps PickTargetRoom's own floor-1-only rule
    // (raiders don't reach upper floors yet — lifts/multi-floor raider pathing explicitly deferred) without
    // imposing it on the in-room spawn type below, which never walks/paths anywhere at all and so has no
    // reason to be floor-limited — any floor's rooms are equally valid candidates for it.
    private RoomBase PickRandomRoomWithFreeEnemySpot(bool restrictToEntranceFloor)
    {
        List<RoomBase> pool = restrictToEntranceFloor
            ? BaseLayoutManager.Instance.GetAllRoomsOnFloor(BaseLayoutManager.Instance.EntranceFloorIndex)
            : BaseLayoutManager.Instance.GetAllRooms();

        List<RoomBase> candidates = pool
            .Where(r => r.EnemySpots != null && r.EnemySpots.Any(s => !s.IsOccupied))
            .ToList();

        if (candidates.Count == 0) return null;
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // Debug/testing entry point — always a full 3-enemy group, same reasoning as DebugForceInvasion above.
    [ContextMenu("Force In-Room Invasion")]
    private void DebugForceInRoomInvasion() => TrySpawnInRoomInvasion(forcedGroupSize: 3);

    // Second raid type, alongside the gate-siege pipeline above — enemies appear directly in a random
    // room's EnemySpots, bypassing EntranceGate/the siege phase entirely (no walk-in, no gate HP/Defense
    // involved at all). Shares every downstream mechanic with a gate-siege's interior arm: activeInvasions
    // bookkeeping, TriggerAutoDefend, OnEnemyGroupSpawned, and HandleEnemyDefeated's own clear/recall
    // logic — the only thing genuinely new here is spawn placement. Picking between this and
    // TrySpawnInvasion (never both at once) happens once per cycle in InvasionLoop.
    // forcedGroupSize: see TrySpawnInvasion's own comment — same override mechanism, same debug purpose.
    public void TrySpawnInRoomInvasion(int? forcedGroupSize = null)
    {
        List<EnemyDefinition> availableTypes = enemyTypes != null
            ? enemyTypes.Where(d => d != null && d.prefab != null && IsEnemyUnlocked(d)).ToList()
            : new List<EnemyDefinition>();

        if (availableTypes.Count == 0)
        {
            DebugLog.Log("InvasionManager: no enemy types have a prefab assigned — skipping in-room invasion.");
            return;
        }

        RoomBase targetRoom = PickRandomRoomWithFreeEnemySpot(restrictToEntranceFloor: false);
        if (targetRoom == null)
        {
            DebugLog.Log("InvasionManager: no interior room has a free EnemySpot — skipping in-room invasion.");
            return;
        }

        List<RoomSpot> freeSpots = targetRoom.EnemySpots.Where(s => !s.IsOccupied).ToList();
        int groupSize = Mathf.Min(forcedGroupSize ?? CombatBalanceConfig.Instance.RollInvasionGroupSize(), freeSpots.Count);
        List<EnemyInstance> spawnedGroup = new List<EnemyInstance>();

        for (int i = 0; i < groupSize; i++)
        {
            EnemyDefinition chosenType = availableTypes[UnityEngine.Random.Range(0, availableTypes.Count)];
            RoomSpot spot = freeSpots[i];

            GameObject spawnedObject = Instantiate(chosenType.prefab, spot.transform.position, spot.transform.rotation);
            EnemyInstance enemy = spawnedObject.GetComponent<EnemyInstance>();
            if (enemy == null)
            {
                Debug.LogWarning($"InvasionManager: {chosenType.displayName}'s prefab has no EnemyInstance component.");
                Destroy(spawnedObject);
                continue;
            }

            spot.TryClaim(enemy);
            enemy.Initialize(chosenType, targetRoom); // straight into RaidingRoom phase — no siege/walk-in leg at all
            enemy.SetClaimedSpot(spot); // so RelocateGroup/HandleEnemyDefeated can read this enemy's current spot later, same as a walk-in arrival sets it

            // Same "read fresh at invocation time" reasoning as the gate-siege walk-in subscription above
            // — this enemy can relocate to a different room long before it dies.
            EnemyInstance spawnedEnemy = enemy;
            spawnedEnemy.OnDefeated += () => HandleEnemyDefeated(spawnedEnemy.CurrentRoom, spawnedEnemy, spawnedEnemy.ClaimedInteriorSpot);

            spawnedGroup.Add(enemy);
        }

        if (spawnedGroup.Count == 0) return;

        raidActive = true;
        hasSpawnedFirstInvasion = true;

        if (activeInvasions.TryGetValue(targetRoom, out List<EnemyInstance> existingGroup))
            existingGroup.AddRange(spawnedGroup);
        else
            activeInvasions[targetRoom] = spawnedGroup;

        DebugLog.Log($"InvasionManager: spawned {spawnedGroup.Count} enemy(ies) directly into {targetRoom.name}.");

        TriggerAutoDefend(targetRoom);
        OnEnemyGroupSpawned?.Invoke(targetRoom, spawnedGroup);
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

        ClearRoomInvasion(room);
    }

    // A room's enemy group can empty out two ways — every enemy in it dies (HandleEnemyDefeated above), or
    // the whole group relocates away while still alive (RelocateGroup) — and both need the exact same
    // cleanup. Without this shared here, a bunny auto-defended into a room right as its enemies relocated
    // elsewhere (a real race: TriggerAutoDefend fires the instant enemies arrive, but the defender can take
    // a couple seconds to actually walk to its CombatSpot, during which RoomAbandonWatchRoutine can already
    // decide the room's abandoned and move the group on) was left stuck in Defending forever — nothing ever
    // told it the room was clear, since only HandleEnemyDefeated used to call this recall logic.
    private void ClearRoomInvasion(RoomBase room)
    {
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
        {
            raidActive = false;
            OnSiegeFullyCleared?.Invoke();
        }
    }
}
