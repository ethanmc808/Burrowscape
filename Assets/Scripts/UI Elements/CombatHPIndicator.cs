using UnityEngine;
using UnityEngine.UI;

// Attach to each bunny prefab (mirrors BunnyStatsClickHandler's per-prefab placement). Visible for the
// bunny's whole combat engagement (bunny.IsDefending, true from BeginDefending until StopDefendingAndReturn
// — covers the walk to its CombatSpot, actual Defending, Fainted, AND melee's mid-fight re-flank walks) —
// combat naturally spaces bunnies out at CombatSpots, so a narrow radial fill here doesn't visually merge
// with a neighboring bunny's indicator the way an overlapping rectangular bar would, and idle/working
// bunnies never show one at all. Deliberately NOT gated on CurrentState == Defending/Fainted directly
// (confirmed bug 2026-08-04): a melee bunny calls MoveToFlankPosition every time its claimed target dies
// and it re-flanks a new one (HandleMeleeDefending), which flips CurrentState to MovingToSpot for that
// walk — with a state-only check the ring would blink off for every re-flank during a multi-enemy fight.
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
        Debug.Log($"[HPIndicatorDebug] {name} Awake — bunny={(bunny != null)}, indicatorPrefab={(indicatorPrefab != null)}, worldCamera={(worldCamera != null ? worldCamera.name : "NULL")}");
    }

    private bool wasShowing;

    private void LateUpdate()
    {
        // IsDefending (defendingRoom != null) alone covers Fainted too — TakeCombatDamage deliberately
        // leaves defendingRoom/defendingSpot set on faint (see its own comment) so revival/recall still works.
        bool shouldShow = bunny != null && bunny.IsDefending;

        if (shouldShow != wasShowing)
        {
            wasShowing = shouldShow;
            Debug.Log($"[HPIndicatorDebug] {name} shouldShow edge -> {shouldShow}, state={(bunny != null ? bunny.CurrentState.ToString() : "NULL")}, canvasInstance={(CombatIndicatorCanvas.Instance != null)}, indicatorPrefab={(indicatorPrefab != null)}, worldCamera={(worldCamera != null)}");
        }

        if (!shouldShow)
        {
            if (indicatorInstance != null) indicatorInstance.gameObject.SetActive(false);
            return;
        }

        if (!EnsureInstance())
        {
            Debug.Log($"[HPIndicatorDebug] {name} EnsureInstance FAILED — indicatorPrefab={(indicatorPrefab != null)}, canvasInstance={(CombatIndicatorCanvas.Instance != null)}, canvasActive={(CombatIndicatorCanvas.Instance != null && CombatIndicatorCanvas.Instance.gameObject.activeInHierarchy)}");
            return;
        }

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
        Debug.Log($"[HPIndicatorDebug] {name} EnsureInstance created new indicator — rect={(indicatorInstance != null)}, image={(fillImage != null)}, parent={instance.transform.parent?.name}, localScale={instance.transform.localScale}, activeInHierarchy={instance.activeInHierarchy}");
        return indicatorInstance != null && fillImage != null;
    }

    private void OnDestroy()
    {
        if (indicatorInstance != null) Destroy(indicatorInstance.gameObject);
    }
}
