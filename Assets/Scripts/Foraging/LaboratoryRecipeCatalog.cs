using System.Collections.Generic;
using UnityEngine;

// Base-wide list of every recipe that exists in the game (whether discovered yet or not) — plays the same
// role for recipes that ForagingManager.Locations plays for foraging locations. Deliberately separate from
// LaboratoryRoom itself so recipes stay shared/global even if multiple Laboratory rooms ever exist at once.
// Which of these are actually visible in a Laboratory's "select item to craft" picker is decided by
// LaboratoryRecipeUnlockTracker.IsDiscovered, not by this catalog.
public class LaboratoryRecipeCatalog : MonoBehaviour
{
    public static LaboratoryRecipeCatalog Instance { get; private set; }

    [SerializeField] private List<RecipeDefinition> allRecipes = new List<RecipeDefinition>();
    public IReadOnlyList<RecipeDefinition> AllRecipes => allRecipes;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
}
