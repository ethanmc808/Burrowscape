using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Explicit field wiring for the FruitRow prefab (see FruitFeeding_DesignDoc.md). Replaces name/position
// -based child lookup in BunnyInfoUI, which proved fragile to get exactly right by hand in the Editor —
// a stray Image nested in the wrong place was silently picked up instead of the intended icon. Add this
// component to the FruitRow prefab's root and drag its own 4 children into these fields once; there's
// nothing left to search for at runtime.
public class FruitRowUI : MonoBehaviour
{
    public Image icon;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statText;
    public Button button;
}
