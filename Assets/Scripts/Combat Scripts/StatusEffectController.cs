using UnityEngine;

// Optional add-on component (put it on any NPCBunny or EnemyInstance GameObject) that tracks ONE active
// status effect at a time — applying a new status while one is already active replaces it rather than
// stacking. Nothing in Combat_DesignDoc.md specifies stacking behavior, and "only one status at a time"
// is the simpler, more classic interpretation consistent with this project's "one signature attack per
// type" philosophy elsewhere in this design; revisit if that turns out to feel bad in practice.
//
// CombatResolver is responsible for rolling CombatBalanceConfig.statusChanceOnHit and mapping the
// attacker's type via StatusEffectLink before calling ApplyStatus here — this component only cares about
// applying/ticking/expiring whatever it's told to.
public class StatusEffectController : MonoBehaviour
{
    private const float TickInterval = 1f; // DOT ticks once per second, same convention as RoomBase's Work XP tick

    private ICombatant owner;
    private StatusEffectType? activeStatus;
    private float remainingDuration;
    private float tickTimer;

    public StatusEffectType? ActiveStatus => activeStatus;
    public bool IsIncapacitated => activeStatus == StatusEffectType.Sleep;

    private void Awake()
    {
        owner = GetComponent<ICombatant>();
        if (owner == null)
            Debug.LogWarning($"{name}: StatusEffectController requires an ICombatant (NPCBunny/EnemyInstance) on the same GameObject.");
    }

    public void ApplyStatus(StatusEffectType type)
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        activeStatus = type;
        tickTimer = 0f;
        remainingDuration = type switch
        {
            StatusEffectType.Burn => cfg.burnDurationSeconds,
            StatusEffectType.Poison => cfg.poisonDurationSeconds,
            StatusEffectType.Chill => cfg.chillDurationSeconds,
            StatusEffectType.Paralyze => cfg.paralyzeDurationSeconds,
            StatusEffectType.Sleep => cfg.sleepDurationSeconds,
            _ => 0f,
        };
    }

    public void ClearStatus()
    {
        activeStatus = null;
    }

    private void Update()
    {
        if (activeStatus == null || owner == null) return;

        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0f)
        {
            activeStatus = null;
            return;
        }

        if (activeStatus == StatusEffectType.Burn || activeStatus == StatusEffectType.Poison)
        {
            tickTimer += Time.deltaTime;
            if (tickTimer >= TickInterval)
            {
                tickTimer -= TickInterval;
                ApplyDotTick();
            }
        }
    }

    private void ApplyDotTick()
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        float fraction = activeStatus == StatusEffectType.Burn ? cfg.burnDotFractionOfMaxHP : cfg.poisonDotFractionOfMaxHP;
        int tickDamage = Mathf.Max(1, Mathf.RoundToInt(owner.Stats.HP * fraction));
        owner.TakeCombatDamage(tickDamage);
    }

    // ---------- Stat modifiers, applied on top of the owner's base Stats at point of use ----------
    // (CombatResolver calls these instead of reading owner.Stats.Attack/Defense/Speed directly.)

    public int ModifyAttack(int baseAttack)
    {
        if (activeStatus != StatusEffectType.Burn) return baseAttack;
        return Mathf.RoundToInt(baseAttack * (1f - CombatBalanceConfig.Instance.burnAttackDebuff));
    }

    public int ModifyDefense(int baseDefense)
    {
        if (activeStatus != StatusEffectType.Poison) return baseDefense;
        return Mathf.RoundToInt(baseDefense * (1f - CombatBalanceConfig.Instance.poisonDefenseDebuff));
    }

    // Chill hits the actual Speed STAT (cascades into hit/evasion via CombatMath.GetHitChance) —
    // deliberately different from Paralyze's movement/attack-speed multipliers below, which do NOT
    // touch this value or cascade into hit/evasion.
    public int ModifySpeed(int baseSpeed)
    {
        if (activeStatus != StatusEffectType.Chill) return baseSpeed;
        return Mathf.RoundToInt(baseSpeed * (1f - CombatBalanceConfig.Instance.chillSpeedStatDebuff));
    }

    public float MovementSpeedMultiplier =>
        activeStatus == StatusEffectType.Paralyze ? 1f - CombatBalanceConfig.Instance.paralyzeMovementSpeedDebuff : 1f;

    public float AttackSpeedMultiplier =>
        activeStatus == StatusEffectType.Paralyze ? 1f - CombatBalanceConfig.Instance.paralyzeAttackSpeedDebuff : 1f;
}
