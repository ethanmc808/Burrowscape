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
// Safe to re-run for asset CREATION: skips any asset that already exists at its target path, so
// re-running after tuning values in the Inspector won't stomp icon/description/sellValue/etc. edits —
// only genuinely missing assets get created. The one exception is `rarity` (see Rarity_DesignDoc.md):
// every run re-applies this generator's designated rarity to every item, existing or new, so the newly-
// introduced field gets backfilled onto the 20+ trinkets/fruits/accessories that predate it. Do your
// lasting rarity tuning via Burrowscape/Rarity Manager or the Inspector — just don't expect it to
// survive a re-run of this generator; remove the rarity re-assignment lines below once initial rollout
// is done if you want re-runs to leave rarity alone.
public static class ForagingDataGenerator
{
    private const string LocationsFolder = "Assets/Data/Foraging Locations";
    private const string AccessoriesFolder = "Assets/Data/Foraging Accessories";
    private const string TierConfigPath = "Assets/Data/Foraging Difficulty Tiers.asset";
    private const string KindRarityConfigPath = "Assets/Data/Foraging Kind Rarities.asset";
    private const string FruitsFolder = "Assets/Data/Foraging Fruits";
    private const string TrinketsFolder = "Assets/Data/Foraging Trinkets";
    private const string MaterialsFolder = "Assets/Data/Foraging Materials";

    [MenuItem("Burrowscape/Generate Foraging Data")]
    public static void Generate()
    {
        EnsureFolder("Assets/Data");
        EnsureFolder(LocationsFolder);
        EnsureFolder(AccessoriesFolder);
        EnsureFolder(FruitsFolder);
        EnsureFolder(TrinketsFolder);
        EnsureFolder(MaterialsFolder);

        int created = 0;

        if (GenerateTierConfig()) created++;
        if (GenerateKindRarityConfig()) created++;

        // Item Expansion content (Foraging Trip Detail Panel + Item Expansion design doc) — assets only,
        // not yet wired into every location's lootTable in every case (see the per-location tables below
        // for what IS wired, per Rarity_DesignDoc.md's "every location gets every tier" decision).
        // All 5 Fruits are fixed Rare rarity — Ethan's call, no per-fruit tiering yet.
        ForagingFruitDefinition peach = GenerateFruit("Peach", BunnyStatType.HP, ForagingLootRarity.Rare, ref created);
        ForagingFruitDefinition orange = GenerateFruit("Orange", BunnyStatType.Attack, ForagingLootRarity.Rare, ref created);
        ForagingFruitDefinition apple = GenerateFruit("Apple", BunnyStatType.Defense, ForagingLootRarity.Rare, ref created);
        ForagingFruitDefinition banana = GenerateFruit("Banana", BunnyStatType.Speed, ForagingLootRarity.Rare, ref created);
        GenerateFruit("Strawberry", BunnyStatType.Luck, ForagingLootRarity.Rare, ref created);

        // Materials are now one asset PER TIER (5 each), not one asset spanning all tiers via multiple
        // icon fields — see Rarity_DesignDoc.md. Common/Uncommon/Rare tiers reuse the exact icon sprites
        // already hand-assigned on the old single-asset Herb/Cloth/Metal (extracted by GUID below); the 2
        // new tiers get no icon yet since no art exists for them.
        GenerateMaterial("Mint Leaf", ForagingMaterialType.Herb, ForagingLootRarity.Common, "ad1f439d7c62e9c469555f3a6181d1d1", ref created);
        GenerateMaterial("Silverwort", ForagingMaterialType.Herb, ForagingLootRarity.Uncommon, "8a1fc95c53deca0428393b9bb20c12c7", ref created);
        GenerateMaterial("Moonpetal", ForagingMaterialType.Herb, ForagingLootRarity.Rare, "e7b0d81549e2b5e4685e5da966500362", ref created);
        GenerateMaterial("Sparkling Thistle", ForagingMaterialType.Herb, ForagingLootRarity.SuperRare, null, ref created);
        GenerateMaterial("Golden Lotus Petal", ForagingMaterialType.Herb, ForagingLootRarity.Mythical, null, ref created);

        GenerateMaterial("Coarse Cloth", ForagingMaterialType.Cloth, ForagingLootRarity.Common, "6cec2d46faaa5b3499124f648ee380cb", ref created);
        GenerateMaterial("Fine Cloth", ForagingMaterialType.Cloth, ForagingLootRarity.Uncommon, "69ee0af400ba691438def7a95b37b4cb", ref created);
        GenerateMaterial("Silken Cloth", ForagingMaterialType.Cloth, ForagingLootRarity.Rare, "aa7e44edbeafc4f4b9a62162b1e38b66", ref created);
        GenerateMaterial("Shimmering Cloth", ForagingMaterialType.Cloth, ForagingLootRarity.SuperRare, null, ref created);
        GenerateMaterial("Ethereal Cloth", ForagingMaterialType.Cloth, ForagingLootRarity.Mythical, null, ref created);

        GenerateMaterial("Scrap Metal", ForagingMaterialType.Metal, ForagingLootRarity.Common, "69acb2e67dc6b594d886fbd194bea46d", ref created);
        GenerateMaterial("Iron Ingot", ForagingMaterialType.Metal, ForagingLootRarity.Uncommon, "3da7f846f6a261440a7157f750f6a1a2", ref created);
        GenerateMaterial("Silver Ingot", ForagingMaterialType.Metal, ForagingLootRarity.Rare, "1e7aad12ebfc9e34fb94b42e83a66ad1", ref created);
        GenerateMaterial("Gold Ingot", ForagingMaterialType.Metal, ForagingLootRarity.SuperRare, null, ref created);
        GenerateMaterial("Starmetal Ingot", ForagingMaterialType.Metal, ForagingLootRarity.Mythical, null, ref created);

        // Draft 8/8/4/2/1 Common/Uncommon/Rare/SuperRare/Mythical split, craftsInto assignments per the
        // design doc's draft roster (extended by Rarity_DesignDoc.md for the 2 new tiers).
        (string name, TrinketCraftFamily craftsInto)[] commonTrinkets =
        {
            ("Ball of Wool", TrinketCraftFamily.Cloth), ("Frayed Ribbon", TrinketCraftFamily.Cloth),
            ("Bent Nail", TrinketCraftFamily.Metal), ("Rusted Washer", TrinketCraftFamily.Metal),
            ("Acorn Cap", TrinketCraftFamily.None), ("Smooth River Stone", TrinketCraftFamily.None),
            ("Dandelion Puff", TrinketCraftFamily.None), ("Dried Berry Cluster", TrinketCraftFamily.None),
        };
        (string name, TrinketCraftFamily craftsInto)[] uncommonTrinkets =
        {
            ("Woven Burlap Scrap", TrinketCraftFamily.Cloth), ("Patchwork Quilt Square", TrinketCraftFamily.Cloth),
            ("Tarnished Silver Spoon", TrinketCraftFamily.Metal), ("Bent Brass Key", TrinketCraftFamily.Metal),
            ("Polished Amber Chunk", TrinketCraftFamily.None), ("Mossy Pocket Watch", TrinketCraftFamily.None),
            ("Iridescent Beetle Shell", TrinketCraftFamily.None), ("Cracked Marble", TrinketCraftFamily.None),
        };
        (string name, TrinketCraftFamily craftsInto)[] rareTrinkets =
        {
            ("Golden Chalice", TrinketCraftFamily.Metal), ("Silken Tapestry Fragment", TrinketCraftFamily.Cloth),
            ("Starlight Dew Vial", TrinketCraftFamily.None), ("Fossilized Four-Leaf Clover", TrinketCraftFamily.None),
        };
        (string name, TrinketCraftFamily craftsInto)[] superRareTrinkets =
        {
            ("Sunken Coral Diadem", TrinketCraftFamily.None), ("Ancient Bronze Sundial", TrinketCraftFamily.Metal),
        };
        (string name, TrinketCraftFamily craftsInto)[] mythicalTrinkets =
        {
            ("Radiant Phoenix Down", TrinketCraftFamily.None),
        };

        foreach (var (name, craftsInto) in commonTrinkets) GenerateTrinket(name, craftsInto, ForagingLootRarity.Common, ref created);
        foreach (var (name, craftsInto) in uncommonTrinkets) GenerateTrinket(name, craftsInto, ForagingLootRarity.Uncommon, ref created);
        foreach (var (name, craftsInto) in rareTrinkets) GenerateTrinket(name, craftsInto, ForagingLootRarity.Rare, ref created);
        foreach (var (name, craftsInto) in superRareTrinkets) GenerateTrinket(name, craftsInto, ForagingLootRarity.SuperRare, ref created);
        foreach (var (name, craftsInto) in mythicalTrinkets) GenerateTrinket(name, craftsInto, ForagingLootRarity.Mythical, ref created);

        ForagingTrinketDefinition tarnishedSilverSpoon = LoadTrinket("Tarnished Silver Spoon");
        ForagingTrinketDefinition bentBrassKey = LoadTrinket("Bent Brass Key");
        ForagingTrinketDefinition sunkenCoralDiadem = LoadTrinket("Sunken Coral Diadem");
        ForagingTrinketDefinition ancientBronzeSundial = LoadTrinket("Ancient Bronze Sundial");
        ForagingTrinketDefinition radiantPhoenixDown = LoadTrinket("Radiant Phoenix Down");

        // All 3 Accessories are fixed Rare rarity — matches their sole existing usage in every location's
        // Rare loot band.
        ForagingAccessoryDefinition backpack = GenerateAccessory("Backpack", "+carry capacity", ForagingAccessoryEffectType.CarryCapacityBonus, 10f, ForagingLootRarity.Rare, ref created);
        ForagingAccessoryDefinition binoculars = GenerateAccessory("Binoculars", "+rare loot chance", ForagingAccessoryEffectType.RareLootChanceBonus, 10f, ForagingLootRarity.Rare, ref created);
        ForagingAccessoryDefinition lightningShoes = GenerateAccessory("Lightning Shoes", "+return speed", ForagingAccessoryEffectType.ReturnSpeedMultiplier, 0.5f, ForagingLootRarity.Rare, ref created);

        // Every location now has an entry at all 5 rarity bands (Rarity_DesignDoc.md's "all locations get
        // all tiers, for simplicity") — additions beyond the original Carrot/Potion/Accessory rows are
        // one new Fruit at Rare, and Trinket entries filling Uncommon/SuperRare/Mythical wherever a band
        // was previously empty. Thresholds staggered against BunnyTypeDefinition's 25/50/75/100/150/200
        // per the design doc's unlock table.
        if (GenerateLocation("Beach", 0, ForagingDifficultyTier.Weak,
            new List<BunnyType> { BunnyType.Water },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Carrot, minAmount = 1, maxAmount = 3 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = binoculars },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Fruit, fruit = peach, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.SuperRare, kind = ForagingLootKind.Trinket, trinket = sunkenCoralDiadem, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Mythical, kind = ForagingLootKind.Trinket, trinket = radiantPhoenixDown, minAmount = 1, maxAmount = 1 },
            },
            new List<string> { "a Crab", "a Seagull", "a Sand Worm" })) created++;

        if (GenerateLocation("Forest", 0, ForagingDifficultyTier.Weak,
            new List<BunnyType> { BunnyType.Plant },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Carrot, minAmount = 1, maxAmount = 3 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = backpack },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Fruit, fruit = orange, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.SuperRare, kind = ForagingLootKind.Trinket, trinket = ancientBronzeSundial, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Mythical, kind = ForagingLootKind.Trinket, trinket = radiantPhoenixDown, minAmount = 1, maxAmount = 1 },
            },
            new List<string> { "a Fox", "a Wasp", "a Wild Boar" })) created++;

        if (GenerateLocation("Dungeon", 90, ForagingDifficultyTier.Tough,
            new List<BunnyType> { BunnyType.Dark },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 2 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Trinket, trinket = tarnishedSilverSpoon, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = lightningShoes },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Fruit, fruit = apple, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.SuperRare, kind = ForagingLootKind.Trinket, trinket = sunkenCoralDiadem, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Mythical, kind = ForagingLootKind.Trinket, trinket = radiantPhoenixDown, minAmount = 1, maxAmount = 1 },
            },
            new List<string> { "a Skeleton", "a Giant Spider", "a Shadow Wisp" })) created++;

        if (GenerateLocation("Volcano", 150, ForagingDifficultyTier.Brutal,
            new List<BunnyType> { BunnyType.Fire },
            new List<ForagingLootEntry>
            {
                new ForagingLootEntry { rarity = ForagingLootRarity.Common, kind = ForagingLootKind.Potion, minAmount = 1, maxAmount = 2 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Uncommon, kind = ForagingLootKind.Trinket, trinket = bentBrassKey, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Accessory, accessory = backpack },
                new ForagingLootEntry { rarity = ForagingLootRarity.Rare, kind = ForagingLootKind.Fruit, fruit = banana, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.SuperRare, kind = ForagingLootKind.Trinket, trinket = ancientBronzeSundial, minAmount = 1, maxAmount = 1 },
                new ForagingLootEntry { rarity = ForagingLootRarity.Mythical, kind = ForagingLootKind.Trinket, trinket = radiantPhoenixDown, minAmount = 1, maxAmount = 1 },
            },
            new List<string> { "an Ember Hound", "a Magma Slug", "an Ash Wraith" })) created++;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"ForagingDataGenerator: created {created} new asset(s) (existing assets at the same paths were left untouched, except every item's `rarity` field which this generator always re-applies — see the header comment). " +
            "Remember to wire these scene references on ForagingInventoryManager in the Inspector: clothTiers[0..4] = Coarse/Fine/Silken/Shimmering/Ethereal Cloth, " +
            "metalTiers[0..4] = Scrap Metal/Iron/Silver/Gold/Starmetal Ingot, herbTiers[0..4] = Mint Leaf/Silverwort/Moonpetal/Sparkling Thistle/Golden Lotus Petal, " +
            "and kindRarities = the generated 'Foraging Kind Rarities' asset. Also assign icon art for the 4 new tiers with no sprite yet (Sparkling Thistle, Golden Lotus Petal, Gold/Starmetal Ingot, Shimmering/Ethereal Cloth) and for the 3 new trinkets. " +
            "None of the new Fruit/Trinket/Herb-kind loot entries beyond what's listed above are placed in any location's lootTable yet either — which items appear where beyond this pass is a separate authoring pass.");
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

    private static Sprite LoadSpriteByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static ForagingTrinketDefinition LoadTrinket(string displayName)
        => AssetDatabase.LoadAssetAtPath<ForagingTrinketDefinition>($"{TrinketsFolder}/{displayName}.asset");

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

    private static bool GenerateKindRarityConfig()
    {
        if (AssetDatabase.LoadAssetAtPath<ForagingKindRarityConfig>(KindRarityConfigPath) != null) return false;

        ForagingKindRarityConfig config = ScriptableObject.CreateInstance<ForagingKindRarityConfig>();
        config.carrotRarity = ForagingLootRarity.Common;
        config.potionRarity = ForagingLootRarity.Uncommon;
        config.crystalCarrotRarity = ForagingLootRarity.Rare;

        AssetDatabase.CreateAsset(config, KindRarityConfigPath);
        return true;
    }

    private static ForagingAccessoryDefinition GenerateAccessory(string displayName, string description, ForagingAccessoryEffectType effectType, float magnitude, ForagingLootRarity rarity, ref int createdCounter)
    {
        string path = $"{AccessoriesFolder}/{displayName}.asset";
        ForagingAccessoryDefinition existing = AssetDatabase.LoadAssetAtPath<ForagingAccessoryDefinition>(path);
        if (existing != null) { existing.rarity = rarity; EditorUtility.SetDirty(existing); return existing; }

        ForagingAccessoryDefinition accessory = ScriptableObject.CreateInstance<ForagingAccessoryDefinition>();
        accessory.displayName = displayName;
        accessory.description = description;
        accessory.effectType = effectType;
        accessory.magnitude = magnitude;
        accessory.rarity = rarity;

        AssetDatabase.CreateAsset(accessory, path);
        createdCounter++;
        return accessory;
    }

    private static ForagingFruitDefinition GenerateFruit(string displayName, BunnyStatType boostedStat, ForagingLootRarity rarity, ref int createdCounter)
    {
        string path = $"{FruitsFolder}/{displayName}.asset";
        ForagingFruitDefinition existing = AssetDatabase.LoadAssetAtPath<ForagingFruitDefinition>(path);
        if (existing != null) { existing.rarity = rarity; EditorUtility.SetDirty(existing); return existing; }

        ForagingFruitDefinition fruit = ScriptableObject.CreateInstance<ForagingFruitDefinition>();
        fruit.displayName = displayName;
        fruit.boostedStat = boostedStat;
        fruit.rarity = rarity;

        AssetDatabase.CreateAsset(fruit, path);
        createdCounter++;
        return fruit;
    }

    private static ForagingMaterialDefinition GenerateMaterial(string displayName, ForagingMaterialType materialType, ForagingLootRarity rarity, string iconGuid, ref int createdCounter)
    {
        string path = $"{MaterialsFolder}/{displayName}.asset";
        ForagingMaterialDefinition existing = AssetDatabase.LoadAssetAtPath<ForagingMaterialDefinition>(path);
        if (existing != null) { existing.rarity = rarity; EditorUtility.SetDirty(existing); return existing; }

        ForagingMaterialDefinition material = ScriptableObject.CreateInstance<ForagingMaterialDefinition>();
        material.displayName = displayName;
        material.materialType = materialType;
        material.rarity = rarity;
        material.icon = LoadSpriteByGuid(iconGuid);

        AssetDatabase.CreateAsset(material, path);
        createdCounter++;
        return material;
    }

    private static ForagingTrinketDefinition GenerateTrinket(string displayName, TrinketCraftFamily craftsInto, ForagingLootRarity rarity, ref int createdCounter)
    {
        string path = $"{TrinketsFolder}/{displayName}.asset";
        ForagingTrinketDefinition existing = AssetDatabase.LoadAssetAtPath<ForagingTrinketDefinition>(path);
        if (existing != null) { existing.rarity = rarity; EditorUtility.SetDirty(existing); return existing; }

        ForagingTrinketDefinition trinket = ScriptableObject.CreateInstance<ForagingTrinketDefinition>();
        trinket.displayName = displayName;
        trinket.craftsInto = craftsInto;
        trinket.rarity = rarity;

        AssetDatabase.CreateAsset(trinket, path);
        createdCounter++;
        return trinket;
    }

    private static bool GenerateLocation(string displayName, int populationThreshold, ForagingDifficultyTier tier,
        List<BunnyType> recommendedTypes, List<ForagingLootEntry> lootTable, List<string> enemyNames = null)
    {
        string path = $"{LocationsFolder}/{displayName}.asset";
        if (AssetDatabase.LoadAssetAtPath<ForagingLocationDefinition>(path) != null) return false;

        ForagingLocationDefinition location = ScriptableObject.CreateInstance<ForagingLocationDefinition>();
        location.displayName = displayName;
        location.populationThreshold = populationThreshold;
        location.difficultyTier = tier;
        location.recommendedTypes = recommendedTypes;
        location.lootTable = lootTable;
        location.enemyNames = enemyNames ?? new List<string>();

        AssetDatabase.CreateAsset(location, path);
        return true;
    }
}
