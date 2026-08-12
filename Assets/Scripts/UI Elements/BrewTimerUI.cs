using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Explicit field wiring for the Laboratory's per-bunny brew-progress display, shown inside AssignmentUI's
// Laboratory extras block (see AssignmentUI.RefreshLaboratoryExtras) while that bunny has an active
// LaboratoryRoom.BrewJob. Same reasoning as TraitRerollRowUI/FruitRowUI — explicit dragged references
// instead of name/position-based child lookup. fillBar must be an Image with Image.Type = Filled, same
// idiom BunnyInfoUI's hunger/thirst/energy/mood bars already use.
public class BrewTimerUI : MonoBehaviour
{
    public Image itemIcon;
    public Image fillBar;
    public TextMeshProUGUI timeRemainingLabel;
    public TextMeshProUGUI recipeNameLabel;
}
