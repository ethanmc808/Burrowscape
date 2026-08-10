using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Explicit field wiring for the trait-picker row prefab used by BunnyInfoUI's "Adaptable" block (Neutral's
// self-only trait re-roll — see BunnyTypeSystem_DesignDoc.md / BunnyTypeNiches_DesignDoc.md). Same
// reasoning as FruitRowUI: explicit dragged references instead of name/position-based child lookup, which
// proved fragile to get right by hand in the Editor. No icon field — BunnyTraitDefinition has no icon/
// sprite of its own, unlike ForagingFruitDefinition.
public class TraitRerollRowUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public Button button;
}
