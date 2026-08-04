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
    // climbs every 10 levels: tier 0 (level 1-9) is startingBasePower x1, tier 1 (10-19) x2, ... tier 4
    // (40+) x5. The TIER SHAPE is a structural rule nobody asked to retune, so it stays a plain function
    // rather than living in CombatBalanceConfig — but the tier-0 starting value itself is per-type (see
    // BunnyTypeDefinition.attackBasePower), since attack interval and base Attack stat both vary a lot by
    // type and a shared flat 20 badly undersells slow-attacking types (Ethan's call, 2026-08-03: e.g. Plant
    // attacking once per 5s vs. Fire once per 1s at the same Base Power was ~5x Fire's effective DPS).
    public static int GetBasePower(int level, int startingBasePower)
    {
        int tier = Mathf.Clamp((level - 1) / 10, 0, 4);
        return startingBasePower * (tier + 1);
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
