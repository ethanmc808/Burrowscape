using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

// Standalone, toggleable balance-tuning overlay for Power/Water/Carrot production vs. consumption —
// gameplay-balance tooling, NOT the player-facing HUD. Deliberately separate from PowerCountDisplay/
// WaterCountDisplay/CarrotCountDisplay, which are untouched. Builds its own Canvas/TextMeshProUGUI at
// runtime, so it needs zero scene/prefab wiring: RuntimeBootstrap below auto-creates one at game start
// if nothing's manually placed (mirrors PowerManager.EnsureInstance's "check the scene first" shape, so
// a hand-placed instance with a tuned windowSizeMinutes is never silently duplicated).
//
// Samples each resource's production/consumption once per real-world second: Power reads the already-
// continuous ActiveProductionRate/TotalDemandRate directly (no accumulation needed, it's already a
// rate); Water/Carrots read-and-reset the producedThisSecond/consumedThisSecond buckets those managers
// accumulate via RecordProduction/RecordConsumption. Each sample feeds its own RollingMinuteAverage — 6
// total, production and consumption tracked independently per resource. See RollingMinuteAverage's
// header comment for why this is a sliding window of completed one-minute averages rather than a
// cumulative all-time average (short version: a cumulative average dilutes a fresh tuning change into
// dozens of old samples, which fights the entire point of a balance-tuning tool).
//
// Surplus/deficit-per-minute isn't its own tracked sample stream — it's just productionTracker.
// WindowAverage - consumptionTracker.WindowAverage at display time (mean of differences equals
// difference of means over the same set of minutes, so this is exact, not approximate).
public class ResourceBalanceDebugPanel : MonoBehaviour
{
    [SerializeField] private bool startVisible = false;
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;

    // Shared by all 6 trackers below so they stay in sync with each other.
    [SerializeField] private int windowSizeMinutes = 5;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RuntimeBootstrap()
    {
        if (FindAnyObjectByType<ResourceBalanceDebugPanel>() != null) return;
        GameObject go = new GameObject("ResourceBalanceDebugPanel (Auto)");
        go.AddComponent<ResourceBalanceDebugPanel>();
    }

    private RollingMinuteAverage powerProductionTracker;
    private RollingMinuteAverage powerConsumptionTracker;
    private RollingMinuteAverage waterProductionTracker;
    private RollingMinuteAverage waterConsumptionTracker;
    private RollingMinuteAverage carrotProductionTracker;
    private RollingMinuteAverage carrotConsumptionTracker;

    private float secondTimer;

    private GameObject panelRoot;
    private TextMeshProUGUI displayText;

    private void Awake()
    {
        powerProductionTracker = new RollingMinuteAverage(windowSizeMinutes);
        powerConsumptionTracker = new RollingMinuteAverage(windowSizeMinutes);
        waterProductionTracker = new RollingMinuteAverage(windowSizeMinutes);
        waterConsumptionTracker = new RollingMinuteAverage(windowSizeMinutes);
        carrotProductionTracker = new RollingMinuteAverage(windowSizeMinutes);
        carrotConsumptionTracker = new RollingMinuteAverage(windowSizeMinutes);

        BuildUI();
        panelRoot.SetActive(startVisible);
        RefreshDisplayText();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            panelRoot.SetActive(!panelRoot.activeSelf);

        // Sampling runs regardless of visibility — a rolling-average tool is only useful if the window
        // already reflects real recent history the moment you open it, not just however long it's
        // happened to be open this particular time. Only the (comparatively expensive) text rebuild below
        // is skipped while hidden.
        secondTimer += Time.deltaTime;
        while (secondTimer >= 1f)
        {
            secondTimer -= 1f;
            SampleOneSecond();
        }

        if (!panelRoot.activeSelf) return;

        RefreshDisplayText();
    }

    private void SampleOneSecond()
    {
        float powerProduction = PowerManager.Instance != null ? PowerManager.Instance.ActiveProductionRate : 0f;
        float powerDemand = PowerManager.Instance != null ? PowerManager.Instance.TotalDemandRate : 0f;
        powerProductionTracker.AddSecondSample(powerProduction);
        powerConsumptionTracker.AddSecondSample(powerDemand);

        int waterProduced = WaterManager.Instance != null ? WaterManager.Instance.ReadAndResetProducedThisSecond() : 0;
        int waterConsumed = WaterManager.Instance != null ? WaterManager.Instance.ReadAndResetConsumedThisSecond() : 0;
        waterProductionTracker.AddSecondSample(waterProduced);
        waterConsumptionTracker.AddSecondSample(waterConsumed);

        int carrotsProduced = CarrotManager.Instance != null ? CarrotManager.Instance.ReadAndResetProducedThisSecond() : 0;
        int carrotsConsumed = CarrotManager.Instance != null ? CarrotManager.Instance.ReadAndResetConsumedThisSecond() : 0;
        carrotProductionTracker.AddSecondSample(carrotsProduced);
        carrotConsumptionTracker.AddSecondSample(carrotsConsumed);
    }

    private void RefreshDisplayText()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"(window = {windowSizeMinutes} min, toggle = {toggleKey})");
        sb.AppendLine();

        float powerProdNow = PowerManager.Instance != null ? PowerManager.Instance.ActiveProductionRate : 0f;
        float powerDemandNow = PowerManager.Instance != null ? PowerManager.Instance.TotalDemandRate : 0f;
        float powerInstantNet = powerProdNow - powerDemandNow;
        float powerAvgNet = powerProductionTracker.WindowAverage - powerConsumptionTracker.WindowAverage;
        float rationingCurrent = PowerManager.Instance != null ? PowerManager.Instance.RationingPoolCurrent : 0f;
        float rationingMax = PowerManager.Instance != null ? PowerManager.Instance.RationingPoolMax : 0f;

        sb.AppendLine("<b>POWER</b>");
        sb.AppendLine($"  Instant/s:  prod {powerProdNow:F1}  demand {powerDemandNow:F1}  net {powerInstantNet:+0.0;-0.0}");
        sb.AppendLine($"  Avg/min:    prod {powerProductionTracker.WindowAverage:F1}  demand {powerConsumptionTracker.WindowAverage:F1}  net {powerAvgNet:+0.0;-0.0}");
        sb.AppendLine($"  Rationing Pool: {rationingCurrent:F1} / {rationingMax:F1}");
        sb.AppendLine();

        float waterAvgNet = waterProductionTracker.WindowAverage - waterConsumptionTracker.WindowAverage;
        int waterCurrent = WaterManager.Instance != null ? WaterManager.Instance.CurrentWater : 0;
        int waterMax = WaterManager.Instance != null ? WaterManager.Instance.WaterStorageMax : 0;
        float waterRationingCurrent = WaterRationingManager.Instance != null ? WaterRationingManager.Instance.RationingPoolCurrent : 0f;
        float waterRationingMax = WaterRationingManager.Instance != null ? WaterRationingManager.Instance.RationingPoolMax : 0f;

        sb.AppendLine("<b>WATER</b>");
        sb.AppendLine($"  Avg/min:  prod {waterProductionTracker.WindowAverage:F1}  consume {waterConsumptionTracker.WindowAverage:F1}  net {waterAvgNet:+0.0;-0.0}");
        sb.AppendLine($"  Stockpile (everyday supply): {waterCurrent} / {waterMax}");
        sb.AppendLine($"  Rationing Pool (emergency backup): {waterRationingCurrent:F1} / {waterRationingMax:F1}");
        sb.AppendLine();

        float carrotAvgNet = carrotProductionTracker.WindowAverage - carrotConsumptionTracker.WindowAverage;
        int carrotCurrent = CarrotManager.Instance != null ? CarrotManager.Instance.CurrentCarrots : 0;
        int carrotMax = CarrotManager.Instance != null ? CarrotManager.Instance.CarrotStorageMax : 0;

        sb.AppendLine("<b>CARROTS</b>");
        sb.AppendLine($"  Avg/min:  prod {carrotProductionTracker.WindowAverage:F1}  consume {carrotConsumptionTracker.WindowAverage:F1}  net {carrotAvgNet:+0.0;-0.0}");
        sb.AppendLine($"  Storage: {carrotCurrent} / {carrotMax}");

        displayText.text = sb.ToString();
    }

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("ResourceBalanceDebugCanvas");
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // draw above the normal HUD
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        const float headerHeight = 26f;

        panelRoot = new GameObject("Panel");
        panelRoot.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRect = panelRoot.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 0f);
        panelRect.pivot = new Vector2(0f, 0f);
        panelRect.anchoredPosition = new Vector2(20f, 20f);
        panelRect.sizeDelta = new Vector2(480f, 380f + headerHeight);

        Image background = panelRoot.AddComponent<Image>();
        background.color = Color.white;

        // Drag handle — a distinct strip along the top so click-dragging the panel doesn't compete with
        // selecting/scrolling the body text below it. Moves panelRect (see
        // ResourceBalanceDebugPanelDragHandler), not itself.
        GameObject headerGO = new GameObject("Header");
        headerGO.transform.SetParent(panelRoot.transform, false);
        RectTransform headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, headerHeight);

        Image headerBackground = headerGO.AddComponent<Image>();
        headerBackground.color = new Color(0.75f, 0.75f, 0.75f, 1f);

        GameObject headerLabelGO = new GameObject("HeaderLabel");
        headerLabelGO.transform.SetParent(headerGO.transform, false);
        RectTransform headerLabelRect = headerLabelGO.AddComponent<RectTransform>();
        headerLabelRect.anchorMin = Vector2.zero;
        headerLabelRect.anchorMax = Vector2.one;
        headerLabelRect.offsetMin = new Vector2(8f, 0f);
        headerLabelRect.offsetMax = new Vector2(-8f, 0f);
        TextMeshProUGUI headerLabel = headerLabelGO.AddComponent<TextMeshProUGUI>();
        headerLabel.text = "<b>RESOURCE BALANCE DEBUG</b>  (drag to move)";
        headerLabel.fontSize = 14f;
        headerLabel.color = Color.black;
        headerLabel.alignment = TextAlignmentOptions.MidlineLeft;
        headerLabel.raycastTarget = false; // let drags fall through to the header's Image, not get eaten by the label

        ResourceBalanceDebugPanelDragHandler dragHandler = headerGO.AddComponent<ResourceBalanceDebugPanelDragHandler>();
        dragHandler.Initialize(panelRect, canvas);

        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(panelRoot.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -12f - headerHeight);

        displayText = textGO.AddComponent<TextMeshProUGUI>();
        displayText.fontSize = 20f;
        displayText.color = Color.black;
        displayText.alignment = TextAlignmentOptions.TopLeft;
        displayText.enableWordWrapping = false;
    }
}
