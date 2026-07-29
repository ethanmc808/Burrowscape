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

    public EnemyDefinition Definition => definition;
    public BunnyType Type { get; private set; }
    public int Level { get; private set; } = 1;
    public BunnyStats Stats { get; private set; }

    private int currentHP;

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
    public void Initialize(EnemyDefinition def, int? explicitLevel = null)
    {
        definition = def;
        Type = def.type;
        Level = explicitLevel ?? CombatBalanceConfig.Instance.RollEnemyLevel();

        Stats = BunnyStatCalculator.Resolve(
            def.baseHP, def.baseAttack, def.baseDefense, def.baseSpeed, def.baseLuck, Level,
            ivHP: 0, ivAttack: 0, ivDefense: 0, ivSpeed: 0, ivLuck: 0,
            evHP: 0, evAttack: 0, evDefense: 0, evSpeed: 0, evLuck: 0);

        currentHP = Stats.HP;
    }

    public void TakeCombatDamage(int amount)
    {
        if (currentHP <= 0) return;
        currentHP = Mathf.Max(0, currentHP - Mathf.Max(0, amount));
        if (currentHP == 0) OnDefeated?.Invoke();
    }
}
