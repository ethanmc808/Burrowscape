using UnityEngine;
using UnityEditor;

// One-time (and safe-to-re-run) seeding tool for the first real batch of trait content — see
// BunnyTypeSystem_DesignDoc.md. Mirrors BunnyDataGenerator's approach for BunnyTypeDefinition assets,
// but BunnyTraitCatalog isn't asset-backed (its trait list lives on a scene component), so this writes
// directly into whichever BunnyTraitCatalog is in the currently open scene via SerializedObject.
//
// Matches existing entries by `id` and updates them in place rather than duplicating, so re-running
// after tweaking a multiplier by hand in the Inspector won't fight you — only missing entries get
// appended. Combat/quest traits aren't seeded here yet; those are still just names, no effects,
// pending playtesting per the user.
public static class BunnyTraitDataGenerator
{
    private struct SeedTrait
    {
        public string id;
        public string displayName;
        public string description;
        public TraitEffectType effectType;
        public float effectMultiplier;
        public string[] incompatibleIds;

        public SeedTrait(string id, string displayName, string description, TraitEffectType effectType, float effectMultiplier, params string[] incompatibleIds)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            this.effectType = effectType;
            this.effectMultiplier = effectMultiplier;
            this.incompatibleIds = incompatibleIds;
        }
    }

    // Multiplier values and incompatible pairs straight from the user's spec.
    private static readonly SeedTrait[] Seeds =
    {
        new SeedTrait("energetic", "Energetic", "Energy decays 0.75x as slowly as normal.", TraitEffectType.EnergyDecayMultiplier, 0.75f, "lazy"),
        new SeedTrait("lazy", "Lazy", "Energy decays 1.25x faster than normal.", TraitEffectType.EnergyDecayMultiplier, 1.25f, "energetic"),

        new SeedTrait("diligent", "Diligent", "Produces 1.25x more than normal.", TraitEffectType.ProductionMultiplier, 1.25f, "slacker"),
        new SeedTrait("slacker", "Slacker", "Produces 0.75x as much as normal.", TraitEffectType.ProductionMultiplier, 0.75f, "diligent"),

        new SeedTrait("cheerful", "Cheerful", "Mood decays 0.75x as slowly as normal.", TraitEffectType.MoodDecayMultiplier, 0.75f, "grumpy"),
        new SeedTrait("grumpy", "Grumpy", "Mood decays 1.25x faster than normal.", TraitEffectType.MoodDecayMultiplier, 1.25f, "cheerful"),

        new SeedTrait("glutton", "Glutton", "Hunger decays 1.25x faster than normal.", TraitEffectType.HungerDecayMultiplier, 1.25f, "light_eater"),
        new SeedTrait("light_eater", "Light Eater", "Hunger decays 0.75x as slowly as normal.", TraitEffectType.HungerDecayMultiplier, 0.75f, "glutton"),

        new SeedTrait("parched", "Parched", "Thirst decays 1.25x faster than normal.", TraitEffectType.ThirstDecayMultiplier, 1.25f, "reservoir"),
        new SeedTrait("reservoir", "Reservoir", "Thirst decays 0.75x as slowly as normal.", TraitEffectType.ThirstDecayMultiplier, 0.75f, "parched"),

        new SeedTrait("quick_footed", "Quick-Footed", "Moves 1.25x faster than normal.", TraitEffectType.MoveSpeedMultiplier, 1.25f, "sluggish"),
        new SeedTrait("sluggish", "Sluggish", "Moves 0.75x as fast as normal.", TraitEffectType.MoveSpeedMultiplier, 0.75f, "quick_footed"),

        // Bunny Stat System Redesign (2026-07-23): suppresses this bunny's Zodiac (Nature) stat
        // modifier entirely — see NPCBunny.ApplyNatureEffects. effectMultiplier is unused by
        // IgnoresNature (kept at 1f as a neutral placeholder since every SeedTrait needs one). No
        // incompatible traits — Stoic doesn't oppose any of the rate-based traits above.
        new SeedTrait("stoic", "Stoic", "Ignores this bunny's Zodiac (Nature) stat modifier entirely.", TraitEffectType.IgnoresNature, 1f),

        // Work Room XP system (2026-07-27): applies to XP from every source (Foraging, Work Rooms, and
        // any future source) — see WorkRoomXP_DesignDoc.md.
        new SeedTrait("smart", "Smart", "Gains 1.25x XP from all sources.", TraitEffectType.XPGainMultiplier, 1.25f, "dumb"),
        new SeedTrait("dumb", "Dumb", "Gains 0.75x XP from all sources.", TraitEffectType.XPGainMultiplier, 0.75f, "smart"),
    };

    [MenuItem("Burrowscape/Generate Bunny Trait Seed Data")]
    public static void Generate()
    {
        BunnyTraitCatalog catalog = Object.FindAnyObjectByType<BunnyTraitCatalog>();
        if (catalog == null)
        {
            Debug.LogWarning("BunnyTraitDataGenerator: no BunnyTraitCatalog found in the open scene. Add Component > BunnyTraitCatalog to a persistent GameObject first, then re-run.");
            return;
        }

        SerializedObject so = new SerializedObject(catalog);
        SerializedProperty traitsProp = so.FindProperty("traits");

        int created = 0;
        int updated = 0;

        foreach (SeedTrait seed in Seeds)
        {
            int index = FindTraitIndex(traitsProp, seed.id);
            if (index < 0)
            {
                index = traitsProp.arraySize;
                traitsProp.arraySize++;
                created++;
            }
            else
            {
                updated++;
            }

            SerializedProperty element = traitsProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = seed.id;
            element.FindPropertyRelative("displayName").stringValue = seed.displayName;
            element.FindPropertyRelative("description").stringValue = seed.description;
            element.FindPropertyRelative("effectType").enumValueIndex = (int)seed.effectType;
            element.FindPropertyRelative("effectMultiplier").floatValue = seed.effectMultiplier;

            SerializedProperty incompatibleProp = element.FindPropertyRelative("incompatibleTraitIds");
            incompatibleProp.arraySize = seed.incompatibleIds.Length;
            for (int i = 0; i < seed.incompatibleIds.Length; i++)
                incompatibleProp.GetArrayElementAtIndex(i).stringValue = seed.incompatibleIds[i];
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);

        Debug.Log($"BunnyTraitDataGenerator: created {created} new trait(s), updated {updated} existing one(s) on '{catalog.name}'. Remember to save the scene.");
    }

    private static int FindTraitIndex(SerializedProperty traitsProp, string id)
    {
        for (int i = 0; i < traitsProp.arraySize; i++)
        {
            if (traitsProp.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id)
                return i;
        }
        return -1;
    }
}
