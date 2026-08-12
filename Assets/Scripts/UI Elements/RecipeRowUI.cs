using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One row per discovered recipe in the Laboratory's "select item to craft" picker (see
// AssignmentUI.RefreshRecipePickerList). No existing row prefab in this codebase shows more than one
// resource cost at once, so ingredientCostContainer holds a small rebuilt sub-list — one
// IngredientCostSlotUI instance per RecipeDefinition.ingredients entry — rather than a single cost field.
// Same explicit-field-wiring reasoning as TraitRerollRowUI/FruitRowUI.
public class RecipeRowUI : MonoBehaviour
{
    public Image recipeIcon;
    public TextMeshProUGUI recipeNameText;
    public Transform ingredientCostContainer;
    public Button selectButton;
}
