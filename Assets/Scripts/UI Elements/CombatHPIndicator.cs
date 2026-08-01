using UnityEngine;
using UnityEngine.UI;

// Attach to each bunny prefab (mirrors BunnyStatsClickHandler's per-prefab placement). Visible only
// while CurrentState is Defending or Fainted — combat naturally spaces bunnies out at CombatSpots, so a
// narrow radial fill here doesn't visually merge with a neighboring bunny's indicator the way an
// overlapping rectangular bar would, and idle/working bunnies never show one at all.
//
// Instantiates a single shared indicatorPrefab (an Image, Type=Filled, Fill Method=Radial360 — one asset
// to author/tweak instead of hand-wiring a UI child onto every bunny type variant) under
// CombatIndicatorCanvas.Instance the first time it's needed, then repositions/refreshes it every
// LateUpdate — same WorldToScreenPoint + LateUpdate timing as BunnyApprovalUI (LateUpdate guarantees the
// camera has already finished panning/zooming for this frame before we anchor to it).
public class CombatHPIndicator : MonoBehaviour
{
    [SerializeField] private NPCBunny bunny;
    [SerializeField] private GameObject indicatorPrefab;
    [SerializeField] private Camera worldCamera;
    [Tooltip("Anchored near the feet per the design call (a ring around the bunny's feet), not above the head.")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, -0.5f, 0f);

    private RectTransform indicatorInstance;
    private Image fillImage;

    private void Awake()
    {
        if (bunny == null) bunny = GetComponent<NPCBunny>();
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void LateUpdate()
    {
        bool shouldShow = bunny != null
            && (bunny.CurrentState == BunnyState.Defending || bunny.CurrentState == BunnyState.Fainted);

        if (!shouldShow)
        {
            if (indicatorInstance != null) indicatorInstance.gameObject.SetActive(false);
            return;
        }

        if (!EnsureInstance()) return;

        indicatorInstance.gameObject.SetActive(true);
        if (worldCamera != null)
            indicatorInstance.position = worldCamera.WorldToScreenPoint(transform.position + worldOffset);

        int maxHP = bunny.Stats.HP;
        fillImage.fillAmount = maxHP > 0 ? (float)bunny.HPValue / maxHP : 0f;
    }

    // Lazily instantiated (not in Awake) so a bunny that never enters combat never spends a screen-space
    // UI element at all. Returns false if the shared prefab/canvas aren't wired up yet.
    private bool EnsureInstance()
    {
        if (indicatorInstance != null) return true;
        if (indicatorPrefab == null || CombatIndicatorCanvas.Instance == null) return false;

        GameObject instance = Instantiate(indicatorPrefab, CombatIndicatorCanvas.Instance.transform);
        indicatorInstance = instance.GetComponent<RectTransform>();
        fillImage = instance.GetComponent<Image>();
        return indicatorInstance != null && fillImage != null;
    }

    private void OnDestroy()
    {
        if (indicatorInstance != null) Destroy(indicatorInstance.gameObject);
    }
}
