using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The base-wide stockpile of Foraging-found potions/accessories, drawn from at dispatch-equip time and
// (for unused potions, and always for accessories — see below) returned on trip completion. Same
// simplicity level as CarrotManager/GoldManager: a plain ledger, nothing inferred automatically.
//
// Potions are genuinely consumable (auto-used during a trip, gone for good) — only whatever's left
// unused at trip end comes back. Accessories are equipment, not consumables — a trip withdraws one from
// stock purely so it can't be double-equipped on two simultaneous trips, and always returns it
// unconditionally when the trip ends, worn but never spent.
public class ForagingInventoryManager : MonoBehaviour
{
    public static ForagingInventoryManager Instance { get; private set; }

    [SerializeField] private int potionStock = 0;
    public int PotionStock => potionStock;

    private readonly Dictionary<ForagingAccessoryDefinition, int> accessoryStock = new Dictionary<ForagingAccessoryDefinition, int>();

    [SerializeField] private int crystalCarrotStock = 0;
    public int CrystalCarrotStock => crystalCarrotStock;

    private readonly Dictionary<ForagingFruitDefinition, int> fruitStock = new Dictionary<ForagingFruitDefinition, int>();
    // Keyed by (trinket, rarity found at) — the SAME trinket asset could in principle sit in more than
    // one location's table at more than one rarity band, and rarity-at-find-time is what it crafts into.
    private readonly Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int> trinketStock = new Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int>();
    // Populated ONLY by TryCraftTrinketIntoMaterial, never by foraging directly.
    private readonly Dictionary<(ForagingMaterialDefinition, ForagingLootRarity), int> materialStock = new Dictionary<(ForagingMaterialDefinition, ForagingLootRarity), int>();

    [Header("Material Assets (Cloth/Metal only ever produced by crafting a Trinket; Herb is granted directly by foraging — see the Foraging Trip Detail Panel + Item Expansion design doc)")]
    [SerializeField] private ForagingMaterialDefinition clothMaterial;
    [SerializeField] private ForagingMaterialDefinition metalMaterial;
    [SerializeField] private ForagingMaterialDefinition herbMaterial;
    // Exposed read-only so ForagingTripDetailUI can look up the Herb material's own tier icons for its
    // rarity-only mid-trip tally, instead of needing the same art assigned a second time.
    public ForagingMaterialDefinition HerbMaterial => herbMaterial;

    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public int GetAccessoryCount(ForagingAccessoryDefinition accessory)
    {
        return accessory != null && accessoryStock.TryGetValue(accessory, out int count) ? count : 0;
    }

    // Every accessory currently held with at least 1 in stock — used by ForagingDispatchUI's equip step
    // to offer only accessories the player has actually found via foraging, rather than needing a
    // separate master catalog list of every accessory that's ever been defined.
    public IEnumerable<ForagingAccessoryDefinition> GetAccessoriesInStock()
    {
        return accessoryStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);
    }

    public void AddPotions(int amount)
    {
        potionStock += Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    public void AddAccessory(ForagingAccessoryDefinition accessory, int amount = 1)
    {
        if (accessory == null) return;
        accessoryStock[accessory] = GetAccessoryCount(accessory) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    // Returns false (no partial withdrawal) if the stockpile doesn't have `amount` available.
    public bool TryWithdrawPotions(int amount)
    {
        if (amount <= 0) return true;
        if (potionStock < amount) return false;

        potionStock -= amount;
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void ReturnPotions(int amount)
    {
        if (amount <= 0) return;
        potionStock += amount;
        OnInventoryChanged?.Invoke();
    }

    public bool TryWithdrawAccessory(ForagingAccessoryDefinition accessory)
    {
        if (accessory == null) return false;
        int count = GetAccessoryCount(accessory);
        if (count <= 0) return false;

        accessoryStock[accessory] = count - 1;
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void ReturnAccessory(ForagingAccessoryDefinition accessory)
    {
        if (accessory == null) return;
        AddAccessory(accessory, 1);
    }

    // ---------- Persistent accessory equip slot (BunnyInfoUI) ----------

    // Handles the withdraw-on-equip / return-on-unequip bookkeeping in one place, so BunnyInfoUI's click
    // handler doesn't need to know stock rules. Swapping directly from one accessory to another is one
    // call: the old one returns to stock, the new one is withdrawn, atomically from the caller's view.
    public bool TryEquipAccessory(NPCBunny bunny, ForagingAccessoryDefinition accessory)
    {
        if (bunny == null) return false;
        if (bunny.EquippedAccessory == accessory) return true; // no-op, already equipped

        if (accessory != null && !TryWithdrawAccessory(accessory)) return false;

        if (bunny.EquippedAccessory != null) ReturnAccessory(bunny.EquippedAccessory);
        bunny.SetEquippedAccessory(accessory);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void UnequipAccessory(NPCBunny bunny) => TryEquipAccessory(bunny, null);

    // ---------- Crystal Carrots ----------

    public void AddCrystalCarrots(int amount)
    {
        crystalCarrotStock += Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    // ---------- Fruits ----------

    public int GetFruitCount(ForagingFruitDefinition fruit)
        => fruit != null && fruitStock.TryGetValue(fruit, out int count) ? count : 0;

    public void AddFruit(ForagingFruitDefinition fruit, int amount)
    {
        if (fruit == null) return;
        fruitStock[fruit] = GetFruitCount(fruit) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    public IEnumerable<ForagingFruitDefinition> GetFruitsInStock()
        => fruitStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // Ledger-level entry point — no feeding UI/button exists yet (future task), but the mechanism is
    // real: grants EV immediately via NPCBunny.AddEV.
    public bool TryFeedFruit(NPCBunny bunny, ForagingFruitDefinition fruit)
    {
        if (bunny == null || fruit == null || GetFruitCount(fruit) <= 0) return false;

        fruitStock[fruit] = GetFruitCount(fruit) - 1;
        bunny.AddEV(fruit.boostedStat, fruit.evAmount);
        OnInventoryChanged?.Invoke();
        return true;
    }

    // ---------- Trinkets ----------

    public int GetTrinketCount(ForagingTrinketDefinition trinket, ForagingLootRarity rarity)
        => trinket != null && trinketStock.TryGetValue((trinket, rarity), out int count) ? count : 0;

    public void AddTrinket(ForagingTrinketDefinition trinket, ForagingLootRarity rarity, int amount)
    {
        if (trinket == null) return;
        var key = (trinket, rarity);
        trinketStock[key] = GetTrinketCount(trinket, rarity) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    // Every trinket currently held with at least 1 in stock — used by BaseInventoryScreenUI and,
    // later, the Workshop room's crafting-menu UI (filtered there to craftsInto != None).
    public IEnumerable<(ForagingTrinketDefinition trinket, ForagingLootRarity rarity)> GetTrinketsInStock()
        => trinketStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // ---------- Materials ----------

    public int GetMaterialCount(ForagingMaterialDefinition material, ForagingLootRarity tier)
        => material != null && materialStock.TryGetValue((material, tier), out int count) ? count : 0;

    public void AddMaterial(ForagingMaterialDefinition material, ForagingLootRarity tier, int amount)
    {
        if (material == null) return;
        var key = (material, tier);
        materialStock[key] = GetMaterialCount(material, tier) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    public IEnumerable<(ForagingMaterialDefinition material, ForagingLootRarity tier)> GetMaterialsInStock()
        => materialStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // Ledger-level entry point only — NOT callable from general UI. Meant to be invoked from wherever the
    // future Workshop room's job-flow lands (an IJobRoom, per Ethan's call — crafting only proceeds while
    // a bunny is actually staffed there), same relationship TryWithdrawAccessory has to TryDispatch.
    public bool TryCraftTrinketIntoMaterial(ForagingTrinketDefinition trinket, ForagingLootRarity rarity)
    {
        if (trinket == null || trinket.craftsInto == TrinketCraftFamily.None) return false;
        if (GetTrinketCount(trinket, rarity) <= 0) return false;

        var key = (trinket, rarity);
        trinketStock[key] = trinketStock[key] - 1;

        ForagingMaterialDefinition output = trinket.craftsInto == TrinketCraftFamily.Cloth ? clothMaterial : metalMaterial;
        AddMaterial(output, rarity, 1);
        OnInventoryChanged?.Invoke();
        return true;
    }

    // Herb's counterpart to TryCraftTrinketIntoMaterial's Cloth/Metal output — called directly from a
    // Herb-kind loot find (ForagingManager) rather than from a crafting action, since Herb needs no
    // Trinket step in between; it's already usable as found.
    public void AddHerbLoot(ForagingLootRarity rarity, int amount)
    {
        AddMaterial(herbMaterial, rarity, amount);
    }
}
