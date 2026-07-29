using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// One-time bulk data-entry tool for the bunny type system (see BunnyTypeSystem_DesignDoc.md at the
// project root). Seeds a BunnyTypeDefinition asset per BunnyType with the placeholder base stats from
// BunnyBaseStats.xlsx and the placeholder unlock group/population thresholds from that doc — hand-typing
// 20 assets' worth of the same 7 fields is exactly the repetitive, typo-prone task RoomDataGenerator
// (Editor/RoomDataGenerator.cs) already avoids for rooms, same reasoning applies here.
//
// Safe to re-run: matches existing assets by their `type` field (not by asset name), so re-running after
// editing stats via the Inspector or the BunnyBaseStatsWindow grid editor will NOT stomp those edits —
// only missing assets get created. To reset stats back to these placeholders, delete the asset first.
public static class BunnyDataGenerator
{
    private const string DefinitionsFolder = "Assets/Data/Bunny Types";
    private const string PrefabFolder = "Assets/Prefabs/Rabbits/NPC Bunnies";
    private const string IconFolder = "Assets/Art/Icons/TypeSymbols";

    private struct SeedData
    {
        public BunnyType type;
        public int group;
        public int populationThreshold;
        public int hp, attack, defense, speed, luck;

        public SeedData(BunnyType type, int group, int populationThreshold, int hp, int attack, int defense, int speed, int luck)
        {
            this.type = type;
            this.group = group;
            this.populationThreshold = populationThreshold;
            this.hp = hp;
            this.attack = attack;
            this.defense = defense;
            this.speed = speed;
            this.luck = luck;
        }
    }

    // Source: BunnyBaseStats.xlsx (stats) + BunnyTypeSystem_DesignDoc.md (groups/thresholds). Every
    // number here is a placeholder per the user — expect retuning once Group 1 is playtested.
    private static readonly SeedData[] Seeds =
    {
        new SeedData(BunnyType.Neutral, 1, 0,   70, 70, 70, 70, 20),
        new SeedData(BunnyType.Fire,    1, 0,   70, 130, 70, 100, 70),
        new SeedData(BunnyType.Water,   1, 0,   100, 80, 80, 60, 30),
        new SeedData(BunnyType.Plant,   1, 0,   130, 70, 80, 50, 20),
        new SeedData(BunnyType.Shock,   1, 0,   50, 110, 60, 130, 80),

        new SeedData(BunnyType.Insect,  2, 25,  50, 70, 90, 100, 70),
        new SeedData(BunnyType.Melee,   2, 25,  80, 150, 70, 70, 40),
        new SeedData(BunnyType.Stone,   2, 25,  90, 80, 130, 30, 60),

        new SeedData(BunnyType.Mind,    3, 50,  60, 110, 50, 60, 100),
        new SeedData(BunnyType.Toxic,   3, 50,  90, 90, 90, 60, 20),
        new SeedData(BunnyType.Ice,     3, 50,  90, 90, 120, 40, 20),

        new SeedData(BunnyType.Sound,   4, 75,  70, 80, 60, 120, 40),
        new SeedData(BunnyType.Air,     4, 75,  60, 70, 60, 150, 30),
        new SeedData(BunnyType.Earth,   4, 75,  110, 90, 100, 40, 20),

        new SeedData(BunnyType.Pixie,   5, 100, 110, 50, 50, 80, 120),
        new SeedData(BunnyType.Light,   5, 100, 60, 80, 50, 140, 90),

        new SeedData(BunnyType.Metal,   6, 150, 70, 80, 160, 30, 20),
        new SeedData(BunnyType.Ghost,   6, 150, 50, 90, 50, 110, 70),
        new SeedData(BunnyType.Dark,    6, 150, 70, 100, 60, 80, 80),

        new SeedData(BunnyType.Draco,   7, 200, 90, 140, 100, 80, 70),
    };

    [MenuItem("Burrowscape/Generate Bunny Type Definitions")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(DefinitionsFolder))
        {
            string parent = "Assets/Data";
            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", "Data");
            AssetDatabase.CreateFolder(parent, "Bunny Types");
        }

        List<BunnyTypeDefinition> existing = LoadAllDefinitions();

        int created = 0;
        int prefabsLinked = 0;
        int iconsLinked = 0;
        List<string> skippedPrefabs = new List<string>();
        List<string> skippedIcons = new List<string>();

        foreach (SeedData seed in Seeds)
        {
            BunnyTypeDefinition def = existing.FirstOrDefault(d => d != null && d.type == seed.type);
            bool isNew = def == null;

            if (isNew)
            {
                def = ScriptableObject.CreateInstance<BunnyTypeDefinition>();
                def.type = seed.type;
                def.displayName = seed.type.ToString();
                def.group = seed.group;
                def.populationThreshold = seed.populationThreshold;
                def.baseHP = seed.hp;
                def.baseAttack = seed.attack;
                def.baseDefense = seed.defense;
                def.baseSpeed = seed.speed;
                def.baseLuck = seed.luck;
                // Melee-vs-ranged roster per Combat_DesignDoc.md — Neutral/Melee are the only two
                // confirmed melee types so far; everything else defaults to ranged (false).
                def.isMelee = seed.type == BunnyType.Neutral || seed.type == BunnyType.Melee;

                string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DefinitionsFolder}/{seed.type}.asset");
                AssetDatabase.CreateAsset(def, assetPath);
                created++;
            }

            // Prefab linking runs for both new and existing assets (harmless to re-link the same
            // reference), so pointing an already-created definition at newly-added art later just
            // means re-running this tool rather than hand-wiring the field.
            if (def.prefab == null)
            {
                string prefabPath = $"{PrefabFolder}/{seed.type}_Type/NPC_Bunny_{seed.type}_Default.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    def.prefab = prefab;
                    EditorUtility.SetDirty(def);
                    prefabsLinked++;
                }
                else
                {
                    skippedPrefabs.Add($"{seed.type} (no prefab at {prefabPath} yet)");
                }
            }

            // Same convention as the prefab linking above: {IconFolder}/{Type}Icon.png, matching how
            // the 20 type-symbol sprites were actually named (FireIcon.png, WaterIcon.png, etc.).
            if (def.icon == null)
            {
                string iconPath = $"{IconFolder}/{seed.type}Icon.png";
                Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (icon != null)
                {
                    def.icon = icon;
                    EditorUtility.SetDirty(def);
                    iconsLinked++;
                }
                else
                {
                    skippedIcons.Add($"{seed.type} (no sprite at {iconPath} yet)");
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = $"BunnyDataGenerator: created {created} new BunnyTypeDefinition asset(s), linked {prefabsLinked} prefab reference(s), linked {iconsLinked} icon reference(s).";
        if (skippedPrefabs.Count > 0)
            summary += $"\nNo prefab yet for {skippedPrefabs.Count} type(s) (expected until their art ships):\n- {string.Join("\n- ", skippedPrefabs)}";
        if (skippedIcons.Count > 0)
            summary += $"\nNo icon yet for {skippedIcons.Count} type(s):\n- {string.Join("\n- ", skippedIcons)}";

        Debug.Log(summary);
    }

    private static List<BunnyTypeDefinition> LoadAllDefinitions()
    {
        if (!AssetDatabase.IsValidFolder(DefinitionsFolder)) return new List<BunnyTypeDefinition>();

        string[] guids = AssetDatabase.FindAssets("t:BunnyTypeDefinition", new[] { DefinitionsFolder });
        return guids.Select(g => AssetDatabase.LoadAssetAtPath<BunnyTypeDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
    }
}
