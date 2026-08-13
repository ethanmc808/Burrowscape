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

    // Screen Space - Overlay UI (indicatorPrefab's own RectTransform.sizeDelta) renders at a fixed PIXEL
    // size no matter how far the camera zooms — fine for CombatHPIndicator (combat is always viewed at
    // roughly the same zoom), but very visible here since a pregnancy can be checked on at any zoom level
    // (confirmed 2026-08-11: icon reads huge once zoomed far out). Fixed without touching the
    // architecture at all: give the icon a fixed WORLD size instead of a fixed screen size — every
    // LateUpdate, measure how many pixels ONE world unit currently spans right next to the bunny (works
    // for perspective OR orthographic, whatever zoom is actually driven by, since it just reads real
    // screen-space output rather than assuming a specific camera property), then size the icon to that
    // many pixels times worldSize. Tune worldSize by eye in the Inspector — same "hand-tune by feel, not
    // an auto-derived formula" approach this project already uses for e.g. per-type attack Base Power —
    // until it looks right at your normal play zoom; it'll then also be correctly sized at every other
    // zoom level, since it's now tracking real world scale instead of a fixed pixel count.
    [Tooltip("The icon's size in WORLD units (not pixels) — tune by eye until it looks right at your normal zoom. It'll then stay correctly sized at any zoom level, since size is now recomputed from real screen scale every frame instead of being a fixed pixel count. 0.8 is a rough starting estimate (back-computed from this scene's 60-degree-FOV camera at a typical bunny distance to land near the icon's old fixed ~40px size) — not a precise calibration, expect to nudge it.")]
    [SerializeField] private float worldSize = 0.8f;

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
        {
            Vector3 worldPos = transform.position + worldOffset;
            indicatorInstance.position = worldCamera.WorldToScreenPoint(worldPos);

            // Two-sample measurement: how many screen pixels does a 1-world-unit yardstick span right
            // here, right now — see worldSize's own comment for why this is the whole fix.
            Vector3 screenA = worldCamera.WorldToScreenPoint(worldPos);
            Vector3 screenB = worldCamera.WorldToScreenPoint(worldPos + worldCamera.transform.right);
            float pixelsPerWorldUnit = Vector3.Distance(screenA, screenB);

            indicatorInstance.sizeDelta = Vector2.one * (pixelsPerWorldUnit * worldSize);
        }
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
