# Fruit Feeding UI — Design Doc

## Goal

Let the player feed a found Fruit to a bunny from `BunnyInfoUI`, via a two-step flow:
1. **"Feed Fruit" button** on the bunny panel opens a picker listing every Fruit currently in base
   stock, each showing what stat it boosts.
2. Clicking a fruit in that list selects it and reveals an **"Eat Fruit"** confirm button; clicking
   that actually feeds it.

## What already exists (backend, no UI)

- `ForagingFruitDefinition` ([ForagingFruitDefinition.cs](Assets/Scripts/Foraging/ForagingFruitDefinition.cs)):
  `displayName`, `icon`, `description`, `boostedStat` (`BunnyStatType`: HP/Attack/Defense/Speed/Luck),
  `evAmount` (EV granted per fruit), plus the `rarity` field added by the rarity system.
- `ForagingInventoryManager.GetFruitsInStock()` / `GetFruitCount(fruit)` — same "stock is the catalog"
  pattern the existing Accessory picker already uses.
- `ForagingInventoryManager.TryFeedFruit(NPCBunny bunny, ForagingFruitDefinition fruit)` — the ledger
  entry point. Decrements stock by 1, calls `bunny.AddEV(fruit.boostedStat, fruit.evAmount)`, fires
  `OnInventoryChanged`. Returns false if the bunny/fruit is null or stock is already 0.
- `NPCBunny.AddEV(BunnyStatType stat, int amount)` ([NPCBunny.cs:1014](Assets/Scripts/NPC%20Bunny%20Scripts/NPCBunny.cs:1014)) — clamps to the
  per-stat cap (256) AND the total-across-all-5-stats cap (`MaxTotalEV` = 512), silently granting less
  than requested (possibly 0) if either cap is already hit. No public getter exists yet to read a
  bunny's current EV-per-stat or EV-total from outside `NPCBunny` (`GetEV`/`SetEV` are private).

## Existing UI pattern this should mirror

`BunnyInfoUI`'s **Foraging Accessory Slot** ([BunnyInfoUI.cs:72-229](Assets/Scripts/UI%20Elements/BunnyInfoUI.cs:72)) is the closest
precedent: a button toggles a picker list rebuilt from `ForagingInventoryManager` stock every time it
opens, one instantiated row per stocked item. The differences for Fruit Feeding:
- Accessory picking is a **single click = immediate equip**. Fruit feeding needs a **two-step
  confirm** (pick fruit → see detail/confirm → Eat Fruit) per Ethan's spec, since feeding is
  irreversible (consumes the fruit) where equip/unequip isn't.
- Accessory rows show only a name. Fruit rows need to show **which stat it raises** too.

## Proposed UI structure

New elements on `BunnyInfoUI`, alongside the existing accessory slot section:

- **`feedFruitButton`** (Button) — opens the fruit picker. Mirrors `sendForagingButton`'s "hidden
  entirely if not wired" optionality, and is only interactable when
  `ForagingInventoryManager.Instance.GetFruitsInStock()` has at least one entry (same spirit as
  `CanShowSendForagingButton`) — greyed out / hidden otherwise rather than opening an empty list.
- **`fruitPickerRoot`** (GameObject, toggled like `accessoryPickerRoot`) containing:
  - **`fruitPickerListContainer`** (Transform) + **`fruitPickerRowPrefab`** — one row per fruit
    *type* in stock (not one row per copy), each row displaying:
    - Icon (`fruit.icon`)
    - Name + count (e.g. "Peach x3", same `x{count}` convention `BaseInventoryScreenUI`/
      `ForagingTripDetailUI` already use)
    - Stat line: `"Boosts {boostedStat} (+{evAmount} EV)"` — reads directly off the asset, no new
      data needed
    - Clicking a row calls `OnFruitSelected(fruit)` — does NOT feed yet, just selects
  - **`fruitDetailRoot`** (GameObject, shown only once a fruit is selected) containing:
    - The selected fruit's icon/name/stat line again (larger/emphasized, confirming the choice)
    - **`eatFruitButton`** — calls `ForagingInventoryManager.TryFeedFruit(currentBunny, selectedFruit)`,
      then closes the whole picker back to the base bunny panel
    - A **back/cancel** affordance to return to the list without feeding (re-clicking `feedFruitButton`,
      or a dedicated back button — open question, see below)

### Flow

```
BunnyInfoUI panel (bunny selected)
  └─ [Feed Fruit] button
       └─ click → fruitPickerRoot opens, listing fruits in stock (name, count, stat boosted)
            └─ click a fruit row → fruitDetailRoot shows that fruit + [Eat Fruit] button
                 └─ click [Eat Fruit] → TryFeedFruit() → picker closes, bunny panel refreshes
```

### Refresh triggers

- List rebuilds every time `fruitPickerRoot` is opened (same as accessory picker) — no live
  `OnInventoryChanged` subscription needed while it's open, matching existing convention (the accessory
  picker doesn't live-update either).
- After a successful feed, if the fed fruit's stock hits 0, that row simply won't appear next time the
  picker reopens (`GetFruitsInStock()` already filters to `count > 0`).

## Decisions (confirmed by Ethan) — implemented

1. **Batch feeding**: after `Eat Fruit`, the picker returns to the fruit *list* (refreshed), not the
   plain bunny panel — the player can feed several fruits in one sitting. The main panel's `closeButton`
   still works independently at any point to dismiss everything (list, detail, and the whole panel).
2. **EV cap feedback**: fruits that would currently grant 0 EV (bunny already at the 256 per-stat or
   512 total cap for that stat) are greyed out (non-interactable) in the list, not just silently
   wasted — added `NPCBunny.CanGainEV(BunnyStatType)` (mirrors `AddEV`'s existing cap logic without
   exposing the private EV fields) for this. Reasoning: with 200+ bunnies there's no realistic way for
   the player to track per-bunny EV totals from memory, so the button itself needs to communicate it.
3. **Button placement**: not specified — the button/list/detail elements are wired in code and left
   unplaced in the Canvas; Ethan will position them manually in the Inspector.

## Implementation notes (as built)

- `NPCBunny.CanGainEV(BunnyStatType)` — new public method, same per-stat/total cap check `AddEV` uses.
- `BunnyInfoUI` gained: `feedFruitButton` (interactable only when stock has ≥1 fruit type, refreshed
  every `Update()` alongside the accessory slot), `fruitPickerRoot`/`fruitPickerListContainer`/
  `fruitPickerRowPrefab` (list step), `fruitDetailRoot`/`fruitDetailIcon`/`fruitDetailNameText`/
  `fruitDetailStatText`/`eatFruitButton`/`fruitDetailBackButton` (detail/confirm step).
- Row prefab contract: a `Button` + an `Image` child (icon) + 2 `TextMeshProUGUI` children — index 0 is
  name+count (`"Peach x3"`), index 1 is the stat line (`"Boosts HP (+4 EV)"`), read via
  `GetComponentsInChildren<TextMeshProUGUI>()` in list order (same pattern the accessory row prefab and
  `ForagingTripDetailUI`'s found-item rows already use for icon+text lookups).
- Opening the fruit picker closes the accessory picker and vice versa (mutually exclusive, avoids two
  popups stacking).
- `OnEatFruitClicked` → `ForagingInventoryManager.TryFeedFruit` → refresh feed button → `ShowFruitList()`
  (list step, not detail) — satisfies the batch-feeding decision.

## Non-goals

- No changes to `TryFeedFruit`'s ledger logic itself (stock decrement / EV grant) — this is UI-only,
  wiring the player to an already-correct backend.
- No bulk "feed all" action.
- No rarity color-coding on fruit rows yet (tracked separately per Rarity_DesignDoc.md's non-goals).
