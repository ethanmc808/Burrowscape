using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The base-wide stockpile of Foraging-found/Laboratory-crafted consumables and accessories, drawn from at
// dispatch-equip time and (for unused consumables, and always for accessories — see below) returned on
// trip completion. Same simplicity level as CarrotManager/GoldManager: a plain ledger, nothing inferred
// automatically.
//
// Consumables (potions, and any future Laboratory output — see ConsumableDefinition) are genuinely
// consumable, auto-used during a trip and gone for good — only whatever's left unused at trip end comes
// back. Accessories are equipment, not consumables — a trip withdraws one from stock purely so it can't be
// double-equipped on two simultaneous trips, and always returns it unconditionally when the trip ends,
// worn but never spent.
public class ForagingInventoryManager : MonoBehaviour
{
    public static ForagingInventoryManager Instance { get; private set; }

    // Consumables (potions and any future Laboratory output) — replaces the old bare potionStock int now
    // that they're a real item asset (ConsumableDefinition) with identity/icon instead of an unnamed
    // counter. Populated ONLY by LaboratoryRoom's brew-completion step, never added to directly by general
    // UI — same "ledger-level entry point" relationship TryCraftTrinketIntoMaterial has to the future
    // Workshop room.
    private readonly Dictionary<ConsumableDefinition, int> consumableStock = new Dictionary<ConsumableDefinition, int>();

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

    [Header("Save/Load Catalogs (name-resolution only — Material already has clothTiers/metalTiers/herbTiers above for this; Fruit/Trinket/Consumable have no other master list anywhere, so these exist purely so SaveManager can resolve a saved displayName back to an asset reference on load)")]
    [SerializeField] private List<ForagingFruitDefinition> allFruits = new List<ForagingFruitDefinition>();
    [SerializeField] private List<ForagingTrinketDefinition> allTrinkets = new List<ForagingTrinketDefinition>();
    [SerializeField] private List<ConsumableDefinition> allConsumables = new List<ConsumableDefinition>();

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

    public void AddAccessory(ForagingAccessoryDefinition accessory, int amount = 1)
    {
        if (accessory == null) return;
        accessoryStock[accessory] = GetAccessoryCount(accessory) + Mathf.Max(0, amount);
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

    // Debug-only shortcut for testing the Laboratory without waiting on foraging RNG — right-click this
    // component's header in the Inspector during Play mode and pick "Debug: Add 10 Of Each Herb Tier".
    // Not wired to any UI, not called from anywhere at runtime.
    [ContextMenu("Debug: Add 10 Of Each Herb Tier")]
    private void DebugAddHerbs()
    {
        for (int i = 0; i < herbTiers.Length; i++)
            AddMaterial(herbTiers[i], 10);
        Debug.Log("[ForagingInventoryManager] Debug: added 10 of each herb tier.");
    }

    // ---------- Consumables (potions and any future Laboratory output) ----------

    public int GetConsumableCount(ConsumableDefinition consumable)
        => consumable != null && consumableStock.TryGetValue(consumable, out int count) ? count : 0;

    public void AddConsumable(ConsumableDefinition consumable, int amount)
    {
        if (consumable == null) return;
        consumableStock[consumable] = GetConsumableCount(consumable) + Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }

    // Every consumable currently held with at least 1 in stock — used by BunnyInfoUI/ForagingDispatchUI to
    // read live stock, and would back a generic "pick any owned consumable" UI if one is ever built.
    public IEnumerable<ConsumableDefinition> GetConsumablesInStock()
        => consumableStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);

    // Returns false (no partial withdrawal) if the stockpile doesn't have `amount` available. Replaces the
    // old TryWithdrawPotions — same withdraw-on-use, return-on-unused-trip-end shape as accessories, just
    // consumable rather than persistent equipment.
    public bool TryWithdrawConsumable(ConsumableDefinition consumable, int amount = 1)
    {
        if (consumable == null || amount <= 0) return amount <= 0;
        if (GetConsumableCount(consumable) < amount) return false;

        consumableStock[consumable] -= amount;
        OnInventoryChanged?.Invoke();
        return true;
    }

    // Replaces the old ReturnPotions — used by ForagingManager to give back whatever consumables a trip
    // didn't end up auto-using.
    public void ReturnConsumable(ConsumableDefinition consumable, int amount)
    {
        if (amount <= 0) return;
        AddConsumable(consumable, amount);
    }

    // ---------- Laboratory recipes ----------

    // Atomic all-or-nothing herb withdrawal for one brew's worth of ingredients — fails clean (nothing
    // deducted) if any single ingredient is short, same "no partial spend" contract every other TryX method
    // here follows. Called by LaboratoryRoom.StartBrew at the moment a bunny begins brewing, not at
    // completion — herbs are locked in for the brew's duration rather than risking being spent elsewhere
    // mid-brew. Ledger-level entry point only, same relationship TryCraftTrinketIntoMaterial has to the
    // Workshop room — not callable from general UI, meant to be invoked from LaboratoryRoom's job-flow.
    // quantity multiplies every ingredient's cost — used by LaboratoryRoom.StartBrew's 1x/5x/10x batch
    // picker. Defaults to 1 so any other future caller can ignore the parameter entirely.
    public bool TryWithdrawIngredients(RecipeDefinition recipe, int quantity = 1)
    {
        if (recipe == null || quantity <= 0) return false;

        foreach (RecipeIngredientCost cost in recipe.ingredients)
            if (GetMaterialCount(cost.herb) < cost.amount * quantity) return false;

        foreach (RecipeIngredientCost cost in recipe.ingredients)
            materialStock[cost.herb] = GetMaterialCount(cost.herb) - cost.amount * quantity;

        OnInventoryChanged?.Invoke();
        return true;
    }

    // ---------- Save/load (see SaveManager.SaveForagingInventory/LoadForagingInventory) ----------
    // Every stock dictionary here holds direct asset references, which can't round-trip through JSON, so
    // each is exported/imported by displayName instead — same pattern ForagingLocationUnlockTracker/
    // LaboratoryRecipeUnlockTracker already use for their own unlock sets. Resolved back against the
    // relevant catalog above (allFruits/allTrinkets/allConsumables, or the existing clothTiers/metalTiers/
    // herbTiers for materials) on import.

    public List<NamedCountEntry> ExportFruitStock()
    {
        List<NamedCountEntry> entries = new List<NamedCountEntry>();
        foreach (KeyValuePair<ForagingFruitDefinition, int> kvp in fruitStock)
            if (kvp.Key != null && kvp.Value > 0) entries.Add(new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value });
        return entries;
    }

    public void ImportFruitStock(List<NamedCountEntry> entries)
    {
        fruitStock.Clear();
        if (entries == null) return;
        foreach (NamedCountEntry entry in entries)
        {
            ForagingFruitDefinition fruit = allFruits.FirstOrDefault(f => f != null && f.displayName == entry.name);
            if (fruit != null) fruitStock[fruit] = entry.count;
        }
        OnInventoryChanged?.Invoke();
    }

    public List<NamedCountEntry> ExportTrinketStock()
    {
        List<NamedCountEntry> entries = new List<NamedCountEntry>();
        foreach (KeyValuePair<ForagingTrinketDefinition, int> kvp in trinketStock)
            if (kvp.Key != null && kvp.Value > 0) entries.Add(new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value });
        return entries;
    }

    public void ImportTrinketStock(List<NamedCountEntry> entries)
    {
        trinketStock.Clear();
        if (entries == null) return;
        foreach (NamedCountEntry entry in entries)
        {
            ForagingTrinketDefinition trinket = allTrinkets.FirstOrDefault(t => t != null && t.displayName == entry.name);
            if (trinket != null) trinketStock[trinket] = entry.count;
        }
        OnInventoryChanged?.Invoke();
    }

    public List<NamedCountEntry> ExportMaterialStock()
    {
        List<NamedCountEntry> entries = new List<NamedCountEntry>();
        foreach (KeyValuePair<ForagingMaterialDefinition, int> kvp in materialStock)
            if (kvp.Key != null && kvp.Value > 0) entries.Add(new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value });
        return entries;
    }

    public void ImportMaterialStock(List<NamedCountEntry> entries)
    {
        materialStock.Clear();
        if (entries == null) return;

        IEnumerable<ForagingMaterialDefinition> allMaterials = clothTiers.Concat(metalTiers).Concat(herbTiers);
        foreach (NamedCountEntry entry in entries)
        {
            ForagingMaterialDefinition material = allMaterials.FirstOrDefault(m => m != null && m.displayName == entry.name);
            if (material != null) materialStock[material] = entry.count;
        }
        OnInventoryChanged?.Invoke();
    }

    public List<NamedCountEntry> ExportConsumableStock()
    {
        List<NamedCountEntry> entries = new List<NamedCountEntry>();
        foreach (KeyValuePair<ConsumableDefinition, int> kvp in consumableStock)
            if (kvp.Key != null && kvp.Value > 0) entries.Add(new NamedCountEntry { name = kvp.Key.displayName, count = kvp.Value });
        return entries;
    }

    public void ImportConsumableStock(List<NamedCountEntry> entries)
    {
        consumableStock.Clear();
        if (entries == null) return;
        foreach (NamedCountEntry entry in entries)
        {
            ConsumableDefinition consumable = allConsumables.FirstOrDefault(c => c != null && c.displayName == entry.name);
            if (consumable != null) consumableStock[consumable] = entry.count;
        }
        OnInventoryChanged?.Invoke();
    }

    public void SetCrystalCarrotStockForLoad(int amount)
    {
        crystalCarrotStock = Mathf.Max(0, amount);
        OnInventoryChanged?.Invoke();
    }
}
