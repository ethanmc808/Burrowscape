using UnityEngine;

// Pure combat math helpers shared by bunnies and enemies. See TypeChart.cs for the type-effectiveness
// half of damage resolution, and CombatBalanceConfig for every tunable number used below — deliberately
// NOT hardcoded here (unlike TypeChart's matchup grid and GetBasePower's level-tier breakpoints, which
// Ethan said are fine to stay as code) since these were explicitly called out as needing to be "easily
// editable for future game balance."
public static class CombatMath
{
    // luckStat is the fully resolved stat (post IV/EV/Nature), not a type's base Luck value.
    public static float GetCritChance(int luckStat)
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        float chance = cfg.critBaseChance + luckStat * cfg.critChancePerLuckPoint;
        return Mathf.Clamp(chance, 0f, cfg.maxCritChance);
    }

    // Every type has exactly one signature attack (see BunnyTypeDefinition.attackName) whose Base Power
    // climbs every 10 levels: 1-9 -> 20, 10-19 -> 40, 20-29 -> 60, 30-39 -> 80, 40+ -> 100. A structural
    // rule rather than a balance number anyone asked to retune, so it stays a plain function rather than
    // living in CombatBalanceConfig.
    public static int GetBasePower(int level)
    {
        int tier = Mathf.Clamp((level - 1) / 10, 0, 4);
        return 20 * (tier + 1);
    }

    // Speed-based hit/evasion roll — gates whether an attack lands at ALL, resolved at the moment the
    // attack's hitbox actually reaches the defender (see AttackInstance), separate from and prior to the
    // crit/damage math below. Higher attacker Speed relative to the defender's increases the chance to
    // land; higher defender Speed relative to the attacker's increases the chance to be missed.
    public static float GetHitChance(int attackerSpeed, int defenderSpeed)
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        float chance = cfg.hitChanceBase + (attackerSpeed - defenderSpeed) * cfg.hitChancePerSpeedDifference;
        return Mathf.Clamp(chance, cfg.minHitChance, cfg.maxHitChance);
    }

    // Integer 80-100 (or whatever CombatBalanceConfig specifies) divided by 100 — the "random" term in
    // the damage formula, rolled fresh per hit.
    public static float RollDamageVariance()
    {
        CombatBalanceConfig cfg = CombatBalanceConfig.Instance;
        int roll = Random.Range(cfg.randomVarianceMinPercent, cfg.randomVarianceMaxPercent + 1);
        return roll / 100f;
    }
}
