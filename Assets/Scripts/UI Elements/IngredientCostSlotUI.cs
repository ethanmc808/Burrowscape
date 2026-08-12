using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One herb-cost slot inside a RecipeRowUI's ingredientCostContainer — one instance per
// RecipeDefinition.ingredients entry, rebuilt every time the recipe picker refreshes (see
// AssignmentUI.RefreshRecipePickerList). countText is tinted white/red depending on whether the base
// currently has enough of that herb in stock.
public class IngredientCostSlotUI : MonoBehaviour
{
    public Image herbIcon;
    public TextMeshProUGUI countText;
}
