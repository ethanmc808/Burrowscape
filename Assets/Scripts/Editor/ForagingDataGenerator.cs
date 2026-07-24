using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

// One-time bulk data-entry tool for the Foraging system (see Foraging_DesignDoc.md at the project root)
// — generates the example ScriptableObject assets named as concrete examples in that doc:
// ForagingDifficultyTierConfig (one asset, 4 tiers), the Backpack/Binoculars/Lightning Shoes accessories
// referenced by the example loot tables, and Beach/Forest/Dungeon/Volcano ForagingLocationDefinitions.
// Same reasoning as BunnyDataGenerator/RoomDataGenerator: hand-building this many ScriptableObject
// assets in the Inspector is repetitive and typo-prone.
//
// Safe to re-run: skips any asset that already exists at its target path, so re-running after tuning
// values in the Inspector won't stomp those edits — only genuinely missing assets get created. Every
// number here is a placeholder, same "expect retuning once playtested" caveat as BunnyDataGenerator's
// seed stats.
public static class ForagingDataGenerator
{
    private const string LocationsFolder = "Assets/Data/Foraging Locations";
    private const string AccessoriesFolder = "Assets/Data/Foraging Accessories";
    private const string TierConfigPath = "Assets/Data/Foraging Difficulty Tiers.asset";

    [MenuItem("Burrowscape/Generate Foraging Data")]
    public static void Generate()
    {
        EnsureFolder("Assets/Data");
        EnsureFolder(LocationsFolder);
        EnsureFolder(AccessoriesFolder);

        int created = 0;

        if (GenerateTierConfig()) created++;

        ForagingAccessoryDefinition backpack = GenerateAccessory("Backpack", "+carry capacity", ForagingAccessoryEffectType.CarryCapacityBonus, 10f, ref created);
        ForagingAccessoryDefinition binoculars = GenerateAccessory("Binoculars", "+rare loot chance", ForagingAccessoryEffectType.RareLootChanceBonus, 10f, ref created);
        ForagingAccessoryDefinition lightningShoes = GenerateAccessory("Lightning Shoes", "+return speed", ForagingAccessoryEffectType.ReturnSpeedMultiplier, 0.5f, ref created);

        // Beach/Forest — starter tier (populationThreshold 0), Carrots findable. Dungeon/Volcano — later
        // tiers, Carrots genuinely absent from the table entirely, not just rare. Thresholds staggered
        // against BunnyTypeDefinition's 25/50/75/100/150/200 per the design doc's unlock table.
        if (GenerateLocation("Beach", 0, ForagingDifficultyTier.Weak,
            new List<BunnyType> { BunnyType.Water },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Carrot, minAmount = 1, maxAmount = 3 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = binoculars },
            })) created++;

        if (GenerateLocation("Forest", 0, ForagingDifficultyTier.Weak,
            new List<BunnyType> { BunnyType.Plant },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Carrot, minAmount = 1, maxAmount = 3 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = backpack },
            })) created++;

        if (GenerateLocation("Dungeon", 90, ForagingDifficultyTier.Tough,
            new List<BunnyType> { BunnyType.Dark },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 2 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = lightningShoes },
            })) created++;

        if (GenerateLocation("Volcano", 150, ForagingDifficultyTier.Brutal,
            new List<BunnyType> { BunnyType.Fire },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 2 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = backpack },
            })) created++;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"ForagingDataGenerator: created {created} new asset(s) (existing assets at the same paths were left untouched).");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent)) return;

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static bool GenerateTierConfig()
    {
        if (AssetDatabase.LoadAssetAtPath<ForagingDifficultyTierConfig>(TierConfigPath) != null) return false;

        ForagingDifficultyTierConfig config = ScriptableObject.CreateInstance<ForagingDifficultyTierConfig>();
        config.tiers = new List<ForagingTierData>
        {
            new ForagingTierData
            {
                tier = ForagingDifficultyTier.Weak, enemyEffectivePower = 8, minDamageOnLoss = 2, maxDamageOnLoss = 5,
                xpPerEnemyDefeated = 4f, xpPerGateCheckPassed = 4f,
                attackCheck = new StatCheckRange { floor = 5, ceiling = 25 },
                defenseCheck = new StatCheckRange { floor = 5, ceiling = 25 },
                speedCheck = new StatCheckRange { floor = 5, ceiling = 25 },
                luckCheck = new StatCheckRange { floor = 5, ceiling = 25 },
                goldFindChance = 0.15f, minGoldPerFind = 1, maxGoldPerFind = 3,
            },
            new ForagingTierData
            {
                tier = ForagingDifficultyTier.Average, enemyEffectivePower = 15, minDamageOnLoss = 4, maxDamageOnLoss = 9,
                xpPerEnemyDefeated = 7f, xpPerGateCheckPassed = 7f,
                attackCheck = new StatCheckRange { floor = 10, ceiling = 35 },
                defenseCheck = new StatCheckRange { floor = 10, ceiling = 35 },
                speedCheck = new StatCheckRange { floor = 10, ceiling = 35 },
                luckCheck = new StatCheckRange { floor = 10, ceiling = 35 },
                goldFindChance = 0.15f, minGoldPerFind = 2, maxGoldPerFind = 6,
            },
            new ForagingTierData
            {
                tier = ForagingDifficultyTier.Tough, enemyEffectivePower = 26, minDamageOnLoss = 7, maxDamageOnLoss = 14,
                xpPerEnemyDefeated = 12f, xpPerGateCheckPassed = 12f,
                attackCheck = new StatCheckRange { floor = 18, ceiling = 45 },
                defenseCheck = new StatCheckRange { floor = 18, ceiling = 45 },
                speedCheck = new StatCheckRange { floor = 18, ceiling = 45 },
                luckCheck = new StatCheckRange { floor = 18, ceiling = 45 },
                goldFindChance = 0.15f, minGoldPerFind = 4, maxGoldPerFind = 10,
            },
            new ForagingTierData
            {
                tier = ForagingDifficultyTier.Brutal, enemyEffectivePower = 42, minDamageOnLoss = 10, maxDamageOnLoss = 22,
                xpPerEnemyDefeated = 20f, xpPerGateCheckPassed = 20f,
                attackCheck = new StatCheckRange { floor = 28, ceiling = 60 },
                defenseCheck = new StatCheckRange { floor = 28, ceiling = 60 },
                speedCheck = new StatCheckRange { floor = 28, ceiling = 60 },
                luckCheck = new StatCheckRange { floor = 28, ceiling = 60 },
                goldFindChance = 0.15f, minGoldPerFind = 8, maxGoldPerFind = 18,
            },
        };

        AssetDatabase.CreateAsset(config, TierConfigPath);
        return true;
    }

    private static ForagingAccessoryDefinition GenerateAccessory(string displayName, string description, ForagingAccessoryEffectType effectType, float magnitude, ref int createdCounter)
    {
        string path = $"{AccessoriesFolder}/{displayName}.asset";
        ForagingAccessoryDefinition existing = AssetDatabase.LoadAssetAtPath<ForagingAccessoryDefinition>(path);
        if (existing != null) return existing;

        ForagingAccessoryDefinition accessory = ScriptableObject.CreateInstance<ForagingAccessoryDefinition>();
        accessory.displayName = displayName;
        accessory.description = description;
        accessory.effectType = effectType;
        accessory.magnitude = magnitude;

        AssetDatabase.CreateAsset(accessory, path);
        createdCounter++;
        return accessory;
    }

    private static bool GenerateLocation(string displayName, int populationThreshold, ForagingDifficultyTier tier,
        List<BunnyType> recommendedTypes, List<ForagingLootEntry> lootTable)
    {
        string path = $"{LocationsFolder}/{displayName}.asset";
        if (AssetDatabase.LoadAssetAtPath<ForagingLocationDefinition>(path) != null) return false;

        ForagingLocationDefinition location = ScriptableObject.CreateInstance<ForagingLocationDefinition>();
        location.displayName = displayName;
        location.populationThreshold = populationThreshold;
        location.difficultyTier = tier;
        location.recommendedTypes = recommendedTypes;
        location.lootTable = lootTable;

        AssetDatabase.CreateAsset(location, path);
        return true;
    }
}
