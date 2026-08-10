using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public struct BunnyStats
{
    public int HP, Attack, Defense, Speed, Luck;
}

// All 5 core stats, including HP — deliberately separate from NatureStat (BunnyNature.cs), which only
// covers Attack/Defense/Speed/Luck since Nature/Zodiac never affects HP anywhere in the resolver below.
// Used by ForagingFruitDefinition/NPCBunny.AddEV, where HP needs to be a selectable target.
public enum BunnyStatType { HP, Attack, Defense, Speed, Luck }

// Resolves a BunnyTypeDefinition's Base stats plus this bunny's IV/EV individuality layer into actual
// in-game stats at a given level — the modified Pokemon-style formula from the Bunny Stat System
// Redesign design doc. Deliberately stops short of Nature (Zodiac): that's a separate second pass
// (NPCBunny.ApplyNatureEffects) run right after this, since it needs to check the bunny's Traits
// (Stoic) before it can finish — see that design doc's Option B for why this is a two-pass split
// instead of one function taking Nature/Traits too.
//
// Every floor() in the design doc's formula is plain integer division here, which equals a
// mathematical floor since every operand (Base, IV, EV, Level) is always non-negative.
public static class BunnyStatCalculator
{
    public static BunnyStats Resolve(BunnyTypeDefinition def, int level,
        int ivHP, int ivAttack, int ivDefense, int ivSpeed, int ivLuck,
        int evHP, int evAttack, int evDefense, int evSpeed, int evLuck)
    {
        return Resolve(def.baseHP, def.baseAttack, def.baseDefense, def.baseSpeed, def.baseLuck, level,
            ivHP, ivAttack, ivDefense, ivSpeed, ivLuck, evHP, evAttack, evDefense, evSpeed, evLuck);
    }

    // Raw-base-stat overload, extracted so callers with no BunnyTypeDefinition of their own (EnemyInstance
    // — see Combat_DesignDoc.md's "enemies level like bunnies") can reuse the exact same formula against
    // EnemyDefinition's base stats instead of duplicating it. Behavior of the BunnyTypeDefinition overload
    // above is unchanged — it's now a thin wrapper over this.
    public static BunnyStats Resolve(int baseHP, int baseAttack, int baseDefense, int baseSpeed, int baseLuck, int level,
        int ivHP, int ivAttack, int ivDefense, int ivSpeed, int ivLuck,
        int evHP, int evAttack, int evDefense, int evSpeed, int evLuck)
    {
        return new BunnyStats
        {
            HP = ResolveHP(baseHP, level, ivHP, evHP),
            Attack = ResolvePreNatureStat(baseAttack, level, ivAttack, evAttack),
            Defense = ResolvePreNatureStat(baseDefense, level, ivDefense, evDefense),
            Speed = ResolvePreNatureStat(baseSpeed, level, ivSpeed, evSpeed),
            Luck = ResolvePreNatureStat(baseLuck, level, ivLuck, evLuck),
        };
    }

    // HP = floor((2*Base + IV + floor(EV/4)) * Level / 50) + Level + 8, clamped to a minimum of 1.
    private static int ResolveHP(int baseStat, int level, int iv, int ev)
    {
        int raw = FloorFormulaCore(baseStat, level, iv, ev) + level + 8;
        return Mathf.Max(1, raw);
    }

    // Pre-Nature value for Attack/Defense/Speed/Luck (i.e. NatureMultiplier = 1 baked in). NOT clamped
    // here — ApplyNatureEffects applies the real +-10%/x1 multiplier and clamps afterward, per the
    // design doc's "clamp after the full formula resolves" rule.
    private static int ResolvePreNatureStat(int baseStat, int level, int iv, int ev)
    {
        return FloorFormulaCore(baseStat, level, iv, ev) + 4;
    }

    private static int FloorFormulaCore(int baseStat, int level, int iv, int ev)
    {
        return (2 * baseStat + iv + ev / 4) * level / 50;
    }
}

// Pure function of (type, level) — every passive on the type definition whose unlockLevel <= level AND
// whose discovered flag is true (see BunnyPassiveDefinition.discovered) — an undiscovered passive (e.g.
// Neutral's Adaptable before Ghost unlocks it) never shows up in ActivePassives regardless of level, so
// it can't appear in BunnyInfoUI's passive list or be treated as active by anything reading this result.
public static class BunnyPassiveResolver
{
    public static List<BunnyPassiveDefinition> ResolvePassives(BunnyTypeDefinition def, int level)
    {
        return def.passives.Where(p => p.discovered && p.unlockLevel <= level).ToList();
    }
}
