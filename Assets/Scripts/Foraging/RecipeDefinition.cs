using System.Collections.Generic;
using UnityEngine;

// One herb cost line within a RecipeDefinition's ingredient list. `herb` is expected to be a
// ForagingMaterialDefinition with materialType == Herb (Cloth/Metal aren't valid Laboratory ingredients)
// — not type-enforced at the field level since ForagingMaterialDefinition is shared across all three
// families, so double-check materialType when authoring a recipe in the Inspector.
[System.Serializable]
public class RecipeIngredientCost
{
    public ForagingMaterialDefinition herb;
    public int amount = 1;
}

// The Laboratory's craftable-item catalog entry — what a bunny can brew, from what herbs, and how long it
// takes. See LaboratoryRoom (the job-flow that actually consumes these) and LaboratoryRecipeUnlockTracker
// (the discovery ledger gating which recipes show up in the "select item to craft" picker).
//
// `discovered` is the SEED value LaboratoryRecipeUnlockTracker.SeedAlreadyUnlocked() reads on startup —
// true for starter recipes the player already knows (Potion/Great Potion/Super Potion ship this way), false
// for anything meant to be found later via foraging/quests/special visitors (none of those grant recipes
// yet — LaboratoryRecipeUnlockTracker.DiscoverRecipe is scaffolding for when they do). Same "ships
// dormant-but-flippable" precedent as Neutral's Adaptable passive.
[CreateAssetMenu(fileName = "RecipeDefinition", menuName = "Burrowscape/Recipe Definition")]
public class RecipeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Output")]
    public ConsumableDefinition output;
    public int outputAmount = 1;

    [Header("Cost")]
    [Tooltip("Herb-type ForagingMaterialDefinitions only — see RecipeIngredientCost.")]
    public List<RecipeIngredientCost> ingredients = new List<RecipeIngredientCost>();

    [Header("Timing")]
    [Tooltip("Seconds to brew at Grade 1 with a non-recommended-type bunny. Reduced by the room's GradeMultiplier and LaboratoryRoom's Toxic type-match brew-speed bonus.")]
    public float baseBrewTime = 30f;

    [Header("Discovery")]
    [Tooltip("True for starter recipes the player already knows from the start. False for anything meant to be unlocked later (foraging find, quest reward, special visitor) via LaboratoryRecipeUnlockTracker.DiscoverRecipe.")]
    public bool discovered = false;
}
