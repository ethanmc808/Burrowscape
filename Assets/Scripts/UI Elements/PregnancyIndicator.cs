using UnityEngine;
using UnityEngine.UI;

// Attach to each bunny prefab (mirrors CombatHPIndicator's per-prefab placement, same shared-canvas
// pattern). Visible for the bunny's whole pregnancy — from NPCBunny.BeginPregnancy until TryLayEgg clears
// it via ClearPregnancyState — so it naturally covers the full window regardless of what she's doing
// (working, relaxing, mid-mating-sequence). No fill/amount needed here (unlike CombatHPIndicator's HP
// ring) — just a static icon shown/hidden based on IsPregnant.
//
// Instantiates a single shared indicatorPrefab (a plain Image, a dedicated pregnancy icon) under
// CombatIndicatorCanvas.Instance the first time it's needed, then repositions/refreshes it every
// LateUpdate — same WorldToScreenPoint + LateUpdate timing as CombatHPIndicator (LateUpdate guarantees
// the camera has already finished panning/zooming for this frame before we anchor to it).
public class PregnancyIndicator : MonoBehaviour
{
    [SerializeField] private NPCBunny bunny;
    [SerializeField] private GameObject indicatorPrefab;
    [SerializeField] private Camera worldCamera;
    [Tooltip("Anchored ABOVE the head — deliberately the opposite of CombatHPIndicator's feet-level ring, so the two never collide if a bunny is ever ready to show both.")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.6f, 0f);

    private RectTransform indicatorInstance;

    private void Awake()
    {
        if (bunny == null) bunny = GetComponent<NPCBunny>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void LateUpdate()
    {
        bool shouldShow = bunny != null && bunny.IsPregnant;

        if (!shouldShow)
        {
            if (indicatorInstance != null) indicatorInstance.gameObject.SetActive(false);
            return;
        }

        if (!EnsureInstance()) return;

        indicatorInstance.gameObject.SetActive(true);
        if (worldCamera != null)
            indicatorInstance.position = worldCamera.WorldToScreenPoint(transform.position + worldOffset);
    }

    // Lazily instantiated (not in Awake) so a bunny that never gets pregnant (males, every other type
    // room's workers) never spends a screen-space UI element at all — same reasoning as CombatHPIndicator.
    private bool EnsureInstance()
    {
        if (indicatorInstance != null) return true;
        if (indicatorPrefab == null || CombatIndicatorCanvas.Instance == null) return false;

        GameObject instance = Instantiate(indicatorPrefab, CombatIndicatorCanvas.Instance.transform);
        indicatorInstance = instance.GetComponent<RectTransform>();
        return indicatorInstance != null;
    }

    private void OnDestroy()
    {
        if (indicatorInstance != null) Destroy(indicatorInstance.gameObject);
    }
}
