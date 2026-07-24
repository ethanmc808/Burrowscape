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
}
