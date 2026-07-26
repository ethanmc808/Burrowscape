using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One row per currently-out bunny on ForagingScreenUI's status list — mirrors RoomListItemUI's Setup
// pattern (its own component rather than a bare Button+TextMeshProUGUI, since a row needs several
// distinct fields: name, location, HP/Energy/carry-fullness). Clicking anywhere on the row selects that
// bunny (highlighted via backgroundImage) — ForagingScreenUI owns a single shared Return button that
// acts on whichever row is currently selected, rather than each row having its own Recall button.
public class ForagingActiveTripRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI locationText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI carryText;
    [Tooltip("Small type-symbol icon prefab (TypeIconSmall) plugged in at the start of the bunny's name via BunnyTypeIconHelper. Skipped for types with no icon assigned yet.")]
    [SerializeField] private GameObject typeIconPrefab;
    [Tooltip("Button spanning the whole row — clicking anywhere on the row selects this bunny.")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalColor = Color.white;

    public void Setup(NPCBunny bunny, ForagingTripState trip, bool isSelected, Action onSelect)
    {
        if (nameText != null)
        {
            nameText.text = bunny.BunnyName;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, transform, nameText, bunny.TypeIcon);
        }
        if (locationText != null) locationText.text = trip.location != null ? trip.location.displayName : "";
        if (hpText != null) hpText.text = $"{bunny.HPValue}/{bunny.Stats.HP}";
        if (energyText != null) energyText.text = Mathf.RoundToInt(bunny.EnergyValue).ToString();
        if (carryText != null) carryText.text = $"{trip.CarriedItemCount}/{trip.carryCapacity}";

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => onSelect());
        SetSelected(isSelected);
    }

    public void SetSelected(bool selected)
    {
        if (backgroundImage != null) backgroundImage.color = selected ? selectedColor : normalColor;
    }
}
