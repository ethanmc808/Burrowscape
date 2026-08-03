using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Runtime counterpart to EnemyDefinition, playing the same role for enemies that NPCBunny plays for
// bunnies — but deliberately minimal, since enemies are flat stat-sticks (no IV/EV/Nature/traits, no
// job/need state machine). Level is rolled once at spawn from the player's population
// (CombatBalanceConfig.RollEnemyLevel) and never changes afterward, same "spawn-time-only level" rule
// WildBunnySpawner already uses for wild bunnies. Stats reuse BunnyStatCalculator's raw-stat overload
// with IV/EV fixed at 0 (no individual variance) — same formula, same level-scaling curve as a bunny,
// just without the individuality layer.
//
// NOT wired to any spawner yet — nothing in the codebase creates an EnemyInstance today. Whatever
// invasion-trigger system eventually spawns enemies into a room should Instantiate EnemyDefinition.prefab
// and call Initialize() once, the same way WildBunnySpawner calls NPCBunny.SetTypeAndProgression.
public class EnemyInstance : MonoBehaviour, ICombatant
{
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private Animator animator;
    [Tooltip("How long the Dying clip needs to finish playing before this GameObject is destroyed. Tune to match the actual clip length.")]
    [SerializeField] private float deathAnimationSeconds = 1f;
    [Tooltip("Local offset from this enemy's root added to CombatTransform.position when spawning attack VFX, so the attack can leave from mouth height instead of the floor.")]
    [SerializeField] private Vector3 attackOriginOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("Movement speed while walking to a flank position — melee enemies only, irrelevant for ranged (which never moves after spawn).")]
    [SerializeField] private float meleeMoveSpeed = 3f;
    [Tooltip("Root transform whose localScale.x sign is flipped to face left/right, same mechanism as NPCBunny's own scale-root flip. Only consulted by melee enemies (see HandleMeleeUpdate) — ranged enemy prefabs can leave this unset and keep their fixed spawn orientation exactly as before.")]
    [SerializeField] private Transform visualScaleRoot;
    [Tooltip("Movement speed while walking in from the gate to the interior target room, once the siege phase has been broken. Separate from meleeMoveSpeed (local flank shuffling) since this is base-scale walking.")]
    [SerializeField] private float walkInMoveSpeed = 2f;

    // Which of the three combat phases this enemy is currently in — replaces the old implicit
    // "room == null means not spawned yet" assumption, since room == null is now a legitimate state for
    // BesiegingGate/WalkingIn (see Combat_DesignDoc.md's Gate Siege section). Defaults to RaidingRoom so
    // the original Initialize(def, room) interior-spawn path is completely unaffected.
    public enum EnemyPhase { BesiegingGate, WalkingIn, RaidingRoom }
    private EnemyPhase phase = EnemyPhase.RaidingRoom;
    // The outside RoomSpot (ranged or melee) this enemy claimed at siege-spawn time — released the
    // moment it starts walking in (BeginWalkToRoom), not on eventual death, since siege-phase enemies are
    // invulnerable and can't die pre-breach (nothing can damage them: bunnies can't defend the gate, and
    // the gate never attacks back).
    private RoomSpot claimedSiegeSpot;

    public EnemyDefinition Definition => definition;
    public BunnyType Type { get; private set; }
    public int Level { get; private set; } = 1;
    public BunnyStats Stats { get; private set; }

    private int currentHP;
    private RoomBase room;

    // Cached once here rather than re-queried by VisualCenter every frame an attack homes toward this
    // enemy — GetComponentsInChildren allocates, and the set of renderers a rig has never changes at
    // runtime.
    private Renderer[] visualRenderers;

    private void Awake()
    {
        visualRenderers = GetComponentsInChildren<Renderer>();
    }

    // Combat AI (see CombatEngagement) — always targets whichever Defending bunny in `room` is closest,
    // per Ethan's design (a future quest system will let the player override this via clicking a target;
    // that's a separate, not-yet-built concern and doesn't touch this field).
    private ICombatant currentTarget;
    private float attackCooldownRemaining;
    // Snapshotted by Update when a wind-up starts, consumed by ReleasePendingAttack (called via an
    // Animation Event on this enemy's Attacking clip, at its "release" frame) — see CombatEngagement's
    // own comment for why firing is split into these two phases instead of instant-on-cooldown.
    private ICombatant pendingAttackTarget;

    // Melee-only state (see HandleMeleeUpdate) — mirrors NPCBunny.HandleMeleeDefending's shape, but uses
    // a small local state enum instead of reusing an existing umbrella state, since EnemyInstance has no
    // equivalent to NPCBunny's BunnyState.Defending to fold this into.
    private enum MeleeApproachState { SeekingTarget, MovingToFlank, Flanking }
    private MeleeApproachState meleeApproachState = MeleeApproachState.SeekingTarget;
    private FlankSlots claimedSlots;
    private ICombatant claimedTarget;
    private Vector3 flankDestination;
    // Enemies never flipped before melee needed it — defaults to true (facing whatever this prefab was
    // authored to spawn facing), matching AttackOrigin's existing behavior for every enemy that never
    // calls SetFacing (i.e. every current ranged prefab).
    private bool facingRight = true;

    // WalkingIn state (see BeginWalkToRoom/HandleWalkInUpdate) — deliberately simpler than
    // NPCBunny.MoveAlongPath (no facing-override table, no job-arrival dispatch), just a queue of
    // waypoints walked in order via a plain Vector3.MoveTowards-per-frame advance.
    private Queue<Transform> walkInPath;
    private Transform currentWalkTarget;
    private RoomBase pendingTargetRoom;
    private RoomSpot pendingTargetSpot;

    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    BunnyTypeDefinition ICombatant.AttackSource => definition != null ? definition.attackSource : null;
    Transform ICombatant.CombatTransform => transform;
    // `this == null` (Unity's real destroyed-object check) rather than unconditionally dereferencing
    // gameObject — Component.gameObject throws MissingReferenceException once this instance has been
    // Destroy()'d, instead of the "fake null" every caller checking CombatGameObject == null expects.
    GameObject ICombatant.CombatGameObject => this == null ? null : gameObject;
    // X negation now conditional on live facing — was unconditional before melee enemies needed to flip
    // at runtime (see facingRight/SetFacing). For any enemy that never calls SetFacing (every current
    // ranged prefab), facingRight stays at its default true, producing the exact same sign as the old
    // unconditional negation — behavior-preserving for existing content. Mirrors NPCBunny.AttackOrigin's
    // own live-facing check, just against facingRight instead of a scale-root read.
    Vector3 ICombatant.AttackOrigin => transform.position + new Vector3(facingRight ? -attackOriginOffset.x : attackOriginOffset.x, attackOriginOffset.y, attackOriginOffset.z);
    Vector3 ICombatant.VisualCenter => CombatEngagement.ComputeVisualCenter(visualRenderers, transform.position);
    bool ICombatant.IsFacingRight => facingRight;

    public event System.Action OnDefeated;

    // Called once by whatever spawns this enemy, immediately after Instantiate — mirrors
    // NPCBunny.SetTypeAndProgression's role. Rolls this instance's level from the current population if
    // no explicit level is passed (the normal case); an explicit level is there for testing/quest-spawned
    // enemies that might want a fixed difficulty instead of the population ramp.
    public void Initialize(EnemyDefinition def, RoomBase spawnedRoom, int? explicitLevel = null)
    {
        // GetComponentInChildren, not GetComponent — this prefab's Animator lives on a child (the
        // nested rig prefab instance), not the root GameObject EnemyInstance itself sits on.
        if (animator == null) animator = GetComponentInChildren<Animator>();

        definition = def;
        room = spawnedRoom;
        Type = def.type;
        Level = explicitLevel ?? CombatBalanceConfig.Instance.RollEnemyLevel();

        Stats = BunnyStatCalculator.Resolve(
            def.baseHP, def.baseAttack, def.baseDefense, def.baseSpeed, def.baseLuck, Level,
            ivHP: 0, ivAttack: 0, ivDefense: 0, ivSpeed: 0, ivLuck: 0,
            evHP: 0, evAttack: 0, evDefense: 0, evSpeed: 0, evLuck: 0);

        currentHP = Stats.HP;
    }

    // Called by InvasionManager.TrySpawnInvasion instead of Initialize for the gate-siege phase — spawns
    // with no room (siege enemies aren't in any room yet, they're outside attacking the gate) and tracks
    // the outside spot claimed for it, released once it starts walking in (see BeginWalkToRoom).
    public void InitializeForSiege(EnemyDefinition def, RoomSpot spot, int? explicitLevel = null)
    {
        Initialize(def, spawnedRoom: null, explicitLevel);
        claimedSiegeSpot = spot;
        phase = EnemyPhase.BesiegingGate;
    }

    private void Update()
    {
        if (currentHP <= 0 || DwellerRoster.Instance == null) return;

        if (phase == EnemyPhase.BesiegingGate) { HandleSiegeUpdate(); return; }
        if (phase == EnemyPhase.WalkingIn) { HandleWalkInUpdate(); return; }

        // phase == RaidingRoom — existing interior-invasion logic below, unchanged.
        if (room == null) return; // defensive — RaidingRoom should always have a room, but guard anyway

        BunnyTypeDefinition attackSource = definition != null ? definition.attackSource : null;
        if (attackSource != null && attackSource.isMelee)
        {
            HandleMeleeUpdate();
            return;
        }

        List<NPCBunny> defendersInRoom = DwellerRoster.Instance.GetBunniesDefendingRoom(room);
        List<ICombatant> candidatePool = defendersInRoom
            .Where(b => b.CurrentState == BunnyState.Defending)
            .Cast<ICombatant>()
            .ToList();

        // TEMP — chasing "enemy stops attacking entirely after fainting one of several defenders" bug.
        if (Time.frameCount % 30 == 0)
            Debug.Log($"[VFXDEBUG] EnemyInstance.Update({name}): defendersInRoom={string.Join(", ", defendersInRoom.Select(b => $"{b.name}:{b.CurrentState}"))} poolCount={candidatePool.Count} currentTarget={(currentTarget != null ? currentTarget.CombatGameObject?.name : "NULL")} cooldown={attackCooldownRemaining:F2}");

        bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, candidatePool, out ICombatant attackTarget);
        if (!startedWindUp) return;

        pendingAttackTarget = attackTarget;

        // Same one-shot trigger shape as "Die" below — Any State -> Attacking (Has Exit Time off),
        // Attacking -> Idle (Has Exit Time on) once wired in this enemy's own Animator Controller.
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    // Siege-phase combat — every siege enemy (melee or ranged) has exactly one possible target, the gate
    // itself, so this is simpler than the interior path: no room-wide candidate pool, no death handling
    // (siege enemies can't take damage pre-breach — see the EnemyPhase field comment). Ranged fires from
    // wherever it's standing (its siege spot), same as interior ranged always has; melee reuses the
    // existing HandleMeleeUpdate flanking logic unmodified, just pointed at the gate via candidateOverride.
    private void HandleSiegeUpdate()
    {
        ICombatant gate = EntranceGate.Instance;
        if (gate == null || !gate.IsAlive) return; // breach already resolved elsewhere (InvasionManager.HandleGateBreached) — just idle until this enemy is told to walk in

        BunnyTypeDefinition attackSource = definition != null ? definition.attackSource : null;
        if (attackSource != null && attackSource.isMelee)
        {
            HandleMeleeUpdate(candidateOverride: new List<ICombatant> { gate });
            return;
        }

        List<ICombatant> candidatePool = new List<ICombatant> { gate };
        bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, candidatePool, out ICombatant attackTarget);
        if (!startedWindUp) return;

        pendingAttackTarget = attackTarget;
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    // Called by InvasionManager.HandleGateBreached once this enemy has a real interior target — releases
    // the outside siege spot (leaving the gate for good) and walks the given path, set on arrival to
    // RaidingRoom so normal Update() dispatch takes over next frame exactly as any interior-spawned enemy.
    public void BeginWalkToRoom(List<Transform> path, RoomBase targetRoom, RoomSpot targetSpot)
    {
        claimedSiegeSpot?.Release(this);
        claimedSiegeSpot = null;

        if (path == null || path.Count == 0)
        {
            // No walkable route — fail-soft straight into the room rather than stranding this enemy
            // outside forever (same "don't leave an unreachable target reserved" tolerance BeginDefending
            // already uses elsewhere).
            room = targetRoom;
            phase = EnemyPhase.RaidingRoom;
            return;
        }

        pendingTargetRoom = targetRoom;
        pendingTargetSpot = targetSpot;
        walkInPath = new Queue<Transform>(path);
        phase = EnemyPhase.WalkingIn;
        currentWalkTarget = walkInPath.Count > 0 ? walkInPath.Dequeue() : null;
    }

    private void HandleWalkInUpdate()
    {
        if (currentWalkTarget == null)
        {
            room = pendingTargetRoom;
            phase = EnemyPhase.RaidingRoom;
            transform.position = pendingTargetSpot != null ? pendingTargetSpot.transform.position : transform.position;
            return;
        }

        Vector3 toTarget = currentWalkTarget.position - transform.position;
        if (toTarget.magnitude <= 0.05f)
        {
            transform.position = currentWalkTarget.position;
            currentWalkTarget = walkInPath.Count > 0 ? walkInPath.Dequeue() : null;
            return;
        }

        Vector3 dir = toTarget.normalized;
        transform.position += dir * walkInMoveSpeed * Time.deltaTime;
        if (Mathf.Abs(dir.x) > 0.01f) SetFacing(dir.x < 0f); // reuses existing SetFacing/visualScaleRoot, no-op if unset
    }

    // Melee counterpart to Update above — mirrors NPCBunny.HandleMeleeDefending's shape exactly (pick a
    // target with an open FlankSlots slot, walk to a computed position beside it, attack from a pinned
    // single-target pool once there), just with EnemyInstance's own simpler local move (enemies don't do
    // general base pathing the way bunnies do — a straight MoveTowards within the room is enough, since a
    // melee enemy only ever repositions locally after its one fixed EnemySpot spawn).
    //
    // candidateOverride: when non-null (the siege-phase call from HandleSiegeUpdate), used as the
    // candidate pool instead of the normal DwellerRoster-based room query — lets the gate-siege phase
    // reuse this entire method, including its flank-claim/walk/attack logic, completely unmodified.
    private void HandleMeleeUpdate(IEnumerable<ICombatant> candidateOverride = null)
    {
        if (claimedSlots != null)
        {
            if (claimedTarget == null || claimedTarget.CombatGameObject == null || !claimedTarget.IsAlive)
            {
                claimedSlots.ReleaseSlot(this);
                claimedSlots = null;
                claimedTarget = null;
                meleeApproachState = MeleeApproachState.SeekingTarget;
                return;
            }

            if (meleeApproachState == MeleeApproachState.MovingToFlank)
            {
                transform.position = Vector3.MoveTowards(transform.position, flankDestination, meleeMoveSpeed * Time.deltaTime);
                if (Vector3.Distance(transform.position, flankDestination) <= 0.05f)
                    meleeApproachState = MeleeApproachState.Flanking;
                return; // no attacking mid-walk
            }

            List<ICombatant> pinnedPool = new List<ICombatant> { claimedTarget };
            bool startedWindUp = CombatEngagement.TryBeginAttack(this, ref currentTarget, ref attackCooldownRemaining, pinnedPool, out ICombatant attackTarget);
            if (!startedWindUp) return;

            pendingAttackTarget = attackTarget;
            if (animator != null)
                animator.SetTrigger("Attack");
            return;
        }

        List<ICombatant> candidatePool;
        if (candidateOverride != null)
        {
            candidatePool = candidateOverride.ToList();
        }
        else
        {
            List<NPCBunny> defendersInRoom = DwellerRoster.Instance.GetBunniesDefendingRoom(room);
            candidatePool = defendersInRoom
                .Where(b => b.CurrentState == BunnyState.Defending)
                .Cast<ICombatant>()
                .ToList();
        }

        ICombatant chosen = CombatEngagement.FindBestMeleeTarget(transform.position, candidatePool);
        if (chosen == null) return; // nobody defending has an open flank slot right now

        FlankSlots slots = CombatEngagement.GetFlankSlots(chosen);
        if (slots == null || !slots.TryClaimSlot(this, out FlankSide side)) return;

        claimedSlots = slots;
        claimedTarget = chosen;
        flankDestination = CombatEngagement.ComputeFlankPosition(chosen, side);
        meleeApproachState = MeleeApproachState.MovingToFlank;
        // FlankSide.Left gets a NEGATIVE world-X offset in ComputeFlankPosition, and this project's
        // world-X is inverted from screen space (bigger world-X = screen-left) — so "Left" actually lands
        // the attacker on the target's screen-RIGHT, and it must face left (toward the target) from there.
        // Same fix as NPCBunny.HandleMeleeDefending, confirmed empirically there in Play mode.
        SetFacing(side != FlankSide.Left);
    }

    // No-op if visualScaleRoot isn't wired — every current ranged enemy prefab never calls this at all,
    // so facingRight stays at its default true and AttackOrigin's sign is unaffected for them.
    private void SetFacing(bool faceRight)
    {
        if (visualScaleRoot == null || facingRight == faceRight) return;
        facingRight = faceRight;
        Vector3 scale = visualScaleRoot.localScale;
        // Sign convention not yet verified empirically against a real melee enemy rig — flip if a
        // "face right" call visually lands facing left in Play mode.
        scale.x = Mathf.Abs(scale.x) * (faceRight ? 1f : -1f);
        visualScaleRoot.localScale = scale;
    }

    // Animation Event receiver — place this on this enemy's Attacking clip at the frame the attack
    // visually "releases", so the AttackInstance VFX spawns in sync with the animation instead of
    // instantly on cooldown. Re-validates the target itself (see CombatEngagement.ReleaseAttack) since a
    // few frames of wind-up may have passed since TryBeginAttack snapshotted it.
    public void ReleasePendingAttack()
    {
        if (pendingAttackTarget == null) return;
        CombatEngagement.ReleaseAttack(this, pendingAttackTarget);
        pendingAttackTarget = null;
    }

    // Test-only hook for VFXPreviewHarness — triggers the same wind-up animation Update does (skipping
    // its DwellerRoster/room/cooldown gating, which the isolated preview scene has no reason to set up),
    // so the Attacking clip's existing Animation Event still fires ReleasePendingAttack above at the real
    // "release" frame, the same path actual combat uses.
    public void TestFireAttack(ICombatant target)
    {
        pendingAttackTarget = target;
        if (animator != null)
            animator.SetTrigger("Attack");
    }

    public void TakeCombatDamage(int amount)
    {
        if (currentHP <= 0) return;
        currentHP = Mathf.Max(0, currentHP - Mathf.Max(0, amount));
        if (currentHP != 0) return;

        // Same "release immediately, don't leak the slot" reasoning as NPCBunny.TakeCombatDamage — a
        // defeated melee enemy would otherwise permanently occupy a flank slot on whoever it was flanking.
        if (claimedSlots != null)
        {
            claimedSlots.ReleaseSlot(this);
            claimedSlots = null;
            claimedTarget = null;
        }

        if (animator != null) animator.SetTrigger("Die");
        OnDefeated?.Invoke();

        // Self-destructs after the Dying clip has time to play, rather than InvasionManager destroying
        // this instantly on defeat — gameplay bookkeeping (EnemySpot release, invasion tracking) still
        // happens immediately via OnDefeated above, only the visual corpse lingers.
        Destroy(gameObject, deathAnimationSeconds);
    }

    // Test-only hook — until the AI/attack-triggering layer exists, there's no other way to defeat an
    // enemy and exercise InvasionManager's OnInvasionCleared/defender-recall path. Right-click this
    // component's header in Play mode to use.
    [ContextMenu("Debug: Kill (Test)")]
    private void DebugKill()
    {
        TakeCombatDamage(currentHP);
    }
}
