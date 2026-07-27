using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Read-only base inventory catalog — Ethan's ask: "there needs to be a base inventory catalog, that we
// can view in-game somehow." Carrots/Gold already have their own persistent HUD display elsewhere
// (CarrotCountDisplay and friends); this screen covers everything Foraging-sourced that currently has NO
// visibility once it's in the base stockpile. See the Foraging Trip Detail Panel + Item Expansion design
// doc. No sell/use actions here — selling Trinkets, feeding Fruits, crafting Materials are each separate
// future UI work.
public class ForagingInventoryScreenUI : MonoBehaviour
{
    public static ForagingInventoryScreenUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;

    [Header("List")]
    [Tooltip("Cleared and rebuilt on open and whenever ForagingInventoryManager.OnInventoryChanged fires.")]
    [SerializeField] private Transform listContainer;
    [Tooltip("Simple prefab: an Image (icon) + a TextMeshProUGUI child (name + count).")]
    [SerializeField] private GameObject rowPrefab;

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
        Refresh();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
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

        if (inv.PotionStock > 0) AddRow(null, "Potion", inv.PotionStock);
        if (inv.CrystalCarrotStock > 0) AddRow(null, "Crystal Carrot", inv.CrystalCarrotStock);

        // Only unequipped copies show up here — an equipped accessory was withdrawn from stock at equip
        // time (see NPCBunny.EquippedAccessory / ForagingInventoryManager.TryEquipAccessory).
        foreach (ForagingAccessoryDefinition accessory in inv.GetAccessoriesInStock())
            AddRow(accessory.icon, accessory.displayName, inv.GetAccessoryCount(accessory));

        foreach (ForagingFruitDefinition fruit in inv.GetFruitsInStock())
            AddRow(fruit.icon, fruit.displayName, inv.GetFruitCount(fruit));

        foreach (var (trinket, rarity) in inv.GetTrinketsInStock())
            AddRow(trinket.icon, $"{ForagingRarityDisplay.GetTierLabel(rarity)} {trinket.displayName}", inv.GetTrinketCount(trinket, rarity));

        // Herb (found directly) and Cloth/Metal (only via crafting a Trinket) all live in the same
        // materialStock bucket — see ForagingMaterialType.
        foreach (var (material, tier) in inv.GetMaterialsInStock())
            AddRow(material.GetIcon(tier), $"{ForagingRarityDisplay.GetTierLabel(tier)} {material.displayName}", inv.GetMaterialCount(material, tier));
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
