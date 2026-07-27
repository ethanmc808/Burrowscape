# Foraging Trip Detail Panel + Item Expansion — Design Document

## Context

Two merged asks, now one doc since they turned out tightly coupled:

1. **Trip detail panel**: Foraging works mechanically but reads as opaque — dispatch a bunny, get a
   number back at the end, no visibility into what happened. Modeled on Fallout Shelter's per-dweller
   exploration log. Opened by selecting a row in `ForagingScreenUI`'s active-trips list.
2. **Item expansion**: today's loot is only Carrots/Potions/Accessories — three kinds, none of them
   really "collectible." Ethan brainstormed four new findable/derived categories (Fruits, Trinkets,
   Materials, Herbs) plus a Rare-only Crystal Carrot, in a separate handoff doc
   (`Foraging_ItemExpansion_HandoffDoc.md`). Folded in here because the detail panel's "Current Items
   Found" section needs to display all of these, not just the original three.

This supersedes the original doc's "Item variety stays as-is for this pass" decision — that was true
before Ethan's brainstorming pass landed, isn't anymore.

## Corrections made while folding in the handoff doc

The handoff doc was written/reasoned about without re-checking current `NPCBunny`/`BunnyStatCalculator`
state. Verified directly against the code before accepting any of it:

- **EV storage already exists.** `NPCBunny.EVHP/EVAttack/EVDefense/EVSpeed/EVLuck` are already real
  properties, already fed into `BunnyStatCalculator.Resolve` at spawn (`WildBunnySpawner`) and at
  `LevelUp()` — the handoff doc's claimed "critical fix" (LevelUp discarding EVs) does not exist as a bug.
  The only real gap is that nothing has ever called anything to *change* EVHP etc. after
  `RollIndividuality()` sets them to their spawn-time value (0) — no `AddEV`-shaped method exists yet.
- **The EV/4 divisor is already inside `BunnyStatCalculator.FloorFormulaCore`** (`ev / 4`). The handoff
  doc's proposed `ApplyEVBonuses` helper re-divides by 4 a second time — dropped entirely. `AddEV` just
  calls the existing `Resolve` the same way `LevelUp` already does.
- **`NPCBunny.MaxTotalEV = 512` already exists**, explicitly commented as reserved for "whatever future
  system grants EVs" — this is that system. `AddEV` must respect both the per-stat cap (256) **and** this
  512 total-across-all-5 cap; the handoff doc's snippet only enforced the per-stat cap.
- **`NatureStat` has no HP entry, by design** (`BunnyStatCalculator.ResolveHP` never runs Nature's
  multiplier — HP is structurally excluded from every Nature-related switch in the codebase, including
  gate-checks). Rather than add HP to an enum whose entire meaning is "one of the two Nature-affected
  stats," Fruits use a new, separate `BunnyStatType { HP, Attack, Defense, Speed, Luck }` enum. This
  answers the handoff doc's own open dependency-check question (#4) rather than leaving it open.

Everything else in the handoff doc (new ScriptableObjects, loot-kind additions, inventory ledger shape,
Workshop-room deferral) checked out against the current code and is carried forward largely as written.

## Locked-in decisions (trip detail panel — unchanged from original pass)

- **Enemy identity is per-location flavor text, not new gameplay.** `ForagingLocationDefinition` gets
  `List<string> enemyNames` (e.g. Volcano: "an Ash Hound", "a Wasp"). `ResolveEncounter` picks one at
  random purely to word the log line and increment `enemiesSlain` — no new stats. Empty list falls back
  to "a wild creature."
- **The log only records meaningful events** — a tick where nothing triggered produces zero log lines.
  Last-10 window is always 10 things that actually happened.
- **The walking-bunny visual is a decorative duplicate on a dedicated offscreen preview stage** — its own
  camera rendering to a `RenderTexture` shown via `RawImage`, not a UI sprite animation and not the real
  `NPCBunny` (which stays parked at its actual foraging staging position the whole trip, untouched). The
  duplicate has its `NPCBunny` script and all colliders disabled. Only one exists at a time.
- **The detail panel lives on the left; `ForagingScreenUI`'s existing panel moves right→left** to make
  room. Pure Editor/RectTransform task, not code.
- **Live refresh, no manual push** — `Show(bunny, trip)` captures references; the panel reads them on its
  own throttled timer (mirrors `ForagingScreenUI.listRefreshInterval`).

## Locked-in decisions (item expansion — from the handoff doc, verified)

- **Materials are never found directly** — only produced by crafting a Trinket at a future Workshop room
  (`TryCraftTrinketIntoMaterial`, ledger-only this pass, no room mechanics). Only 2 Material assets needed
  (Cloth, Metal), not 6 — tier is inherited from the crafted Trinket's found-rarity, not rolled separately.
- **Fruits** feed a bunny for a flat EV amount toward one stat (`BunnyStatType`) — ledger method
  (`TryFeedFruit`) exists, no feeding UI this pass (button/screen is a future task).
- **Trinkets** are mostly collectible/sellable (`sellValue`); a subset also have `craftsInto` set to Cloth
  or Metal for the future Workshop.
- **Herbs** (Mint Leaf/Silverwort/Moonpetal) are pure identity stubs for now — no effect fields, feeds a
  future Laboratory/potion system, same "data exists, behavior comes later" treatment as
  `BunnyPassiveDefinition`.
- **Crystal Carrot** needs no new asset — behaves exactly like `Carrot` (a plain stockpile int), just its
  own `ForagingLootKind` so it can be authored into a location's Rare band specifically (enforced by loot
  table authoring, not code).
- **Workshop room is explicitly out of scope**, same deferred-scope treatment as Guard Station. This pass
  only adds the ledger method the room will eventually call, plus a `GetTrinketsInStock()`-style
  enumerable for whatever crafting-menu UI comes later.
- **Workshop room's shape is resolved** (Ethan's call): it's a work room, same `IJobRoom` job-assignment
  pattern as `GardenRoom` — crafting only proceeds while a bunny is actually staffed/working there, not a
  manual instant-craft screen gated purely on ownership. Still deferred as an actual room to build (this
  pass only ships the ledger method it will eventually call), but the open question about its shape is no
  longer open.

## Locked-in decisions (persistent accessory slot — new, supersedes "chosen fresh each trip")

Ethan confirmed accessories become a **persistent equip slot on the bunny**, viewable/changeable via
`BunnyInfoUI` at any time the bunny is resident (not foraging), rather than picked fresh at every
dispatch. This directly supersedes `Foraging_DesignDoc.md`'s original "Dispatch is per-trip, not a
persistent loadout" decision **for accessories only** — potions are untouched, still chosen fresh per
trip.

- **One slot for now** — matches today's data shape (`ForagingAccessoryDefinition equippedAccessory`,
  singular). A future second slot is an explicit later expansion, not designed here.
- **Equip/unequip only happens while the bunny is resident.** A foraging bunny is parked offscreen and
  can't be clicked in the world (`Foraging_DesignDoc.md`'s "Manual recall" section) — so `BunnyInfoUI`,
  and therefore the accessory slot, is simply never reachable mid-trip. No mid-trip swap case to handle.
- **Withdrawal from the shared stock happens at equip time, not at dispatch time.** Equipping a bunny
  takes that physical copy out of `ForagingInventoryManager`'s stock (so it can't be double-equipped on
  two bunnies at once); unequipping returns it. Foraging trips no longer touch accessory stock at all —
  they just read whatever's already equipped.
- **`ForagingDispatchUI`'s accessory-picker step goes away**, replaced by a read-only "Currently Equipped:
  {name}" line (or hidden entirely if nothing's equipped) — equip/unequip now only happens via
  `BunnyInfoUI`, before dispatch.

### `NPCBunny` additions
```csharp
public ForagingAccessoryDefinition EquippedAccessory { get; private set; }
public void SetEquippedAccessory(ForagingAccessoryDefinition accessory) => EquippedAccessory = accessory;
```
Deliberately dumb — `NPCBunny` just stores the reference. All the stock withdraw/return bookkeeping lives
in `ForagingInventoryManager` (below), same separation `TryDispatch` already keeps between "bunny does
identity/state" and "manager does economy."

### `ForagingInventoryManager` additions
```csharp
// Handles the withdraw-on-equip / return-on-unequip bookkeeping in one place, so BunnyInfoUI's click
// handler doesn't need to know stock rules. Swapping directly from one accessory to another is one call:
// the old one returns to stock, the new one is withdrawn, atomically from the caller's point of view.
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
```

### `ForagingManager` changes
- **`TryDispatch` drops its `accessory` parameter** — reads `bunny.EquippedAccessory` internally instead
  and stores it into `trip.equippedAccessory` for the trip's own internal use (carry-capacity bonus, rare-
  loot bonus, return-speed multiplier — all unchanged, just sourced differently).
- **`DepositTripResults` no longer calls `ReturnAccessory`** — the accessory was never withdrawn at
  dispatch time anymore, so there's nothing to return at trip end. It stays equipped on the bunny exactly
  as it was before the trip started.

## Data model additions

### `BunnyStatType` (new enum — put in `BunnyStats.cs`, next to the `BunnyStats` struct)
```csharp
public enum BunnyStatType { HP, Attack, Defense, Speed, Luck }
```
Deliberately separate from `NatureStat` (see "Corrections" above).

### `ForagingLocationDefinition`
```csharp
[Header("Encounter Flavor (log text only — no gameplay effect)")]
[Tooltip("Include the article, e.g. \"a Wasp\", \"an Ash Hound\". Empty list falls back to \"a wild creature\".")]
public List<string> enemyNames = new List<string>();
```

### `ForagingLootKind` (extended)
```csharp
public enum ForagingLootKind { Carrot, Potion, Accessory, Fruit, Trinket, Herb, CrystalCarrot }
```
`Material` deliberately absent as a *kind* — Cloth/Metal are never rolled, only crafted. Herb IS a
`ForagingMaterialType` (see below) but IS rolled directly, since it needs no crafting step.

### `ForagingLootEntry` (extended)
```csharp
[Tooltip("Only used when kind == Fruit.")]
public ForagingFruitDefinition fruit;
[Tooltip("Only used when kind == Trinket.")]
public ForagingTrinketDefinition trinket;
```
`Herb` needs no reference field of its own — same as `CrystalCarrot`, it reuses `minAmount`/`maxAmount`
and the entry's own `rarity`, since there's only one generic Herb material asset (rarity alone identifies
the tier granted).

### New ScriptableObjects (mirror `ForagingAccessoryDefinition`'s one-asset-per-entry shape)

**`ForagingFruitDefinition.cs`**
```csharp
[CreateAssetMenu(fileName = "ForagingFruitDefinition", menuName = "Burrowscape/Foraging Fruit Definition")]
public class ForagingFruitDefinition : ScriptableObject
{
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;
    public BunnyStatType boostedStat;
    public int evAmount = 4; // tunable per asset, not a shared constant — matches this project's convention
}
```

**`ForagingTrinketDefinition.cs`**
```csharp
public enum TrinketCraftFamily { None, Cloth, Metal }

[CreateAssetMenu(fileName = "ForagingTrinketDefinition", menuName = "Burrowscape/Foraging Trinket Definition")]
public class ForagingTrinketDefinition : ScriptableObject
{
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;
    public int sellValue = 5;
    [Tooltip("None = collectible/sellable only. Tier produced when crafted is inherited from the rarity band this was FOUND at, not stored here.")]
    public TrinketCraftFamily craftsInto = TrinketCraftFamily.None;
}
```

**`ForagingMaterialDefinition.cs`** (one asset per *type*, not per tier — Cloth, Metal, Herb: 3 assets total)
```csharp
public enum ForagingMaterialType { Cloth, Metal, Herb }

[CreateAssetMenu(fileName = "ForagingMaterialDefinition", menuName = "Burrowscape/Foraging Material Definition")]
public class ForagingMaterialDefinition : ScriptableObject
{
    public string displayName; // "Cloth", "Metal", "Herb" — tier prefix added at display time
    [TextArea] public string description;
    public ForagingMaterialType materialType;

    // One asset spans all 3 rarity tiers, so it needs 3 separate icons, not 1 — a Rare Cloth should be
    // able to look visually distinct from a Common Cloth, not just carry a different text label.
    public Sprite commonIcon;
    public Sprite fineIcon;
    public Sprite rareIcon;
    public Sprite GetIcon(ForagingLootRarity rarity) => rarity switch
    {
        ForagingLootRarity.Uncommon => fineIcon,
        ForagingLootRarity.Rare => rareIcon,
        _ => commonIcon,
    };
}
```
Display naming for a tiered material (presentation-layer only, e.g. building "Fine Cloth"): Common →
"Common", Uncommon → "Fine", Rare → "Rare". `ForagingInventoryManager` exposes `HerbMaterial` (read-only)
so `ForagingTripDetailUI`'s rarity-only mid-trip Herb tally can read the same tier icons rather than
needing the same art assigned a second time as a fixed sprite.

**Herb has no separate identity ScriptableObject** (the earlier `ForagingHerbDefinition` — Mint
Leaf/Silverwort/Moonpetal — was retired). Ethan's correction: Herb's rarity system should match Cloth/Metal
exactly (3 tiers, tracked the same way in `materialStock`); the only real difference is that Herb is
*already usable as found* (granted directly by a Herb-kind loot roll), while Cloth/Metal are only ever
produced by crafting a same-rarity Trinket down at the Workshop. So Herb became a third
`ForagingMaterialType`, one generic asset, exactly like Cloth/Metal — not three named flavor items without
tier tracking.

### `ForagingTripState` (extended)
```csharp
public Dictionary<ForagingFruitDefinition, int> foundFruits = new Dictionary<ForagingFruitDefinition, int>();
public Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int> foundTrinkets = new Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int>();
// Keyed by rarity alone, not an asset — Herb has a single generic ForagingMaterialDefinition, so rarity
// is the only thing distinguishing one find from another.
public Dictionary<ForagingLootRarity, int> foundHerbs = new Dictionary<ForagingLootRarity, int>();
public int carriedCrystalCarrots;

public int enemiesSlain;
public const int MaxLogEntries = 10;
public readonly List<string> recentLog = new List<string>(); // index 0 = most recent
public void AddLogEntry(string entry) { recentLog.Insert(0, entry); if (recentLog.Count > MaxLogEntries) recentLog.RemoveAt(recentLog.Count - 1); }
```
No `foundMaterials` — materials never appear mid-trip, only in the Workshop ledger afterward.

`CarriedItemCount` extended:
```csharp
public int CarriedItemCount => carriedCarrots + foundPotionCount + foundAccessories.Count
    + foundFruits.Values.Sum() + foundTrinkets.Values.Sum() + foundHerbs.Values.Sum() + carriedCrystalCarrots;
```

### `NPCBunny`
```csharp
public BunnyTypeDefinition TypeDefinition => typeDefinition; // for ForagingPreviewStage's duplicate lookup

public void AddEV(BunnyStatType stat, int amount)
{
    if (amount <= 0 || typeDefinition == null) return;

    int currentTotal = EVHP + EVAttack + EVDefense + EVSpeed + EVLuck;
    int currentStat = GetEV(stat);
    int actualAmount = Mathf.Max(0, Mathf.Min(amount, Mathf.Min(256 - currentStat, MaxTotalEV - currentTotal)));
    if (actualAmount <= 0) return;

    SetEV(stat, currentStat + actualAmount);

    int previousMaxHP = Stats.HP;
    Stats = BunnyStatCalculator.Resolve(typeDefinition, Level,
        IVHP, IVAttack, IVDefense, IVSpeed, IVLuck,
        EVHP, EVAttack, EVDefense, EVSpeed, EVLuck);
    ApplyNatureEffects();
    currentHP = Mathf.Clamp(currentHP + (Stats.HP - previousMaxHP), 1, Stats.HP);
}

private int GetEV(BunnyStatType stat) { switch (stat) { case BunnyStatType.HP: return EVHP; case BunnyStatType.Attack: return EVAttack; case BunnyStatType.Defense: return EVDefense; case BunnyStatType.Speed: return EVSpeed; default: return EVLuck; } }
private void SetEV(BunnyStatType stat, int value) { switch (stat) { case BunnyStatType.HP: EVHP = value; break; case BunnyStatType.Attack: EVAttack = value; break; case BunnyStatType.Defense: EVDefense = value; break; case BunnyStatType.Speed: EVSpeed = value; break; default: EVLuck = value; break; } }
```
Same recompute pattern `LevelUp` already uses (re-resolve `Stats`, reapply Nature, carry `currentHP`
forward by the max-HP delta) — no new pattern introduced, just reused.

## `ForagingInventoryManager` additions

Same "plain ledger, nothing inferred" shape as existing `potionStock`/`accessoryStock`.

```csharp
[SerializeField] private int crystalCarrotStock = 0;
public int CrystalCarrotStock => crystalCarrotStock;

private readonly Dictionary<ForagingFruitDefinition, int> fruitStock = new Dictionary<ForagingFruitDefinition, int>();
// Keyed by (trinket, rarity found at) — the SAME trinket asset could in principle sit in more than one
// location's table at more than one rarity band, and rarity-at-find-time is what it crafts into.
private readonly Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int> trinketStock = new Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int>();
// Herb (found directly) AND Cloth/Metal (only via crafting a Trinket) all live here — see
// ForagingMaterialType.
private readonly Dictionary<(ForagingMaterialDefinition, ForagingLootRarity), int> materialStock = new Dictionary<(ForagingMaterialDefinition, ForagingLootRarity), int>();

[SerializeField] private ForagingMaterialDefinition clothMaterial;
[SerializeField] private ForagingMaterialDefinition metalMaterial;
[SerializeField] private ForagingMaterialDefinition herbMaterial;
```

New methods (`Add*`/`Get*Count` pattern, matching existing `AddAccessory`/`GetAccessoryCount`):
`AddFruit`/`GetFruitCount`, `AddTrinket`/`GetTrinketCount`, `AddMaterial`/`GetMaterialCount`,
`AddCrystalCarrots`, plus `AddHerbLoot(rarity, amount)` — Herb's counterpart to
`TryCraftTrinketIntoMaterial`'s Cloth/Metal output, just calling `AddMaterial(herbMaterial, rarity, amount)`
directly since Herb needs no Trinket/crafting step in between.

```csharp
public bool TryFeedFruit(NPCBunny bunny, ForagingFruitDefinition fruit)
{
    if (bunny == null || fruit == null || GetFruitCount(fruit) <= 0) return false;
    fruitStock[fruit] = GetFruitCount(fruit) - 1;
    bunny.AddEV(fruit.boostedStat, fruit.evAmount);
    OnInventoryChanged?.Invoke();
    return true;
}

// Ledger-level entry point only — NOT callable from general UI. Meant to be invoked from wherever the
// future Workshop room's job-flow lands, same relationship TryWithdrawAccessory has to TryDispatch.
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

public IEnumerable<(ForagingTrinketDefinition trinket, ForagingLootRarity rarity)> GetTrinketsInStock()
    => trinketStock.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key);
```

## `ForagingManager` changes

**`ResolveLootFind`** — new switch cases (reusing the `rarity`/`amount` variables already in scope):
```csharp
case ForagingLootKind.Fruit:
    if (entry.fruit != null)
    {
        trip.foundFruits[entry.fruit] = trip.foundFruits.GetValueOrDefault(entry.fruit) + amount;
        trip.AddLogEntry($"Found {entry.fruit.displayName}");
    }
    break;
case ForagingLootKind.Trinket:
    if (entry.trinket != null)
    {
        var key = (entry.trinket, rarity);
        trip.foundTrinkets[key] = trip.foundTrinkets.GetValueOrDefault(key) + amount;
        trip.AddLogEntry($"Found {entry.trinket.displayName}");
    }
    break;
case ForagingLootKind.Herb:
    trip.foundHerbs[rarity] = trip.foundHerbs.GetValueOrDefault(rarity) + amount;
    trip.AddLogEntry($"Found {amount} {ForagingRarityDisplay.GetTierLabel(rarity)} Herb{(amount == 1 ? "" : "s")}");
    break;
case ForagingLootKind.CrystalCarrot:
    trip.carriedCrystalCarrots += amount;
    trip.AddLogEntry($"Found {amount} Crystal Carrot{(amount == 1 ? "" : "s")}");
    break;
```
Plus the original three kinds' own log lines (Carrot/Potion/Accessory found), gold-find log, encounter
win/loss + enemy-name log, auto-potion-use log, and gate-check-pass log — all from the original pass,
listed together below for one complete reference:

| Resolver | Line(s) |
|---|---|
| Loot: Carrot | `"Found {amount} Carrot(s)"` |
| Loot: Potion | `"Found {amount} Potion(s)"` |
| Loot: Accessory | `"Found {accessory.displayName}"` |
| Loot: Fruit | `"Found {fruit.displayName}"` |
| Loot: Trinket | `"Found {trinket.displayName}"` |
| Loot: Herb | `"Found {herb.displayName}"` |
| Loot: CrystalCarrot | `"Found {amount} Crystal Carrot(s)"` |
| Gold find | `"Found {amount} Gold"` |
| Encounter win | `"Fought off {enemyName}"` + `enemiesSlain++` |
| Encounter loss | `"Was hurt by {enemyName} (-{damage} HP)"` |
| Auto-potion after a loss | `"Used a Potion to heal"` |
| Gate-check pass | `"Passed a {stat} test"` |

**`DepositTripResults`** — new deposit calls:
```csharp
foreach (var kvp in trip.foundFruits) ForagingInventoryManager.Instance?.AddFruit(kvp.Key, kvp.Value);
foreach (var kvp in trip.foundTrinkets) ForagingInventoryManager.Instance?.AddTrinket(kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
foreach (var kvp in trip.foundHerbs) ForagingInventoryManager.Instance?.AddHerbLoot(kvp.Key, kvp.Value);
if (trip.carriedCrystalCarrots > 0) ForagingInventoryManager.Instance?.AddCrystalCarrots(trip.carriedCrystalCarrots);
```

**`GetRandomEnemyName` helper** (new, from the original pass):
```csharp
private static string GetRandomEnemyName(ForagingLocationDefinition location)
    => location?.enemyNames != null && location.enemyNames.Count > 0
        ? location.enemyNames[Random.Range(0, location.enemyNames.Count)]
        : "a wild creature";
```

## `BunnyInfoUI` changes (new — accessory slot)

- New accessory slot: an `Image` (the equipped accessory's icon, or an empty/placeholder state when
  `EquippedAccessory` is null) plus a click handler.
- Clicking opens a small picker — same list shape as `ForagingDispatchUI`'s old accessory step
  (`ForagingInventoryManager.GetAccessoriesInStock()` plus a "None" option to unequip), but calling
  `ForagingInventoryManager.Instance.TryEquipAccessory(bunny, chosen)` directly instead of staging a
  selection for later dispatch.
- Only shown/interactable for a resident bunny — moot point during a trip since a foraging bunny can't be
  clicked into `BunnyInfoUI` at all (see above), so no extra guard needed beyond what already exists.

## New script: `ForagingInventoryScreenUI` (new — base inventory catalog)

Ethan's ask: "there needs to be a base inventory catalog, that we can view in-game somehow." Carrots and
Gold already have their own persistent HUD display (`CarrotCountDisplay` and friends) — this screen is
specifically for everything Foraging-sourced that currently has **no visibility at all** once it's in the
base stockpile:

- Potion stock (count)
- Accessory stock **not currently equipped** (`GetAccessoriesInStock()` — already excludes equipped
  copies naturally, since those were withdrawn at equip time)
- Crystal Carrot stock (count)
- Fruit stock (per fruit asset, count)
- Trinket stock (per `(trinket, rarity)`, count — rarity shown via the Common/Fine/Rare display naming)
- Herb stock (per herb asset, count)
- Material stock (per `(material, rarity)`, count — will start empty until a Workshop room exists to
  produce any)

Read-only this pass (no sell/use actions here — selling Trinkets, feeding Fruits, and crafting Materials
are all separate future UI work, each already flagged elsewhere in this doc). Structurally the simplest
new screen in the project: one panel, one scrollable container, one generic icon+name+count row prefab,
rebuilt on open and whenever `ForagingInventoryManager.OnInventoryChanged` fires — mirrors
`ForagingScreenUI`'s own list-rebuild pattern, just simpler (no selection state, no buttons per row).
Opened via a new button — placement is an Editor task, not specified here (e.g. alongside the existing
Foraging screen's own open button).

## New script: `ForagingPreviewStage` (unchanged from original pass)

Singleton `MonoBehaviour` far outside any real camera's view (isolation by distance, e.g.
`(0, -5000, 0)`, since nothing else occupies that space). `ShowBunny(NPCBunny source)` instantiates
`source.TypeDefinition.prefab` at a spawn anchor, disables every `NPCBunny`/`Collider2D`/`Collider` in its
hierarchy, sets its Animator's `"IsMoving"` bool true, and loops its position rightward within
`loopDistance` before resetting. `ClearBunny()` destroys the current duplicate. A serialized
`flipToFaceRight` bool handles initial facing by eye (the duplicate's own `NPCBunny.bunnyFacesLeftByDefault`
is private and inaccessible with that script disabled — purely cosmetic, not worth threading through).

## New script: `ForagingTripDetailUI`

Shown/hidden via `Show(NPCBunny, ForagingTripState)` / `Hide()`, wired into every place
`ForagingScreenUI` currently sets `selectedForagingBunny` (`OnRowSelected`, the "no longer active" branch
of `RefreshActiveTrips`, `OnReturnClicked`, `Close`) — each of those now also calls `Show`/`Hide` here,
and `ForagingPreviewStage.ShowBunny`/`ClearBunny` ride along with it.

| Field | Source |
|---|---|
| Current Hitpoints | `bunny.HPValue` / `bunny.Stats.HP` |
| Current Energy | `bunny.EnergyValue` |
| Current Accessory Equipped | `trip.equippedAccessory` (icon + name, "None" if null) — persistent one-slot equip, see "Persistent accessory slot" above |
| **Current Items Found** | generic icon+count list, see below — supersedes the original pass's fixed Carrot/Potion-only layout |
| Current Gold Found | `trip.carriedGold` |
| Enemies Slain | `trip.enemiesSlain` |
| Time Spent Foraging | `trip.elapsedTripTime`, formatted `mm:ss` |
| Event log | `trip.recentLog`, newest first |
| Walking visual | `RawImage` bound to `ForagingPreviewStage`'s `RenderTexture` |

**"Current Items Found" is now a generic icon+count row list**, not fixed per-kind fields — it needs to
cover up to 7 distinct kinds (Carrot, Potion, CrystalCarrot, Fruit ×5 possible assets, Trinket ×20,
Herb ×3, Accessory) with only whatever was actually found this trip shown. Built the same
Destroy+Instantiate-per-item way `RefreshFoundAccessoryIcons` was already planned (one row prefab: icon +
name/count text), iterating: `carriedCarrots` (fixed carrot icon), `foundPotionCount` (fixed potion
icon), `carriedCrystalCarrots` (fixed crystal-carrot icon), then each non-zero entry in
`foundFruits`/`foundTrinkets`/`foundHerbs`/`foundAccessories` using that item's own `.icon`. Refreshed on
the same throttled timer as the rest of the panel.

## Editor/content work still needed (not code)

- Move `ForagingScreenUI`'s panel from right to left.
- Build the detail panel's layout and wire serialized references, including the new generic found-items
  row list.
- Author `enemyNames` per location asset (can start small, expand later).
- Build the offscreen preview stage (empty parent far from the base, spawn anchor, camera + RenderTexture,
  RawImage in the panel).
- Assign fixed Carrot/Potion/CrystalCarrot icon sprites on the detail UI component.
- Add the new accessory-slot Image + click handler + picker popup to `BunnyInfoUI`.
- Rework `ForagingDispatchUI`'s old accessory-picker step into a read-only "Currently Equipped" line.
- Build `ForagingInventoryScreenUI`'s panel/row-prefab and place its open button somewhere in the HUD.
- **Author the new content** (draft rosters below — names/flavor/icons open to change later, per Ethan):

  **5 Fruits** — names confirmed (Ethan's call):
  | Fruit | Boosted Stat |
  |---|---|
  | Peach | HP |
  | Orange | Attack |
  | Apple | Defense |
  | Banana | Speed |
  | Strawberry | Luck |

  **20 Trinkets** (draft split, craftsInto shown):
  - Common (8): Ball of Wool→Cloth, Frayed Ribbon→Cloth, Bent Nail→Metal, Rusted Washer→Metal, Acorn
    Cap→None, Smooth River Stone→None, Dandelion Puff→None, Dried Berry Cluster→None
  - Fine/Uncommon (8): Woven Burlap Scrap→Cloth, Patchwork Quilt Square→Cloth, Tarnished Silver
    Spoon→Metal, Bent Brass Key→Metal, Polished Amber Chunk→None, Mossy Pocket Watch→None, Iridescent
    Beetle Shell→None, Cracked Marble→None
  - Rare (4): Golden Chalice→Metal, Silken Tapestry Fragment→Cloth, Starlight Dew Vial→None, Fossilized
    Four-Leaf Clover→None

  **2 Materials**: Cloth, Metal — never in a loot table.

  **3 Herbs**: Mint Leaf (Common), Silverwort (Fine/Uncommon), Moonpetal (Rare).

  **Crystal Carrot**: no asset, just a `ForagingLootKind.CrystalCarrot` entry placed only in Rare bands.

## Resolved (previously open)

- Trinket flavor/split: hold the drafted 8/8/4 Common/Fine/Rare roster and craft assignments as-is; revise
  later rather than now.
- Accessory slot: one slot for now, persistent (see above), multi-slot explicitly deferred.
- Fruit names: Peach/Orange/Apple/Banana/Strawberry (see content table above).
- Workshop room shape: `IJobRoom` work room, mirrors `GardenRoom` — crafting needs an assigned, working
  bunny. Room itself still deferred/unbuilt.

## Open items / assumptions to confirm before implementation

- Exact wording/tone of log lines is a first draft, open to adjustment once seen in-game.
- Duplicate's default facing direction needs an eyeballed Inspector toggle in `ForagingPreviewStage`, not
  a derived one — cosmetic only, not a design question.
- **Trinket/Herb icons** beyond the confirmed names — Ethan's call, whenever art is ready.
- **`ForagingInventoryScreenUI`'s exact layout/placement** — functionally specified above, visual design
  and HUD button placement are Editor-time calls.
