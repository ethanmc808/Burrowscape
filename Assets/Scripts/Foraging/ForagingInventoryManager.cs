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
    // Rarity is fixed on the Trinket asset itself now (see Rarity_DesignDoc.md), so no separate rarity
    // dimension is needed in the key anymore.
    private readonly Dictionary<ForagingTrinketDefinition, int> trinketStock = new Dictionary<ForagingTrinketDefinition, int>();
    // Populated ONLY by TryCraftTrinketIntoMaterial and AddHerbLoot, never added to directly by general UI.
    private readonly Dictionary<ForagingMaterialDefinition, int> materialStock = new Dictionary<ForagingMaterialDefinition, int>();

    [Header("Material Tier Assets (index = ForagingLootRarity; Cloth/Metal only ever produced by crafting a Trinket, at the trinket's own fixed rarity; Herb is granted directly by foraging — see Rarity_DesignDoc.md)")]
    [Tooltip("5 entries, one per ForagingLootRarity tier (Common..Mythical) — e.g. Coarse/Fine/Silken/Shimmering/Ethereal Cloth.")]
    [SerializeField] private ForagingMaterialDefinition[] clothTiers = new ForagingMaterialDefinition[5];
    [Tooltip("5 entries, one per ForagingLootRarity tier (Common..Mythical) — e.g. Scrap Metal/Iron/Silver/Gold/Starmetal Ingot.")]
    [SerializeField] private ForagingMaterialDefinition[] metalTiers = new ForagingMaterialDefinition[5];
    [Tooltip("5 entries, one per ForagingLootRarity tier (Common..Mythical) — Mint Leaf/Silverwort/Moonpetal/Sparkling Thistle/Golden Lotus Petal.")]
    [SerializeField] private ForagingMaterialDefinition[] herbTiers = new ForagingMaterialDefinition[5];

    [Header("Fixed rarity for the 3 asset-less kinds (Carrot/Potion/Crystal Carrot) — edit via Burrowscape/Rarity Manager")]
    [SerializeField] private ForagingKindRarityConfig kindRarities;
    public ForagingLootRarity CarrotRarity => kindRarities != null ? kindRarities.carrotRarity : ForagingLootRarity.Common;
    public ForagingLootRarity PotionRarity => kindRarities != null ? kindRarities.potionRarity : ForagingLootRarity.Common;
    public ForagingLootRarity CrystalCarrotRarity => kindRarities != null ? kindRarities.crystalCarrotRarity : ForagingLootRarity.Common;

    // Looks up the named tier asset (e.g. Moonpetal for Herb/Rare) so callers can resolve a rolled rarity
    // band down to a specific, fixed-rarity item. Exposed read-only so ForagingTripDetailUI can look up
    // icons without needing the same art assigned a second time.
    public ForagingMaterialDefinition GetHerbForRarity(ForagingLootRarity rarity) => herbTiers[(int)rarity];
    private ForagingMaterialDefinition GetClothForRarity(ForagingLootRarity rarity) => clothTiers[(int)rarity];
    private ForagingMaterialDefinition GetMetalForRarity(ForagingLootRarity rarity) => metalTiers[(int)rarity];

    [Header("Debug / Playtesting")]
    [Tooltip("Assign the Apple fruit asset once — the field below then behaves exactly like potionStock/crystalCarrotStock: edit the number directly in the Inspector during Play mode to set the real stock instantly. It also pulls UP to reflect the true count if it changes some other way (e.g. feeding a bunny), so it never silently drifts out of sync.")]
    [SerializeField] private ForagingFruitDefinition debugAppleAsset;
    [SerializeField] private int debugAppleStock = 0;
    private int lastSyncedDebugAppleStock = 0;

    public event Action OnInventoryChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        SyncDebugAppleStock();
    }

    // Debug-only two-way mirror onto fruitStock for the assigned apple asset (see the field's Tooltip
    // above). A typed-in Inspector value pushes down into the real stock; any other change to the real
    // stock (feeding a bunny, a forage find) pulls back up into the field so it never reads stale.
    private void SyncDebugAppleStock()
    {
        if (debugAppleAsset == null) return;

        int actual = GetFruitCount(debugAppleAsset);
        if (debugAppleStock == actual) return;

        if (debugAppleStock != lastSyncedDebugAppleStock)
        {
            int clamped = Mathf.Max(0, debugAppleStock);
            fruitStock[debugAppleAsset] = clamped;
            actual = clamped;
            OnInventoryChanged?.Invoke();
        }

        debugAppleStock = actual;
        lastSyncedDebugAppleStock = actual;
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

    public int GetTrinketCount(ForagingTrinketDefinition trinket)
        => trinket != null && trinketStock.TryGetValue(trinket, out int count) ? count : 0;

    public void AddTrinket(ForagingTrinketDefinition trinket, int amount)
    {
        if (trinket == null) return;
        trinketStock[trinket] = GetTrinketCount(trinket) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    // Every trinket currently held with at least 1 in stock — used by BaseInventoryScreenUI and,
    // later, the Workshop room's crafting-menu UI (filtered there to craftsInto != None).
    public IEnumerable<ForagingTrinketDefinition> GetTrinketsInStock()
        => trinketStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // ---------- Materials ----------

    public int GetMaterialCount(ForagingMaterialDefinition material)
        => material != null && materialStock.TryGetValue(material, out int count) ? count : 0;

    public void AddMaterial(ForagingMaterialDefinition material, int amount)
    {
        if (material == null) return;
        materialStock[material] = GetMaterialCount(material) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    public IEnumerable<ForagingMaterialDefinition> GetMaterialsInStock()
        => materialStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // Ledger-level entry point only — NOT callable from general UI. Meant to be invoked from wherever the
    // future Workshop room's job-flow lands (an IJobRoom, per Ethan's call — crafting only proceeds while
    // a bunny is actually staffed there), same relationship TryWithdrawAccessory has to TryDispatch.
    public bool TryCraftTrinketIntoMaterial(ForagingTrinketDefinition trinket)
    {
        if (trinket == null || trinket.craftsInto == TrinketCraftFamily.None) return false;
        if (GetTrinketCount(trinket) <= 0) return false;

        trinketStock[trinket] = trinketStock[trinket] - 1;

        // Output tier follows the trinket's own fixed rarity, not wherever it happened to be found.
        ForagingMaterialDefinition output = trinket.craftsInto == TrinketCraftFamily.Cloth
            ? GetClothForRarity(trinket.rarity)
            : GetMetalForRarity(trinket.rarity);
        AddMaterial(output, 1);
        OnInventoryChanged?.Invoke();
        return true;
    }

    // Herb's counterpart to TryCraftTrinketIntoMaterial's Cloth/Metal output — called directly from a
    // Herb-kind loot find (ForagingManager) rather than from a crafting action, since Herb needs no
    // Trinket step in between; it's already usable as found. `rarity` here is the band the location's
    // loot table rolled — resolved to the matching fixed-rarity Herb asset (Mint Leaf/Silverwort/...).
    public void AddHerbLoot(ForagingLootRarity rarity, int amount)
    {
        AddMaterial(GetHerbForRarity(rarity), amount);
    }
}
