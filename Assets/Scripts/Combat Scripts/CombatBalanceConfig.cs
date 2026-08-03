using UnityEngine;

// Central home for every combat number Ethan wants "easily editable for future game balance"
// (Combat_DesignDoc.md) — crit/accuracy/STAB/random-variance/status effect numbers, plus the enemy
// level-by-population ramp. Deliberately NOT where TypeChart's matchup grid or CombatMath.GetBasePower's
// level-tier breakpoints live — those were called out as fine to stay hardcoded (TypeChart explicitly;
// GetBasePower's tiers are a structural rule, not a balance number anyone asked to retune).
//
// Loaded via Resources (same pattern as RoomThemeCatalog.Load) rather than threaded through every
// consumer as a serialized field, since CombatMath/CombatResolver/StatusEffectController are static or
// live on many different GameObjects (bunnies, enemies) with no single natural owner to hold the
// reference. Must live at Assets/Resources/CombatBalanceConfig.asset — created via
// Burrowscape > Generate Combat Balance Config (Editor/CombatBalanceConfigGenerator.cs), not hand-authored,
// same reasoning as every other generator-seeded asset in this project (avoids hand-guessing a script GUID).
[CreateAssetMenu(fileName = "CombatBalanceConfig", menuName = "Burrowscape/Combat Balance Config")]
public class CombatBalanceConfig : ScriptableObject
{
    private const string ResourcesPath = "CombatBalanceConfig";
    private static CombatBalanceConfig cachedInstance;
    private static bool hasTriedLoad;

    // Logs once and falls back to a transient default-valued instance (rather than throwing) if the
    // asset hasn't been generated yet — same "degrade gracefully, content not yet authored isn't an
    // error state" philosophy as ForagingDifficultyTierConfig.GetTier.
    public static CombatBalanceConfig Instance
    {
        get
        {
            if (!hasTriedLoad)
            {
                cachedInstance = Resources.Load<CombatBalanceConfig>(ResourcesPath);
                hasTriedLoad = true;
                if (cachedInstance == null)
                {
                    Debug.LogWarning("CombatBalanceConfig: no asset found at Resources/CombatBalanceConfig — using untuned defaults. Run Burrowscape > Generate Combat Balance Config.");
                    cachedInstance = CreateInstance<CombatBalanceConfig>();
                }
            }
            return cachedInstance;
        }
    }

    [Header("Critical Hits")]
    [Tooltip("Base crit chance before Luck (1/16 = 0.0625).")]
    public float critBaseChance = 1f / 16f;
    public float critChancePerLuckPoint = 0.0005f;
    public float maxCritChance = 0.5f;
    [Tooltip("Damage multiplier applied on top of the normal formula when a crit lands.")]
    public float critDamageMultiplier = 2f;

    [Header("Damage Random Variance")]
    [Tooltip("Inclusive integer percent range rolled per hit, e.g. 80-100 means 0.80x-1.00x damage.")]
    public int randomVarianceMinPercent = 80;
    public int randomVarianceMaxPercent = 100;

    [Header("STAB")]
    [Tooltip("Always-on for now since every attack is same-type-by-construction (see design doc).")]
    public float stabMultiplier = 1.5f;

    [Header("Hit / Evasion")]
    public float hitChanceBase = 0.90f;
    public float hitChancePerSpeedDifference = 0.0005f;
    public float minHitChance = 0.50f;
    public float maxHitChance = 0.95f;

    [Header("Status Effects — Trigger")]
    [Tooltip("Chance per landed hit that a type's attack applies its linked status. Placeholder, not finalized per the design doc.")]
    [Range(0f, 1f)] public float statusChanceOnHit = 0.20f;

    [Header("Status Effects — Burn (Fire)")]
    public float burnDotFractionOfMaxHP = 1f / 16f;
    public float burnDurationSeconds = 4f;
    [Range(0f, 1f)] public float burnAttackDebuff = 0.25f;

    [Header("Status Effects — Poison (Toxic)")]
    public float poisonDotFractionOfMaxHP = 1f / 32f;
    public float poisonDurationSeconds = 8f;
    [Range(0f, 1f)] public float poisonDefenseDebuff = 0.25f;

    [Header("Status Effects — Chill (Ice)")]
    [Tooltip("Hits the actual Speed STAT directly (cascades into hit/evasion) — different mechanism than Paralyze.")]
    [Range(0f, 1f)] public float chillSpeedStatDebuff = 0.35f;
    public float chillDurationSeconds = 6f;

    [Header("Status Effects — Paralyze (Shock)")]
    [Tooltip("Slows movement/attack timing directly WITHOUT touching the Speed stat — does not cascade into hit/evasion.")]
    [Range(0f, 1f)] public float paralyzeMovementSpeedDebuff = 0.25f;
    [Range(0f, 1f)] public float paralyzeAttackSpeedDebuff = 0.25f;
    public float paralyzeDurationSeconds = 6f;

    [Header("Status Effects — Sleep (Mind)")]
    [Tooltip("Full incapacitation — cannot move or act while active.")]
    public float sleepDurationSeconds = 3f;

    [Header("Positioning")]
    [Tooltip("Universal ranged attack max distance, spans the largest possible room footprint (12x2x6).")]
    public float rangedMaxRange = 12f;
    [Tooltip("PLACEHOLDER — exact value not yet decided per the design doc. How far to the left/right of a target a melee attacker stands.")]
    public float meleeStandingDistance = 1f;
    [Tooltip("Left + right slot, per target, both bunny-on-enemy and enemy-on-bunny.")]
    public int maxFlankersPerTarget = 2;

    [Header("Guard Room Buffs")]
    [Tooltip("Grade 1+ — Attack multiplier applied via GuardBuffController while a bunny is posted at a Guard Room. Values TBD/tunable.")]
    public float guardGrade1AttackMultiplier = 1.2f;
    [Tooltip("Grade 1+ — Defense multiplier, same lifecycle as the Attack multiplier above.")]
    public float guardGrade1DefenseMultiplier = 1.2f;
    [Tooltip("Grade 3 only — Speed multiplier, granted/revoked alongside the shield buffer below.")]
    public float guardGrade3SpeedMultiplier = 1.15f;
    [Tooltip("Grade 3 only — flat HP shield buffer granted at full on assignment, cleared entirely on unassignment. Absorbs damage before real HP (see NPCBunny.TakeCombatDamage).")]
    public int guardGrade3ShieldAmount = 50;

    [Header("Entrance Gate")]
    [Tooltip("Gate HP by Entrance Room grade — raiders must destroy the gate before entering.")]
    public int gateGrade1MaxHP = 200;
    public int gateGrade2MaxHP = 350;
    public int gateGrade3MaxHP = 550;
    [Tooltip("Gate Defense by Entrance Room grade.")]
    public int gateGrade1Defense = 10;
    public int gateGrade2Defense = 20;
    public int gateGrade3Defense = 35;
    [Tooltip("Fixed, NOT per-grade — deliberately low so enemies rarely miss the gate.")]
    public int gateSpeed = 1;

    [Header("Enemy Leveling By Population")]
    [Tooltip("Same ramp shape as WildBunnySpawner's population-based level ramp, separate knobs per the design doc.")]
    public int levelRampStartPopulation = 10;
    public int levelRampCapPopulation = 200;
    public int levelRampStartMinLevel = 3;
    public int levelRampStartMaxLevel = 5;
    public int levelRampCapMinLevel = 45;
    public int levelRampCapMaxLevel = 50;

    [Header("Invasion Pacing By Population")]
    [Tooltip("Reuses levelRampStartPopulation/levelRampCapPopulation above for the same difficulty curve — no separate population thresholds.")]
    public float invasionStartMinWaitMinutes = 3f;
    public float invasionStartMaxWaitMinutes = 6f;
    public float invasionCapMinWaitMinutes = 20f;
    public float invasionCapMaxWaitMinutes = 30f;
    public int invasionGroupSizeStartMin = 1;
    public int invasionGroupSizeStartMax = 1;
    public int invasionGroupSizeCapMin = 2;
    public int invasionGroupSizeCapMax = 3;

    [Header("Invasion Cooldown")]
    [Tooltip("Only one invasion (siege + whatever it fed into interior rooms) is ever active at a time — InvasionManager's loop waits for full resolution before counting toward the next one at all. This is a FIXED minimum quiet period on top of that, elapsing before the population-based wait above even starts ticking — Ethan's explicit call: more than one invasion at once would be overwhelming early game, and population pacing's own low end can dip well under a comfortable floor. Late-game difficulty should come from the level/group-size ramp, not raw frequency.")]
    public float invasionCooldownMinutes = 5f;
    [Range(0f, 1f)]
    [Tooltip("Each invasion cycle rolls exactly ONE raid type — never both, per the single-active-raid rule above. Chance the roll picks a gate-siege invasion (walk up, besiege, breach, then raid the chokepoint room); the remainder picks a direct in-room spawn (enemies appear straight into a random room's EnemySpots, bypassing the gate entirely). 0.5 = 50/50, tunable.")]
    public float gateSiegeInvasionChance = 0.5f;

    [Header("Enemy Room Relocation")]
    [Tooltip("How often InvasionManager re-checks whether an enemy group's current room still has any bunnies to fight.")]
    public float enemyRoomAbandonCheckIntervalSeconds = 1.5f;
    [Tooltip("When NO room on the floor has any bunnies at all, an enemy group waits this long (randomized between min/max) before wandering to a random room, giving the player time to cross-floor-deploy a guard.")]
    public float enemyWanderWaitMinSeconds = 30f;
    public float enemyWanderWaitMaxSeconds = 60f;

    // Shared by RollEnemyLevel, GetInvasionWaitRangeSeconds, and RollInvasionGroupSize — all three ramp
    // off the same population/difficulty curve (levelRampStartPopulation/levelRampCapPopulation), just
    // applied to different output ranges. Same ramp shape as WildBunnySpawner.GetPopulationRampT, kept as
    // an independent implementation rather than sharing code with it since bunnies/enemies are separately-
    // tunable systems per the design doc.
    private float GetPopulationRampT()
    {
        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;

        float denominator = levelRampCapPopulation - levelRampStartPopulation;
        return denominator > 0f
            ? Mathf.Clamp01((population - levelRampStartPopulation) / denominator)
            : (population >= levelRampCapPopulation ? 1f : 0f);
    }

    // Rolled once per spawned enemy and never changes afterward, same "spawn-time-only level" rule as a
    // wild bunny's.
    public int RollEnemyLevel()
    {
        float t = GetPopulationRampT();
        int minLevel = Mathf.RoundToInt(Mathf.Lerp(levelRampStartMinLevel, levelRampCapMinLevel, t));
        int maxLevel = Mathf.RoundToInt(Mathf.Lerp(levelRampStartMaxLevel, levelRampCapMaxLevel, t));
        return Mathf.Clamp(Random.Range(minLevel, maxLevel + 1), 1, 50);
    }

    public void GetInvasionWaitRangeSeconds(out float minSeconds, out float maxSeconds)
    {
        float t = GetPopulationRampT();
        minSeconds = Mathf.Lerp(invasionStartMinWaitMinutes, invasionCapMinWaitMinutes, t) * 60f;
        maxSeconds = Mathf.Lerp(invasionStartMaxWaitMinutes, invasionCapMaxWaitMinutes, t) * 60f;
    }

    public int RollInvasionGroupSize()
    {
        float t = GetPopulationRampT();
        int minSize = Mathf.RoundToInt(Mathf.Lerp(invasionGroupSizeStartMin, invasionGroupSizeCapMin, t));
        int maxSize = Mathf.RoundToInt(Mathf.Lerp(invasionGroupSizeStartMax, invasionGroupSizeCapMax, t));
        return Mathf.Clamp(Random.Range(minSize, maxSize + 1), 1, 8);
    }

    public float RollEnemyWanderWaitSeconds()
    {
        return Random.Range(enemyWanderWaitMinSeconds, enemyWanderWaitMaxSeconds);
    }
}
