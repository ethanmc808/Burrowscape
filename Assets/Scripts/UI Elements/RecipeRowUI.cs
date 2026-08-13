using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One row per discovered recipe in the Laboratory's "select item to craft" picker (see
// AssignmentUI.RefreshRecipePickerList). Assumes every recipe costs some quantity of exactly ONE herb tier
// (true for all 3 starter recipes, and confirmed with Ethan as a permanent assumption rather than
// speculative future-proofing) — a fixed ingredientIcon/ingredientCountText pair, no runtime-instantiated
// sub-list. RecipeDefinition.ingredients stays a List<RecipeIngredientCost> for now (existing authored
// assets untouched), but the UI only ever reads index 0. Same explicit-field-wiring reasoning as
// TraitRerollRowUI/FruitRowUI.
public class RecipeRowUI : MonoBehaviour
{
    public Image recipeIcon;
    public TextMeshProUGUI recipeNameText;
    public Image ingredientIcon;
    public TextMeshProUGUI ingredientCountText;
    public Button selectButton;
}
