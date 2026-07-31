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

    public EnemyDefinition Definition => definition;
    public BunnyType Type { get; private set; }
    public int Level { get; private set; } = 1;
    public BunnyStats Stats { get; private set; }

    private int currentHP;
    private RoomBase room;

    // Combat AI (see CombatEngagement) — always targets whichever Defending bunny in `room` is closest,
    // per Ethan's design (a future quest system will let the player override this via clicking a target;
    // that's a separate, not-yet-built concern and doesn't touch this field).
    private ICombatant currentTarget;
    private float attackCooldownRemaining;

    int ICombatant.CurrentHP => currentHP;
    bool ICombatant.IsAlive => currentHP > 0;
    BunnyTypeDefinition ICombatant.AttackSource => definition != null ? definition.attackSource : null;
    Transform ICombatant.CombatTransform => transform;
    GameObject ICombatant.CombatGameObject => gameObject;

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

        IEnumerable<ICombatant> candidatePool = DwellerRoster.Instance
            .GetBunniesDefendingRoom(room)
            .Where(b => b.CurrentState == BunnyState.Defending)
            .Cast<ICombatant>();

        bool fired = CombatEngagement.Tick(this, ref currentTarget, ref attackCooldownRemaining, candidatePool);

        // Same one-shot trigger shape as "Die" above — Any State -> Attacking (Has Exit Time off),
        // Attacking -> Idle (Has Exit Time on) once wired in this enemy's own Animator Controller.
        if (fired && animator != null)
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
