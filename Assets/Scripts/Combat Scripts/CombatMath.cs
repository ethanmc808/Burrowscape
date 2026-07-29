// Pure combat math helpers shared by bunnies and enemies. See TypeChart.cs for the type-effectiveness
// half of damage resolution.
public static class CombatMath
{
    public const float BaseCritChance = 1f / 16f;
    public const float CritChancePerLuckPoint = 0.0005f;
    public const float MaxCritChance = 0.5f;

    // luckStat is the fully resolved stat (post IV/EV/Nature), not a type's base Luck value.
    public static float GetCritChance(int luckStat)
    {
        float chance = BaseCritChance + luckStat * CritChancePerLuckPoint;
        return UnityEngine.Mathf.Clamp(chance, 0f, MaxCritChance);
    }

    // Every type has exactly one signature attack (see BunnyTypeDefinition.attackName) whose Base Power
    // climbs every 10 levels: 1-9 -> 20, 10-19 -> 40, 20-29 -> 60, 30-39 -> 80, 40+ -> 100.
    public static int GetBasePower(int level)
    {
        int tier = UnityEngine.Mathf.Clamp((level - 1) / 10, 0, 4);
        return 20 * (tier + 1);
    }
}
