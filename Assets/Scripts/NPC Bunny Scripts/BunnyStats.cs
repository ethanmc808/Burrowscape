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
        return new BunnyStats
        {
            HP = ResolveHP(def.baseHP, level, ivHP, evHP),
            Attack = ResolvePreNatureStat(def.baseAttack, level, ivAttack, evAttack),
            Defense = ResolvePreNatureStat(def.baseDefense, level, ivDefense, evDefense),
            Speed = ResolvePreNatureStat(def.baseSpeed, level, ivSpeed, evSpeed),
            Luck = ResolvePreNatureStat(def.baseLuck, level, ivLuck, evLuck),
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

// Pure function of (type, level) — every passive on the type definition whose unlockLevel <= level.
// Passives are data tags only right now (no effect/behavior hook exists yet — see the design doc).
public static class BunnyPassiveResolver
{
    public static List<BunnyPassiveDefinition> ResolvePassives(BunnyTypeDefinition def, int level)
    {
        return def.passives.Where(p => p.unlockLevel <= level).ToList();
    }
}
