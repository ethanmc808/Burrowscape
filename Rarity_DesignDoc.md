# Rarity System Expansion — Design Doc

## Goal

1. Add two new rarity tiers above Rare: **Super Rare** and **Mythical**.
2. Every item in the game always has exactly one **fixed, intrinsic rarity**, tracked so it can drive
   future name/background color-coding. This rarity is a property of the *item*, not of where/how it
   was found.
3. A single, universal set of rarity-roll ratios (weights), tunable via the Inspector/an editor tool,
   applied the same way everywhere loot is rolled.

## Decisions already made (confirmed with Ethan)

- **Fixed vs. contextual rarity are separate concepts.** A `ForagingLootEntry.rarity` on a location's
  loot table is a *drop-weight band* — which weight bucket a table row rolls under at that specific
  location (an item can be common in one place and rare in another). An item's own `rarity` field is
  fixed forever, independent of that. Example: Moonpetal Herb is always Rare, even if some location
  rolls it under a different weight band than another location does.
- **Carrot / Potion / Crystal Carrot** get a fixed rarity even though they have no ScriptableObject
  asset today. Proposed: Carrot = Common, Potion = Uncommon, Crystal Carrot = Mythical (flagship
  collectible). Easy to retune — a single lookup table.
- **Every Trinket gets one fixed rarity**, replacing the current "found-at-rarity" model (a trinket
  stack was previously keyed by `(trinket, rarity found at)` — that composite key goes away).
- **Herb splits into 5 separate named items**, one per tier, replacing today's single "Herb" asset that
  spans all tiers via 3 icon fields:
  - Common → **Mint Leaf**
  - Uncommon → **Silverwort**
  - Rare → **Moonpetal**
  - Super Rare → **Sparkling Thistle**
  - Mythical → **Golden Lotus Petal**
- **Cloth and Metal split the same way** (5 fixed-rarity assets each), for consistency, even though
  they're crafting outputs rather than direct forage finds. Placeholder names below (open question 3
  covers whether these need to be final now).
- **New tiers get wired into the actual loot tables now**, not left for a later authoring pass — at
  least one Super Rare and one Mythical entry should be reachable in-game once this ships.

## Current architecture (for reference)

- `ForagingLootRarity` enum lives in `ForagingLocationDefinition.cs`: `{ Common, Uncommon, Rare }`.
- Rarity today is **only** a property of `ForagingLootEntry` (a loot-table row). Fruit/Trinket/
  Accessory/Material definitions have **no** rarity field at all.
- `ForagingManager.RollRarity()` does a weighted roll using 3 `[SerializeField]` floats
  (`baseCommonWeight/baseUncommonWeight/baseRareWeight`) plus an additive Luck/Treasure-Finder/
  Binoculars bonus that shifts weight from Common into Uncommon (×0.7) / Rare (×0.3).
- `GetItemFindXP(rarity)` grants XP based on the **rolled band**, not the item's identity. This does
  not change — a find's XP reflects how lucky/rare that specific roll was at that location, which is
  still a location/roll-time concept, separate from the item's own fixed identity rarity.
- `ForagingInventoryManager` stock dictionaries:
  - `trinketStock: Dictionary<(ForagingTrinketDefinition, ForagingLootRarity), int>`
  - `materialStock: Dictionary<(ForagingMaterialDefinition, ForagingLootRarity), int>`
  - `fruitStock: Dictionary<ForagingFruitDefinition, int>` (no rarity dimension — already fine)
  - `accessoryStock: Dictionary<ForagingAccessoryDefinition, int>` (no rarity dimension — already fine)
  - Carrot/Potion/Crystal Carrot are flat `int` counters, no rarity dimension at all.
- `ForagingMaterialDefinition` has `commonIcon/fineIcon/rareIcon` + `GetIcon(rarity)` — one asset,
  3 icons, spans all tiers. `ForagingInventoryManager` holds single `clothMaterial`/`metalMaterial`/
  `herbMaterial` references.
- Existing `Herb.asset`/`Cloth.asset`/`Metal.asset` **already have real icon sprites assigned** per
  tier (confirmed by reading the .asset YAML) — these need to be preserved when splitting into 5 assets
  each, not lost.

## Proposed changes

### 1. Enum + display labels
`ForagingLootRarity { Common, Uncommon, Rare, SuperRare, Mythical }` in `ForagingLocationDefinition.cs`.
`ForagingRarityDisplay.GetTierLabel` gets two more cases (`"Super Rare"`, `"Mythical"`).

### 2. Fixed rarity on every item type
- Add `public ForagingLootRarity rarity;` to `ForagingFruitDefinition`, `ForagingTrinketDefinition`,
  `ForagingAccessoryDefinition`.
- `ForagingMaterialDefinition`: replace `commonIcon/fineIcon/rareIcon` + `GetIcon(rarity)` with a single
  `icon` field + `rarity` field (same shape as Fruit/Trinket/Accessory now). `materialType` stays
  (Cloth/Metal/Herb family, orthogonal to rarity).
- New static `ForagingKindRarity.GetFixedRarity(ForagingLootKind kind)` helper for Carrot/Potion/
  Crystal Carrot (no asset to hang a field on).

### 3. Inventory stock simplification
Because rarity becomes intrinsic to the asset, the rarity dimension drops out of the dictionary keys:
- `trinketStock: Dictionary<ForagingTrinketDefinition, int>` — `GetTrinketCount(trinket)`,
  `AddTrinket(trinket, amount)`, `GetTrinketsInStock() : IEnumerable<ForagingTrinketDefinition>`.
- `materialStock: Dictionary<ForagingMaterialDefinition, int>` — same simplification.
- `clothMaterial/metalMaterial/herbMaterial` single-reference fields become 5-entry arrays indexed by
  rarity (`clothTiers[5]`, `metalTiers[5]`, `herbTiers[5]`), so crafting/foraging code can look up
  "the Cloth asset for this rarity" etc.
- `TryCraftTrinketIntoMaterial`: output material is now `(trinket.craftsInto == Cloth ? clothTiers :
  metalTiers)[(int)trinket.rarity]` — the trinket's own fixed rarity determines which tier of Cloth/
  Metal it produces (previously it was whatever rarity the trinket was *found* at).
- `AddHerbLoot(rarity, amount)` looks up `herbTiers[(int)rarity]` and adds to `materialStock` for that
  specific herb asset (Mint Leaf/Silverwort/etc.) — the loot-table entry's rolled band still picks
  *which* herb tier gets granted, same mechanism as today, just resolved to a named asset now.

### 4. `ForagingTripState` (ForagingManager.cs)
`foundTrinkets: Dictionary<ForagingTrinketDefinition, int>` (drop the `ForagingLootRarity` from the key
tuple — trinket identity alone is enough now since rarity is fixed on the asset).
`foundHerbs: Dictionary<ForagingLootRarity, int>` stays as-is (keying by rolled band is still correct —
it's resolved to a specific herb asset only at deposit time via `herbTiers`).

### 5. Rarity roll weights (ForagingManager.cs)
Add `baseSuperRareWeight`, `baseMythicalWeight` fields. Generalize the Luck/Treasure-Finder bonus
distribution from the current hardcoded 0.7/0.3 split across 2 tiers to a proportional split across all
4 non-Common tiers (each gets a share of the bonus proportional to its own base weight). Suggested
starting weights (fully tunable): `Common 65 / Uncommon 22 / Rare 9 / SuperRare 3 / Mythical 1`.

Add `xpPerSuperRareItem`, `xpPerMythicalItem` fields (suggested: 15 and 30, following the existing
1/3/8 progression).

### 6. Universal ratio-tuning editor tool
A custom Inspector (`ForagingManagerEditor.cs` in `Assets/Scripts/Editor/`) for the "Loot Rarity Roll"
section: draws the 5 weight fields plus a live-computed normalized percentage next to each (e.g.
`Common: 65  (65.0%)`), so ratios are easy to reason about without doing the math by hand. This is the
same field set already, just a nicer view — no new data model, so "universal across all locations"
falls out for free (it already was — one set of weights, used by every location's roll).

### 7. UI call-site fixes (mechanical, no behavior change beyond the above)
- `BaseInventoryScreenUI.Refresh()`: iterate `GetTrinketsInStock()`/`GetMaterialsInStock()` without the
  rarity tuple; use `trinket.rarity`/`material.rarity` for the label instead of a roll-time value.
- `ForagingTripDetailUI.RefreshFoundItems()`: same trinket-dict simplification; Herb icon lookup changes
  from `HerbMaterial.GetIcon(rarity)` to resolving the specific herb asset via
  `ForagingInventoryManager.Instance.GetHerbForRarity(rarity)` (new small accessor replacing the old
  single `HerbMaterial` property) and reading its `icon` directly.

### 8. Data generator (`ForagingDataGenerator.cs`)
- Assign `.rarity` to every generated Fruit (proposed: all Common — no existing tiering signal),
  Trinket (existing Common/Fine/Rare groupings map directly to `.rarity`), Accessory (all three existing
  accessories map to Rare, matching their current sole usage).
- Replace the single `GenerateMaterial("Herb", ...)` call with 5 calls creating Mint Leaf/Silverwort/
  Moonpetal/Sparkling Thistle/Golden Lotus Petal, each with its own `rarity` and a single `icon` —
  **reusing the exact sprite GUIDs already assigned** on the current `Herb.asset` for the first 3 tiers
  (extracted from the existing asset file), so no art gets lost. Super Rare/Mythical get no icon yet
  (nothing exists to assign).
- Same treatment for Cloth and Metal: 5 assets each, reusing existing `Cloth.asset`/`Metal.asset` icon
  GUIDs for Common/Uncommon/Rare, blank icon for the 2 new tiers.
- Old `Cloth.asset`/`Metal.asset`/`Herb.asset` get deleted (superseded by the 5-asset split); the
  generator's existing "skip if already exists" guard means re-running it won't recreate them at their
  old paths once deleted.
- Add a small number of new Trinkets at Super Rare / Mythical (proposed: 2 Super Rare, 1 Mythical,
  continuing the existing 8/8/4 taper → 8/8/4/2/1), and place at least one Super Rare and one Mythical
  entry into the existing location loot tables (e.g. Dungeon/Volcano, the higher-tier locations)
  so the new tiers are reachable immediately, per Ethan's answer.
- Update `ForagingInventoryManager`'s wiring instructions in the log message for the new array-based
  fields.

## Final decisions (confirmed by Ethan)

1. **Crystal Carrot = Rare** (not Mythical). Carrot = Common, Potion = Uncommon, unchanged.
2. **All 5 Fruits (Peach/Orange/Apple/Banana/Strawberry) = Rare.**
3. **Cloth/Metal placeholder names** — approved as proposed:
   - Cloth: Coarse Cloth (Common) / Fine Cloth (Uncommon) / Silken Cloth (Rare) / Shimmering Cloth
     (Super Rare) / Ethereal Cloth (Mythical)
   - Metal: Scrap Metal (Common) / Iron Ingot (Uncommon) / Silver Ingot (Rare) / Gold Ingot
     (Super Rare) / Starmetal Ingot (Mythical)
4. **New Trinkets** (placeholder names, matching the existing "found-treasure" naming style):
   - Super Rare: **Sunken Coral Diadem** (craftsInto: None), **Ancient Bronze Sundial** (craftsInto: Metal)
   - Mythical: **Radiant Phoenix Down** (craftsInto: None)
5. **Every location gets every rarity tier represented.** Loot tables (additions marked `+`, existing
   entries unchanged):

   | Location | Common | Uncommon | Rare | Super Rare | Mythical |
   |---|---|---|---|---|---|
   | Beach | Carrot | Potion | Accessory: Binoculars, `+`Fruit: Peach | `+`Trinket: Sunken Coral Diadem | `+`Trinket: Radiant Phoenix Down |
   | Forest | Carrot | Potion | Accessory: Backpack, `+`Fruit: Orange | `+`Trinket: Ancient Bronze Sundial | `+`Trinket: Radiant Phoenix Down |
   | Dungeon | Potion | `+`Trinket: Tarnished Silver Spoon | Accessory: Lightning Shoes, `+`Fruit: Apple | `+`Trinket: Sunken Coral Diadem | `+`Trinket: Radiant Phoenix Down |
   | Volcano | Potion | `+`Trinket: Bent Brass Key | Accessory: Backpack, `+`Fruit: Banana | `+`Trinket: Ancient Bronze Sundial | `+`Trinket: Radiant Phoenix Down |

   Strawberry and Herb-kind entries stay unplaced in any table for now (same "separate authoring pass"
   deferral the project already uses elsewhere) — easy to add later since Herb is now just another kind
   entry, same mechanism as Carrot/Potion.

## Rarity Manager editor tool (new requirement)

Ethan wants **one place** to both tune the universal roll weights *and* view/edit every item's fixed
rarity — not scattered across dozens of asset Inspectors. This means the 3 kind-level fixed rarities
(Carrot/Potion/Crystal Carrot) can no longer be a hardcoded C# switch (§2 originally proposed
`ForagingKindRarity.GetFixedRarity`) — they need to live in an editable asset instead.

**New asset**: `ForagingKindRarityConfig : ScriptableObject` — 3 fields (`carrotRarity`,
`potionRarity`, `crystalCarrotRarity`). One instance, generated at `Assets/Data/Foraging Kind
Rarities.asset`. Referenced by `ForagingInventoryManager` (`[SerializeField] private
ForagingKindRarityConfig kindRarities;`) with public read accessors, mirroring how it already holds
`clothMaterial`/`metalMaterial`/etc.

**New editor window**: `RarityManagerWindow` (`Assets/Scripts/Editor/RarityManagerWindow.cs`), menu
item `Burrowscape/Rarity Manager`. Two sections in a scrollable window:

- **Roll Weights** — finds the scene's `ForagingManager`, exposes the 5 `baseXWeight` fields via
  `SerializedObject`, with a live-computed normalized percentage drawn next to each (e.g. `Common: 65
  (65.0%)`) so the ratios are easy to reason about while tuning.
- **Item Rarities** — `AssetDatabase.FindAssets` for every `ForagingFruitDefinition`,
  `ForagingTrinketDefinition`, `ForagingAccessoryDefinition`, and `ForagingMaterialDefinition` in the
  project, grouped under a foldout per type, each row showing the asset name + an `EnumPopup` bound to
  its `rarity` field (edits go through `SerializedObject`/`EditorUtility.SetDirty` so they persist).
  Plus a small fixed section for the `ForagingKindRarityConfig` asset's 3 fields (Carrot/Potion/Crystal
  Carrot).

This supersedes the plain `ForagingKindRarity` static-switch idea from the earlier draft — replaced by
the config asset above.

## Non-goals (explicitly out of scope for this pass)

- Actual color-coding UI (name/background tint by rarity) — this pass only guarantees every item has a
  rarity to key off of later.
- Rebalancing existing Common/Uncommon/Rare weights beyond what's needed to make room for the 2 new
  tiers.
