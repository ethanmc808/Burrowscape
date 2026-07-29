using UnityEngine;

// What actually happened when an attack landed (or didn't) — enough for whatever plays the hit
// reaction/floating number/status VFX to react without re-deriving anything.
public struct CombatHitResult
{
    public bool Hit;
    public bool Crit;
    public int Damage;
    public StatusEffectType? AppliedStatus;

    public static CombatHitResult Miss => new CombatHitResult { Hit = false };
}

// Ties TypeChart + CombatMath + CombatBalanceConfig + status effects together into the actual damage
// formula from Combat_DesignDoc.md. Called exactly once, at the moment an AttackInstance's hitbox
// arrives at its target (see AttackInstance) — never at cast time, per the "attack fired" vs. "damage
// applied" decoupling that's the whole point of this combat model.
public static class CombatResolver
{
    public static CombatHitResult ResolveHit(ICombatant attacker, ICombatant defender)
    {
        StatusEffectController attackerStatus = attacker.CombatGameObject != null ? attacker.CombatGameObject.GetComponent<StatusEffectController>() : null;
        StatusEffectController defenderStatus = defender.CombatGameObject != null ? defender.CombatGameObject.GetComponent<StatusEffectController>() : null;

        int attackerSpeed = attackerStatus != null ? attackerStatus.ModifySpeed(attacker.Stats.Speed) : attacker.Stats.Speed;
        int defenderSpeed = defenderStatus != null ? defenderStatus.ModifySpeed(defender.Stats.Speed) : defender.Stats.Speed;

        float hitChance = CombatMath.GetHitChance(attackerSpeed, defenderSpeed);
        if (Random.value > hitChance)
            return CombatHitResult.Miss;

        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;

        bool crit = Random.value < CombatMath.GetCritChance(attacker.Stats.Luck);

        int power = CombatMath.GetBasePower(attacker.Level);
        int attackStat = attackerStatus != null ? attackerStatus.ModifyAttack(attacker.Stats.Attack) : attacker.Stats.Attack;
        int defenseStat = defenderStatus != null ? defenderStatus.ModifyDefense(defender.Stats.Defense) : defender.Stats.Defense;
        defenseStat = Mathf.Max(1, defenseStat); // guard against a fully-zeroed-out Defense dividing by zero

        int core = (2 * attacker.Level / 5 + 2) * power * attackStat / defenseStat / 50 + 2;

        float critMult = crit ? cfg.critDamageMultiplier : 1f;
        float random = CombatMath.RollDamageVariance();
        float stab = cfg.stabMultiplier; // always-on for now — see design doc's STAB note
        float typeMult = TypeChart.GetMultiplier(attacker.Type, defender.Type);

        int damage = Mathf.Max(1, Mathf.FloorToInt(core * critMult * random * stab * typeMult));
        defender.TakeCombatDamage(damage);

        StatusEffectType? appliedStatus = null;
        if (Random.value < cfg.statusChanceOnHit)
        {
            StatusEffectType? linked = StatusEffectLink.GetLinkedStatus(attacker.Type);
            if (linked.HasValue && defenderStatus != null)
            {
                defenderStatus.ApplyStatus(linked.Value);
                appliedStatus = linked;
            }
        }

        return new CombatHitResult { Hit = true, Crit = crit, Damage = damage, AppliedStatus = appliedStatus };
    }
}
