using System.Collections.Generic;
using UnityEngine;

// Sticky discovery ledger for Laboratory recipes — once a recipe has ever been discovered it stays
// available in the "select item to craft" picker forever (same one-way semantics
// ForagingLocationUnlockTracker/BunnyTypeUnlockTracker already use for their own unlocks), which is also
// what satisfies "discovered or previously owned" as a single check — a recipe never un-discovers itself
// just because current herb stock runs out.
//
// Unlike ForagingLocationUnlockTracker, there's no live population-style trigger to react to — nothing in
// the game currently re-checks recipe discovery on its own. DiscoverRecipe is a plain callable hook,
// meant to be invoked by future systems (foraging finds, quest rewards, special visitors — none built yet,
// see RecipeDefinition/LaboratoryRoom design notes). SeedAlreadyUnlocked/SaveManager wiring exists now so
// those future call sites are correct on day one instead of needing another pass through this file later.
public class LaboratoryRecipeUnlockTracker : MonoBehaviour
{
    public static LaboratoryRecipeUnlockTracker Instance { get; private set; }

    private readonly HashSet<RecipeDefinition> discoveredRecipes = new HashSet<RecipeDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        SeedAlreadyUnlocked();
    }

    // Silently seeds discoveredRecipes from every RecipeDefinition.discovered == true entry in the catalog
    // (the starter recipes — Potion/Great Potion/Super Potion) — these were never genuinely "new" to the
    // player, so they must NOT fire a reveal notification. Public — also called explicitly by
    // SaveManager.LoadGame() right after it restores the real discoveredRecipes set
    // (ImportDiscoveredRecipeNames, via LoadUnlocks), same ordering reasoning
    // ForagingLocationUnlockTracker.SeedAlreadyUnlocked's own comment documents. Calling this twice is
    // harmless — HashSet.Add on an already-present entry is a no-op.
    public void SeedAlreadyUnlocked()
    {
        if (LaboratoryRecipeCatalog.Instance == null) return;
        foreach (RecipeDefinition recipe in LaboratoryRecipeCatalog.Instance.AllRecipes)
            if (recipe != null && recipe.discovered)
                discoveredRecipes.Add(recipe);
    }

    public bool IsDiscovered(RecipeDefinition recipe) => recipe != null && discoveredRecipes.Contains(recipe);

    // THE scaffolding hook — the only thing a future foraging-find/quest-reward/special-visitor system
    // needs to call to grant a recipe. Self-latching: safe to call repeatedly, only notifies once per
    // recipe. Nothing in the game calls this yet.
    public void DiscoverRecipe(RecipeDefinition recipe)
    {
        if (recipe == null || discoveredRecipes.Contains(recipe)) return;

        discoveredRecipes.Add(recipe);
        NotificationManager.Instance?.ShowWithIcon(NotificationType.RecipeDiscovered, recipe.icon, recipe.displayName);
    }

    // ---------- Save/load ----------
    // Saved by displayName (the HashSet holds direct asset references, which can't round-trip through
    // JSON) — resolved back against LaboratoryRecipeCatalog.Instance.AllRecipes on import, same source
    // Start() already reads from.
    public IEnumerable<string> ExportDiscoveredRecipeNames()
    {
        foreach (RecipeDefinition recipe in discoveredRecipes)
            if (recipe != null) yield return recipe.displayName;
    }

    public void ImportDiscoveredRecipeNames(IEnumerable<string> names)
    {
        discoveredRecipes.Clear();
        if (names == null || LaboratoryRecipeCatalog.Instance == null) return;

        HashSet<string> nameSet = new HashSet<string>(names);
        foreach (RecipeDefinition recipe in LaboratoryRecipeCatalog.Instance.AllRecipes)
        {
            if (recipe != null && nameSet.Contains(recipe.displayName))
                discoveredRecipes.Add(recipe);
        }
    }
}
