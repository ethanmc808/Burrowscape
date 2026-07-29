using UnityEngine;
using UnityEditor;
using System.Linq;

// One-time bulk data-entry tool for base-invasion pest enemies (see EnemyDefinition.cs). Same reasoning
// as BunnyDataGenerator/ForagingDataGenerator: creating ScriptableObject assets by hand in the Inspector
// is repetitive and typo-prone, and this also needs to look up the matching BunnyTypeDefinition asset
// (for attackSource) by BunnyType, which is easiest done in code.
//
// Safe to re-run: matches existing assets by displayName, so re-running after tuning stats in the
// Inspector won't stomp those edits — only missing assets get created.
public static class EnemyDataGenerator
{
    private const string EnemiesFolder = "Assets/Data/Enemies";
    private const string BunnyTypesFolder = "Assets/Data/Bunny Types";

    [MenuItem("Burrowscape/Generate Enemy Definitions")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(EnemiesFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data"))
                AssetDatabase.CreateFolder("Assets", "Data");
            AssetDatabase.CreateFolder("Assets/Data", "Enemies");
        }

        int created = 0;

        // Slime: baseline/tutorial pest, weakest thing in the invasion roster. Toxic type so it exercises
        // the type chart (resists Plant, a starter type). Reuses the Toxic bunny's "Sludge Hurl" attack
        // identity instead of getting bespoke art. No basePower here — enemies level like bunnies now, so
        // Sludge Hurl's Base Power comes from CombatMath.GetBasePower(instanceLevel) at runtime, same as
        // any bunny's attack.
        GenerateEnemy("Slime", BunnyType.Toxic, hp: 40, attack: 25, defense: 15, speed: 15, luck: 10, ref created);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"EnemyDataGenerator: created {created} new EnemyDefinition asset(s).");
    }

    private static void GenerateEnemy(string displayName, BunnyType type, int hp, int attack, int defense, int speed, int luck, ref int created)
    {
        EnemyDefinition existing = LoadAllDefinitions().FirstOrDefault(d => d != null && d.displayName == displayName);
        if (existing != null) return;

        EnemyDefinition def = ScriptableObject.CreateInstance<EnemyDefinition>();
        def.displayName = displayName;
        def.type = type;
        def.baseHP = hp;
        def.baseAttack = attack;
        def.baseDefense = defense;
        def.baseSpeed = speed;
        def.baseLuck = luck;
        def.attackSource = FindBunnyTypeDefinition(type);

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{EnemiesFolder}/{displayName}.asset");
        AssetDatabase.CreateAsset(def, assetPath);
        created++;
    }

    private static BunnyTypeDefinition FindBunnyTypeDefinition(BunnyType type)
    {
        if (!AssetDatabase.IsValidFolder(BunnyTypesFolder)) return null;
        string[] guids = AssetDatabase.FindAssets("t:BunnyTypeDefinition", new[] { BunnyTypesFolder });
        return guids.Select(g => AssetDatabase.LoadAssetAtPath<BunnyTypeDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .FirstOrDefault(d => d != null && d.type == type);
    }

    private static System.Collections.Generic.List<EnemyDefinition> LoadAllDefinitions()
    {
        if (!AssetDatabase.IsValidFolder(EnemiesFolder)) return new System.Collections.Generic.List<EnemyDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { EnemiesFolder });
        return guids.Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
    }
}
