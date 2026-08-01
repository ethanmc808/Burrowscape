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

    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    BunnyTypeDefinition ICombatant.AttackSource => definition != null ? definition.attackSource : null;
    Transform ICombatant.CombatTransform => transform;
    // `this == null` (Unity's real destroyed-object check) rather than unconditionally dereferencing
    // gameObject — Component.gameObject throws MissingReferenceException once this instance has been
    // Destroy()'d, instead of the "fake null" every caller checking CombatGameObject == null expects.
    GameObject ICombatant.CombatGameObject => this == null ? null : gameObject;
    // X negated unconditionally, unlike NPCBunny.AttackOrigin's live-facing check. Bunnies self-correct
    // offset.x dynamically because NPCBunny actually flips bunnyScaleRoot at runtime as it turns to face
    // different directions; enemies never flip at runtime (spawned once, stationary, never re-faced), so
    // there's no live state to check here — this negation is just the fixed correction that matches this
    // enemy rig's spawn orientation against this project's inverted X-axis (increasing world X = screen-
    // LEFT), verified empirically against actual Play-mode attack spawns. Revisit if an enemy type ever
    // needs runtime facing flips (would need a ScaleRoot-based live check like NPCBunny's instead).
    Vector3 ICombatant.AttackOrigin => transform.position + new Vector3(-attackOriginOffset.x, attackOriginOffset.y, attackOriginOffset.z);
    Vector3 ICombatant.VisualCenter => CombatEngagement.ComputeVisualCenter(visualRenderers, transform.position);

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

    private void Update()
    {
        if (currentHP <= 0 || DwellerRoster.Instance == null || room == null) return;

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
