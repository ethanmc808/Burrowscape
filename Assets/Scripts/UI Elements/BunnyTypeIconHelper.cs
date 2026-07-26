using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Shared by every "list a bunny by name" row (AssignmentUI's Assign/Unassign lists,
// ForagingScreenUI's picker, ForagingActiveTripRowUI) so the type icon looks and behaves the same
// everywhere: instantiated fresh alongside the row on every refresh (same Destroy+Instantiate
// lifecycle those rows already use), anchored to the row's left edge at nameText's own vertical
// position, then nameText's left edge is pushed over to make room. No-ops entirely — icon skipped,
// nameText left untouched — if the bunny's type has no icon authored yet (mirrors BunnyInfoUI's
// typeIconImage handling).
public static class BunnyTypeIconHelper
{
    private const float IconSize = 20f;
    private const float LeftPadding = 4f;
    private const float Spacing = 4f;

    public static void AddIcon(GameObject typeIconPrefab, Transform rowRoot, TextMeshProUGUI nameText, Sprite typeIcon)
    {
        if (typeIconPrefab == null || typeIcon == null || nameText == null) return;

        GameObject iconObj = Object.Instantiate(typeIconPrefab, rowRoot);
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.5f);
        iconRT.anchorMax = new Vector2(0f, 0.5f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.sizeDelta = new Vector2(IconSize, IconSize);
        iconRT.anchoredPosition = new Vector2(LeftPadding, nameText.rectTransform.anchoredPosition.y);

        Image iconImage = iconObj.GetComponent<Image>();
        if (iconImage != null) iconImage.sprite = typeIcon;

        nameText.alignment = TextAlignmentOptions.Left;
        RectTransform nameRT = nameText.rectTransform;
        nameRT.offsetMin = new Vector2(LeftPadding + IconSize + Spacing, nameRT.offsetMin.y);
    }
}
