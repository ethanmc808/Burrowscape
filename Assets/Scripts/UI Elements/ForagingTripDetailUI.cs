using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Detail readout for whichever bunny is currently selected in ForagingScreenUI's active-trips list — see
// the Foraging Trip Detail Panel + Item Expansion design doc. Show() just captures the bunny/trip
// references; both are the same live objects for the whole trip, so this reads them directly on its own
// throttled timer rather than needing ForagingScreenUI to push refreshes (mirrors
// ForagingScreenUI.listRefreshInterval's own reasoning).
public class ForagingTripDetailUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;

    [Header("Identity")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI locationText;
    [Tooltip("Bound to ForagingPreviewStage's RenderTexture in the Inspector — a live view of a decorative walking duplicate, purely cosmetic.")]
    [SerializeField] private RawImage previewImage;

    [Header("Stats")]
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI enemiesSlainText;

    [Header("Accessory Equipped")]
    [SerializeField] private Image equippedAccessoryIcon;
    [SerializeField] private TextMeshProUGUI equippedAccessoryText;

    [Header("Items Found (generic icon+name+count row list)")]
    [Tooltip("Fixed icons for the 3 kinds with no per-find ScriptableObject icon of their own. Herb is NOT here — it reads its tier's own icon straight off ForagingInventoryManager.GetHerbForRarity instead.")]
    [SerializeField] private Sprite carrotIcon;
    [SerializeField] private Sprite potionIcon;
    [SerializeField] private Sprite crystalCarrotIcon;
    [Tooltip("Cleared and rebuilt every refresh — one row per distinct item kind actually found this trip, covering up to 7 kinds (Carrot/Potion/CrystalCarrot/Fruit/Trinket/Herb/Accessory).")]
    [SerializeField] private Transform foundItemsContainer;
    [Tooltip("Simple prefab: an Image (icon) + a TextMeshProUGUI child (name/count).")]
    [SerializeField] private GameObject foundItemRowPrefab;

    [Header("Log (last 10 events, newest first)")]
    [SerializeField] private TextMeshProUGUI logText;

    [Tooltip("How often this panel re-reads bunny/trip while open — throttled rather than every frame, same reasoning as ForagingScreenUI.listRefreshInterval.")]
    [SerializeField] private float refreshInterval = 0.5f;

    private NPCBunny currentBunny;
    private ForagingTripState currentTrip;
    private float refreshTimer;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Show(NPCBunny bunny, ForagingTripState trip)
    {
        currentBunny = bunny;
        currentTrip = trip;

        bool valid = bunny != null && trip != null;
        if (panelRoot != null) panelRoot.SetActive(valid);

        ForagingPreviewStage.Instance?.ShowBunny(bunny);

        refreshTimer = 0f;
        if (valid) Refresh();
    }

    public void Hide()
    {
        currentBunny = null;
        currentTrip = null;
        if (panelRoot != null) panelRoot.SetActive(false);
        ForagingPreviewStage.Instance?.ClearBunny();
    }

    private void Update()
    {
        if (panelRoot == null || !panelRoot.activeSelf) return;
        // Bunny returned/despawned or trip ended without going through ForagingScreenUI's own Hide call.
        if (currentBunny == null || currentTrip == null) { Hide(); return; }

        refreshTimer += Time.deltaTime;
        if (refreshTimer < refreshInterval) return;
        refreshTimer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (currentBunny == null || currentTrip == null) return;

        if (nameText != null) nameText.text = currentBunny.BunnyName;
        if (locationText != null) locationText.text = currentTrip.location != null ? currentTrip.location.displayName : "";

        if (hpText != null) hpText.text = $"{currentBunny.HPValue}/{currentBunny.Stats.HP}";
        if (energyText != null) energyText.text = Mathf.RoundToInt(currentBunny.EnergyValue).ToString();
        if (goldText != null) goldText.text = currentTrip.carriedGold.ToString();
        if (enemiesSlainText != null) enemiesSlainText.text = currentTrip.enemiesSlain.ToString();

        if (timeText != null)
        {
            int totalSeconds = Mathf.FloorToInt(currentTrip.elapsedTripTime);
            timeText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        RefreshEquippedAccessory();
        RefreshFoundItems();
        RefreshLog();
    }

    private void RefreshEquippedAccessory()
    {
        ForagingAccessoryDefinition accessory = currentTrip.equippedAccessory;

        if (equippedAccessoryText != null)
            equippedAccessoryText.text = accessory != null ? accessory.displayName : "None";

        if (equippedAccessoryIcon != null)
        {
            equippedAccessoryIcon.sprite = accessory != null ? accessory.icon : null;
            equippedAccessoryIcon.enabled = accessory != null && accessory.icon != null;
        }
    }

    // Generic icon+count row list, not fixed per-kind fields — needs to cover up to 7 distinct kinds with
    // only whatever was actually found this trip shown (see the design doc's "Current Items Found"
    // section, which supersedes an earlier fixed-field layout once Item Expansion landed).
    private void RefreshFoundItems()
    {
        if (foundItemsContainer == null || foundItemRowPrefab == null) return;

        foreach (Transform child in foundItemsContainer)
            Destroy(child.gameObject);

        if (currentTrip.carriedCarrots > 0) AddFoundItemRow(carrotIcon, "Carrot", currentTrip.carriedCarrots);
        if (currentTrip.foundPotionCount > 0) AddFoundItemRow(potionIcon, "Potion", currentTrip.foundPotionCount);
        if (currentTrip.carriedCrystalCarrots > 0) AddFoundItemRow(crystalCarrotIcon, "Crystal Carrot", currentTrip.carriedCrystalCarrots);

        foreach (var kvp in currentTrip.foundFruits)
            if (kvp.Key != null) AddFoundItemRow(kvp.Key.icon, kvp.Key.displayName, kvp.Value);

        foreach (var kvp in currentTrip.foundTrinkets)
            if (kvp.Key != null) AddFoundItemRow(kvp.Key.icon, kvp.Key.displayName, kvp.Value);

        foreach (var kvp in currentTrip.foundHerbs)
        {
            ForagingMaterialDefinition herb = ForagingInventoryManager.Instance?.GetHerbForRarity(kvp.Key);
            AddFoundItemRow(herb?.icon, herb != null ? herb.displayName : $"{ForagingRarityDisplay.GetTierLabel(kvp.Key)} Herb", kvp.Value);
        }

        foreach (ForagingAccessoryDefinition accessory in currentTrip.foundAccessories)
            if (accessory != null) AddFoundItemRow(accessory.icon, accessory.displayName, 1);
    }

    private void AddFoundItemRow(Sprite icon, string label, int count)
    {
        GameObject rowObj = Instantiate(foundItemRowPrefab, foundItemsContainer);

        Image image = rowObj.GetComponentInChildren<Image>();
        if (image != null)
        {
            image.sprite = icon;
            image.enabled = icon != null;
        }

        TextMeshProUGUI text = rowObj.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = count > 1 ? $"{label} x{count}" : label;
    }

    private void RefreshLog()
    {
        if (logText == null) return;
        logText.text = currentTrip.recentLog.Count > 0
            ? string.Join("\n", currentTrip.recentLog)
            : "Nothing to report yet...";
    }
}
