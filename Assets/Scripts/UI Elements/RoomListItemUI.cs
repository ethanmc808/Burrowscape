using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

// Kept as its own component (rather than AssignmentUI's GetComponentInChildren<TextMeshProUGUI>()
// approach for its bunny buttons) since a room entry needs two distinct text fields — name and cost —
// not just one.
public class RoomListItemUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Image iconImage;

    public void Setup(RoomDefinition definition, Action onClick)
    {
        if (nameText != null) nameText.text = definition.displayName;
        if (costText != null) costText.text = definition.goldCost.ToString();
        if (iconImage != null)
        {
            iconImage.sprite = definition.icon;
            iconImage.enabled = definition.icon != null;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick());
    }
}
