using System;
using System.Collections.Generic;
using UnityEngine;

// Shared by ForagingLocationDefinition.difficultyTier AND the gate-check resolver in ForagingManager —
// per Foraging_DesignDoc.md's explicit "same 4-tier vocabulary reused for both" decision, so the player
// only ever learns one difficulty language.
public enum ForagingDifficultyTier
{
    Weak,
    Average,
    Tough,
    Brutal
}

[Serializable]
public struct StatCheckRange
{
    [Tooltip("Below this, the gate-check is a guaranteed fail.")]
    public int floor;
    [Tooltip("Above this, the gate-check is a guaranteed pass. Linear in between.")]
    public int ceiling;
}

// One entry per ForagingDifficultyTier. Centralizes every number the design doc calls out as
// "tunable, not yet numbered" for enemy encounters and gate-checks, in one place instead of scattered
// per-location fields, since both explicitly share this same 4-tier vocabulary.
[Serializable]
public class ForagingTierData
{
    public ForagingDifficultyTier tier;

    [Header("Enemy Encounter (placeholder resolver — see 'Placeholder encounter resolver' in the design doc)")]
    [Tooltip("Compared against the bunny's effective power (see ForagingManager) each encounter tick.")]
    public int enemyEffectivePower = 10;
    public int minDamageOnLoss = 3;
    public int maxDamageOnLoss = 8;
    [Tooltip("XP granted for a WIN against this tier's enemy.")]
    public float xpPerEnemyDefeated = 5f;

    [Header("Gate-Check (stat-gated skill check, separate from combat — see 'Gate-checks' in the design doc)")]
    public StatCheckRange attackCheck = new StatCheckRange { floor = 5, ceiling = 30 };
    public StatCheckRange defenseCheck = new StatCheckRange { floor = 5, ceiling = 30 };
    public StatCheckRange speedCheck = new StatCheckRange { floor = 5, ceiling = 30 };
    public StatCheckRange luckCheck = new StatCheckRange { floor = 5, ceiling = 30 };
    [Tooltip("XP granted only on a PASSED gate-check — failing has no downside at all.")]
    public float xpPerGateCheckPassed = 5f;

    [Header("Gold (separate mechanic from the rarity-banded loot table — see 'Loot economy')")]
    [Tooltip("Per-tick find chance. Doesn't scale with tier — the AMOUNT below does.")]
    [Range(0f, 1f)] public float goldFindChance = 0.15f;
    public int minGoldPerFind = 1;
    public int maxGoldPerFind = 5;
}

[CreateAssetMenu(fileName = "ForagingDifficultyTierConfig", menuName = "Burrowscape/Foraging Difficulty Tier Config")]
public class ForagingDifficultyTierConfig : ScriptableObject
{
    public List<ForagingTierData> tiers = new List<ForagingTierData>();

    // Falls back to an all-defaults entry (logged) rather than throwing if a tier hasn't been authored
    // yet — same "degrade gracefully, content not yet authored isn't an error state" philosophy as
    // BunnyTraitCatalog.RollTraits.
    public ForagingTierData GetTier(ForagingDifficultyTier tier)
    {
        foreach (ForagingTierData data in tiers)
            if (data.tier == tier) return data;

        Debug.LogWarning($"ForagingDifficultyTierConfig: no entry authored for tier {tier} — using untuned defaults.");
        return new ForagingTierData { tier = tier };
    }

    public StatCheckRange GetStatCheckRange(ForagingTierData data, NatureStat stat)
    {
        switch (stat)
        {
            case NatureStat.Attack: return data.attackCheck;
            case NatureStat.Defense: return data.defenseCheck;
            case NatureStat.Speed: return data.speedCheck;
            case NatureStat.Luck: return data.luckCheck;
            default: return data.attackCheck;
        }
    }
}
