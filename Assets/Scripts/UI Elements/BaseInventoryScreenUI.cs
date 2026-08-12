using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Read-only base inventory catalog — Ethan's ask: "there needs to be a base inventory catalog, that we
// can view in-game somehow." Carrots/Gold already have their own persistent HUD display elsewhere
// (CarrotCountDisplay and friends); this screen covers everything Foraging-sourced that currently has NO
// visibility once it's in the base stockpile. See the Foraging Trip Detail Panel + Item Expansion design
// doc. No sell/use actions here — selling Trinkets, feeding Fruits, crafting Materials are each separate
// future UI work.
public class BaseInventoryScreenUI : MonoBehaviour
{
    public static BaseInventoryScreenUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;

    [Header("List")]
    [Tooltip("Cleared and rebuilt on open and whenever ForagingInventoryManager.OnInventoryChanged fires.")]
    [SerializeField] private Transform listContainer;
    [Tooltip("Simple prefab: an Image (icon) + a TextMeshProUGUI child (name + count).")]
    [SerializeField] private GameObject rowPrefab;

    [Header("Fixed Icon (Crystal Carrot is a plain int counter on ForagingInventoryManager, not a ScriptableObject asset, so it has no icon field of its own — assign its art here instead. Potions/consumables now have their own icon via ConsumableDefinition, same as every other item family below.)")]
    [SerializeField] private Sprite crystalCarrotIcon;

    private void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);

        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    // Subscribed in Start (not Awake) so ForagingInventoryManager.Instance is guaranteed set regardless
    // of this object's position in the scene's Awake execution order.
    private void Start()
    {
        if (ForagingInventoryManager.Instance != null)
            ForagingInventoryManager.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDestroy()
    {
        if (ForagingInventoryManager.Instance != null)
            ForagingInventoryManager.Instance.OnInventoryChanged -= OnInventoryChanged;
    }

    public void Open()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        AudioManager.EnsureInstance().PlayUIOpen();
        Refresh();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        AudioManager.EnsureInstance().PlayUIClose();
    }

    private void OnInventoryChanged()
    {
        if (panelRoot != null && panelRoot.activeSelf) Refresh();
    }

    private void Refresh()
    {
        if (listContainer == null || rowPrefab == null || ForagingInventoryManager.Instance == null) return;

        foreach (Transform child in listContainer)
            Destroy(child.gameObject);

        ForagingInventoryManager inv = ForagingInventoryManager.Instance;

        if (inv.CrystalCarrotStock > 0) AddRow(crystalCarrotIcon, "Crystal Carrot", inv.CrystalCarrotStock);

        foreach (ConsumableDefinition consumable in inv.GetConsumablesInStock())
            AddRow(consumable.icon, consumable.displayName, inv.GetConsumableCount(consumable));

        // Only unequipped copies show up here — an equipped accessory was withdrawn from stock at equip
        // time (see NPCBunny.EquippedAccessory / ForagingInventoryManager.TryEquipAccessory).
        foreach (ForagingAccessoryDefinition accessory in inv.GetAccessoriesInStock())
            AddRow(accessory.icon, accessory.displayName, inv.GetAccessoryCount(accessory));

        foreach (ForagingFruitDefinition fruit in inv.GetFruitsInStock())
            AddRow(fruit.icon, fruit.displayName, inv.GetFruitCount(fruit));

        foreach (ForagingTrinketDefinition trinket in inv.GetTrinketsInStock())
            AddRow(trinket.icon, $"{ForagingRarityDisplay.GetTierLabel(trinket.rarity)} {trinket.displayName}", inv.GetTrinketCount(trinket));

        // Herb (found directly) and Cloth/Metal (only via crafting a Trinket) all live in the same
        // materialStock bucket — see ForagingMaterialType. Each material asset is now a single fixed-rarity
        // tier (e.g. Moonpetal is always Rare), so its own .rarity/.icon fields are enough.
        foreach (ForagingMaterialDefinition material in inv.GetMaterialsInStock())
            AddRow(material.icon, $"{ForagingRarityDisplay.GetTierLabel(material.rarity)} {material.displayName}", inv.GetMaterialCount(material));
    }

    private void AddRow(Sprite icon, string label, int count)
    {
        GameObject rowObj = Instantiate(rowPrefab, listContainer);

        Image image = rowObj.GetComponentInChildren<Image>();
        if (image != null)
        {
            image.sprite = icon;
            image.enabled = icon != null;
        }

        TextMeshProUGUI text = rowObj.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = $"{label} x{count}";
    }
}
