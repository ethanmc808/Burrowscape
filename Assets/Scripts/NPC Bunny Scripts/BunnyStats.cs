using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public struct BunnyStats
{
    public int HP, Attack, Defense, Speed, Luck;
}

// Resolves a BunnyTypeDefinition's placeholder base stats into actual in-game stats at a given level.
// See BunnyTypeSystem_DesignDoc.md's "Stat resolution" section. Same linear growth for every stat, but
// HP and Luck get a fixed post-multiplier (HP x2, Luck x0.5) so those two scale differently from
// Attack/Defense/Speed and the stat spread between types stays interesting at high level — deliberate,
// not a placeholder like the base stat numbers themselves.
public static class BunnyStatCalculator
{
    private const float HPMultiplier = 2f;
    private const float LuckMultiplier = 0.5f;

    public static BunnyStats Resolve(BunnyTypeDefinition def, int level, float growthRate)
    {
        float mult = 1f + growthRate * (level - 1);
        return new BunnyStats
        {
            HP = Mathf.RoundToInt(def.baseHP * mult * HPMultiplier),
            Attack = Mathf.RoundToInt(def.baseAttack * mult),
            Defense = Mathf.RoundToInt(def.baseDefense * mult),
            Speed = Mathf.RoundToInt(def.baseSpeed * mult),
            Luck = Mathf.RoundToInt(def.baseLuck * mult * LuckMultiplier),
        };
    }
}

// Pure function of (type, level) — every passive on the type definition whose unlockLevel <= level.
// Passives are data tags only right now (no effect/behavior hook exists yet — see the design doc).
public static class BunnyPassiveResolver
{
    public static List<BunnyPassiveDefinition> ResolvePassives(BunnyTypeDefinition def, int level)
    {
        return def.passives.Where(p => p.unlockLevel <= level).ToList();
    }
}
