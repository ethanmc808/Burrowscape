# Bunny Type System — Design Document

## Context

Bunny "type" has never been tracked in code. `NPCBunny` (`Assets/Scripts/NPC Bunny Scripts/NPCBunny.cs`) currently only knows `Gender` and `BunnyName` (set via `SetIdentity`, `NPCBunny.cs:739-744`). `WildBunnySpawner` (`Assets/Scripts/NPC Bunny Scripts/WildBunnySpawner.cs`) picks a random prefab out of a flat `List<NPCBunny> bunnyPrefabs` with no notion of what type that prefab represents — the 5 existing prefabs (`NPC_Bunny_Neutral_Default`, `NPC_Bunny_Fire_Default`, `NPC_Bunny_Water_Default`, `NPC_Bunny_Plant_Default`, `NPC_Bunny_Shock_Default`, under `Assets/Prefabs/Rabbits/NPC Bunnies/`) are just art variants today.

**Implementation status (2026-07-22): fully implemented** as designed below — `BunnyType`, `BunnyTypeDefinition`, `BunnyStats`/`BunnyStatCalculator`, `BunnyPassiveResolver`, `BunnyTraitDefinition`/`BunnyTraitCatalog`, `BunnyTypeUnlockTracker`, the `NPCBunny` progression fields/`SetTypeAndProgression`/`LevelUp`/`AddExperience` scaffold, and `WildBunnySpawner`'s type/level-aware `SpawnWildBunny`. Not yet done — Editor/scene wiring the code can't do on its own:
- Run `Burrowscape > Generate Bunny Type Definitions` if not already run, to create/refresh the 20 `BunnyTypeDefinition` assets in `Assets/Data/Bunny Types/`.
- Add a `BunnyTypeUnlockTracker` and a `BunnyTraitCatalog` component to a persistent GameObject in `Home_Base.unity` (same place `PopulationManager`/`WildBunnyNames`/`RoomUnlockTracker` already live) — both are singletons `WildBunnySpawner` looks up via `.Instance`, and spawning silently no-ops (fails closed) without them.
- On `WildBunnySpawner` in the scene: re-assign its bunny list — the field was renamed `bunnyPrefabs` (`List<NPCBunny>`) -> `bunnyTypes` (`List<BunnyTypeDefinition>`), so the old Inspector assignment doesn't carry over. Drag in the `BunnyTypeDefinition` assets (all 20, or just the 5 Group 1 ones for now).
- No trait content exists yet — `BunnyTraitCatalog`'s list is empty until authored, so bunnies will spawn with 0 traits until then (expected, not a bug).
- **Update (2026-07-23): first real trait batch implemented.** 12 base-related traits (Energetic/Lazy, Diligent/Slacker, Cheerful/Grumpy, Glutton/Light Eater, Parched/Reservoir, Quick-Footed/Sluggish) now have real numeric effects, not just names — see "Trait effects (proof of concept)" below. Run `Burrowscape > Generate Bunny Trait Seed Data` (after adding a `BunnyTraitCatalog` component per above) to seed them into the scene. The other 16 proposed combat/quest trait names are intentionally still name-only — no combat/quest system exists yet to give them real effects, and the user wants to flesh those out + playtest before implementing.

**Update (2026-07-23): stat formula replaced by the Bunny Stat System Redesign.** The linear
`growthRate` formula described in "Stat resolution" below (and `NPCBunny.statGrowthRate`/
`BunnyStatCalculator`'s HP x2/Luck x0.5 post-multipliers) is gone, replaced by a modified
Pokemon-style floor formula with three new individuality layers — **IV** (1-32 per stat, fixed at
spawn), **EV** (0-256 per stat, earned through play — the earning mechanism doesn't exist yet, EVs
just default to 0), and **Nature/Zodiac** (one of 12 signs, ±10% on two of Attack/Defense/Speed/Luck,
never HP). See the "Stat resolution" section below (rewritten) for the actual formula, and the new
`BunnyNature.cs` (sign enum + boosted/lowered lookup) and `NPCBunny.RollIndividuality`/
`ApplyNatureEffects`. A new `Stoic` trait (`TraitEffectType.IgnoresNature`) suppresses a bunny's
Nature effect entirely without erasing its underlying sign — **still needs to be authored as an
actual entry in the `BunnyTraitCatalog` Inspector list** (this doc's code changes only add the enum
case, not the data row; same manual step as any other trait content).

**Gotcha hit and fixed (2026-07-23):** `BunnyTraitCatalog` was originally implemented in the same file as `BunnyTraitDefinition` (`BunnyTraitDefinition.cs`). It compiled fine (confirmed by grepping the compiled `Assembly-CSharp.dll` for the class name) but never showed up in Add Component — Unity's Add Component search/browse resolves entries through a `.cs` file's `MonoScript` asset, and a file only gets ONE `MonoScript`, assigned to whichever class matches the filename. `BunnyTraitCatalog` didn't, so it had no `MonoScript` for the Editor UI to find even though the type itself was real and usable from code. Fixed by moving it to its own `BunnyTraitCatalog.cs`. Lesson for any future MonoBehaviour added to this system: one file, one class, filename matching the class name — exactly what every other singleton here (`RoomUnlockTracker`, `WildBunnyNames`, `BunnyTypeUnlockTracker`) already does.

**Update (2026-08-10): Neutral base stats buffed, and full type base-utility niche pass complete.** Neutral's base stats are raised from 70/70/70/70/20 (300 total, lowest in the roster) to a flat 80/80/80/80/80 (400 total) — see the table below. This was decided alongside giving every type a real reason to be wanted in the base beyond raw combat stats (Sound gates the Radio Station, Toxic/Laboratory, Mind/Library, Earth digs deeper, etc.) — the full 20-type map, including Neutral's new self-only trait-reroll niche, is tracked separately in `BunnyTypeNiches_DesignDoc.md` rather than duplicated here, since it's a different concern (room/economy design) from this doc's scope (spawn/stat/level/trait/passive resolution). The stat table below is updated to match; nothing else in this doc changes as a result.

This doc defines the system that gives every spawned bunny a real `BunnyType`, plus the Level/Stats/Passives/Traits that come with it. Full target roster is 20 types, gated into 7 population-based unlock groups; only Group 1 (Neutral, Fire, Water, Plant, Shock) has art today, but the data model covers all 20 from the start so adding a type later is "author one asset," not "touch code."

**Full type list:** Neutral, Fire, Water, Plant, Shock, Insect, Melee, Stone, Mind, Toxic, Ice, Sound, Air, Earth, Pixie, Light, Metal, Ghost, Dark, Draco.

**Base stats** (source: `BunnyBaseStats.xlsx`, project root — HP/Attack/Defense/Speed/Luck):

| Type | HP | Attack | Defense | Speed | Luck | Total |
|---|---|---|---|---|---|---|
| Neutral | 80 | 80 | 80 | 80 | 80 | 400 |
| Fire | 70 | 130 | 70 | 100 | 70 | 440 |
| Water | 100 | 80 | 80 | 60 | 30 | 350 |
| Plant | 130 | 70 | 80 | 50 | 20 | 350 |
| Shock | 50 | 110 | 60 | 130 | 80 | 430 |
| Ice | 90 | 90 | 120 | 40 | 20 | 360 |
| Mind | 60 | 110 | 50 | 60 | 100 | 380 |
| Toxic | 90 | 90 | 90 | 60 | 20 | 350 |
| Sound | 70 | 80 | 60 | 120 | 40 | 370 |
| Insect | 50 | 70 | 90 | 100 | 70 | 380 |
| Melee | 80 | 150 | 70 | 70 | 40 | 410 |
| Stone | 90 | 80 | 130 | 30 | 60 | 390 |
| Earth | 110 | 90 | 100 | 40 | 20 | 360 |
| Air | 60 | 70 | 60 | 150 | 30 | 370 |
| Metal | 70 | 80 | 160 | 30 | 20 | 360 |
| Pixie | 110 | 50 | 50 | 80 | 120 | 410 |
| Light | 60 | 80 | 50 | 140 | 90 | 420 |
| Ghost | 50 | 90 | 50 | 110 | 70 | 370 |
| Dark | 70 | 100 | 60 | 80 | 80 | 390 |
| Draco | 90 | 140 | 100 | 80 | 70 | 480 |

The xlsx's Traits/Passives columns are blank — no content designed yet for either.

**Unlock groups** (all population thresholds are placeholders, per user):

| Group | Population | Types |
|---|---|---|
| 1 | 0 (start) | Neutral, Water, Fire, Plant, Shock |
| 2 | 25 | Insect, Melee, Stone |
| 3 | 50 | Mind, Toxic, Ice |
| 4 | 75 | Sound, Air, Earth |
| 5 | 100 | Pixie, Light |
| 6 | 150 | Metal, Ghost, Dark |
| 7 | 200 | Draco |

## Locked-in decisions

- **Stat growth**: ~~linear, `Stat(level) = BaseStat * (1 + growthRate * (level - 1))`~~ — **superseded 2026-07-23**, replaced by the Pokemon-style floor formula in "Stat resolution" below (the "wouldn't transfer cleanly" concern this bullet originally raised turned out not to block it — our base stats fit fine within the 1-256 range the floor formula assumes).
- **Leveling is spawn-time only, for now.** No XP system exists (working, questing, foraging aren't wired to any XP concept), and building one is out of scope for this pass. A wild bunny's Level is rolled once at spawn from the population ramp and never changes afterward. Everything level-dependent (Stats, which Passives are active) is a pure function of `(Type, Level)` evaluated once at spawn. This deliberately leaves room for a future XP/leveling pass to call a `LevelUp(int newLevel)`-shaped hook that just re-runs the same stat/passive resolution at the new level — no rework needed here, just a new caller.
- **Level ramp reuses `WildBunnySpawner`'s existing population ramp** (`startPopulation`/`capPopulation`, `NPCBunny Scripts/WildBunnySpawner.cs:20-26`) rather than a second independent curve — same shape as the spawn-timer Lerp, extended with a level-range Lerp so both stay in sync off one set of population knobs.
- **Type unlocks are sticky.** Once population has ever crossed a type's threshold, that type stays available even if population later drops (deaths, banishment). Mirrors `RoomUnlockTracker`'s permanent-flag pattern (`Room Scripts/RoomUnlockTracker.cs`) rather than `RoomUnlockCondition`'s live `PopulationAtLeast` re-check (`Room Scripts/RoomUnlockCondition.cs:32-34`), which would let a type disappear mid-playthrough.
- **Passives and Traits are scaffolded only.** Both get real data structures and spawn-time hooks, but no actual content (no passive effects, no trait pool) — those columns are blank in the source spreadsheet and no incompatibility rules exist yet. The system must work correctly with zero authored content (bunnies simply spawn with no traits/no passives until someone fills the catalogs in).
- **Type chart is explicitly out of scope this pass** — not even a stub. No combat/encounter system exists yet to consume it, and guessing its shape now risks building something that has to be reworked once that system is actually designed.

## Data architecture

### `BunnyType` enum

New enum, 20 cases, in a shared location (e.g. alongside the other bunny enums at the top of `NPCBunny.cs`, next to `BunnyGender`):

```csharp
public enum BunnyType
{
    Neutral, Fire, Water, Plant, Shock,
    Insect, Melee, Stone,
    Mind, Toxic, Ice,
    Sound, Air, Earth,
    Pixie, Light,
    Metal, Ghost, Dark,
    Draco
}
```

### `BunnyTypeDefinition` — one ScriptableObject asset per type

Mirrors `RoomDefinition`'s catalog pattern (`Room Scripts/RoomDefinition.cs`) exactly — one asset per entry, referenced from a serialized list, `[CreateAssetMenu]` so designers author them in the Project window:

```csharp
[CreateAssetMenu(fileName = "BunnyTypeDefinition", menuName = "Burrowscape/Bunny Type Definition")]
public class BunnyTypeDefinition : ScriptableObject
{
    public BunnyType type;
    public string displayName;

    [Header("Prefab")]
    [Tooltip("Null until this type's art/rig exists — WildBunnySpawner skips types with no prefab even if population-unlocked.")]
    public GameObject prefab;

    [Header("Base Stats")]
    public int baseHP;
    public int baseAttack;
    public int baseDefense;
    public int baseSpeed;
    public int baseLuck;

    [Header("Unlock")]
    [Tooltip("Organizational only (matches the design groupings) — actual gating is populationThreshold.")]
    public int group;
    public int populationThreshold;

    [Header("Passives (unique per type, not random)")]
    [Tooltip("Empty until content is authored. A passive is active once the bunny's level >= unlockLevel.")]
    public List<BunnyPassiveDefinition> passives = new List<BunnyPassiveDefinition>();
}

[System.Serializable]
public class BunnyPassiveDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public int unlockLevel = 1;
    // No effect/behavior hook yet — that gameplay system doesn't exist. This is a pure data tag today.
}
```

This produces up to 20 assets (one per type), same authoring shape as `RoomDefinition`'s per-room-type-and-Grade assets. Only 5 need a `prefab` assigned right now (Group 1); the rest exist with `prefab = null` and simply won't be selected until art lands — no code change needed when that happens, just assign the prefab field.

### Base stats editing workflow — built now, ahead of the rest of the system

The user flagged that every base stat above is a placeholder and needs to stay easily editable. Rather than a one-time xlsx import (re-importing on every tweak) or clicking through 20 separate assets in the Inspector, three pieces are built now, matching this codebase's existing "generator tool + Editor window" conventions (`Editor/RoomDataGenerator.cs`, `Editor/BunnyRigDataCopierWindow.cs`):

- **`BunnyTypeDefinition.cs`** (`NPC Bunny Scripts/BunnyTypeDefinition.cs`) — the ScriptableObject class as designed above, implemented in full now (not just the stats fields) so it doesn't need reshaping once the rest of the system lands on top of it.
- **`Editor/BunnyDataGenerator.cs`** — `Burrowscape > Generate Bunny Type Definitions` menu command. One-time bulk creation: seeds all 20 assets into `Assets/Data/Bunny Types/` (mirroring `RoomDefinition`'s `Assets/Data/Room Definitions/` convention) with the placeholder stats/group/threshold table from this doc, and links the 5 existing Group 1 prefabs by convention path (`Assets/Prefabs/Rabbits/NPC Bunnies/{Type}_Type/NPC_Bunny_{Type}_Default.prefab`). Safe to re-run — matches by the asset's `type` field, so it only fills in what's missing (new types added later, newly-shipped prefabs) and never overwrites stats already hand-tuned.
- **`Editor/BunnyBaseStatsWindow.cs`** — `Burrowscape > Bunny Base Stats Editor` menu command. The one-screen grid the user asked for: every type's HP/Attack/Defense/Speed/Luck (plus a computed Total) in a single editable table, grouped by unlock group, writing straight back to the `BunnyTypeDefinition` assets via `SerializedObject` (full Undo support, identical result to hand-editing each asset's Inspector). A toolbar "Save All" flushes to disk.

Per-asset Inspector editing still works too (nothing about this hides the normal Inspector) — the grid window is just the fast path for comparing/tuning many types at once. Spreadsheet re-upload was considered and dropped: it would mean re-parsing an xlsx on every tweak with no engine-side benefit over editing the numbers directly where they already live.

**To run it:** open Unity, use `Burrowscape > Generate Bunny Type Definitions` once to create the 20 assets, then `Burrowscape > Bunny Base Stats Editor` any time to view/edit them.

### `BunnyTraitDefinition` — flat pool, not per-asset

Traits are small, numerous, and edited together as a table — closer to `WildBunnyNames`' serialized-array pattern (`NPC Bunny Scripts/WildBunnyNames.cs`) than to a one-asset-per-entry catalog. A new singleton, `BunnyTraitCatalog`, holds the pool:

```csharp
public class BunnyTraitCatalog : MonoBehaviour
{
    public static BunnyTraitCatalog Instance { get; private set; }

    [SerializeField] private List<BunnyTraitDefinition> traits = new List<BunnyTraitDefinition>();
    // Empty by default — no trait content exists yet.

    public IReadOnlyList<BunnyTraitDefinition> AllTraits => traits;
}

[System.Serializable]
public class BunnyTraitDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Traits that can never co-occur with this one on the same bunny, e.g. Energetic/Lazy.")]
    public List<string> incompatibleTraitIds = new List<string>();
}
```

### Trait effects (proof of concept, implemented 2026-07-23)

The 12 base-related traits above aren't cosmetic-only anymore — `BunnyTraitDefinition` gained an `effectType` (`TraitEffectType` enum) and a flat `effectMultiplier`:

```csharp
public enum TraitEffectType
{
    None,
    EnergyDecayMultiplier, MoodDecayMultiplier, HungerDecayMultiplier,
    ThirstDecayMultiplier, MoveSpeedMultiplier, ProductionMultiplier,
}
```

`NPCBunny.SetTypeAndProgression` calls a new `ApplyTraitEffects()` once, right after `Traits` is set (well before `HasEnteredBase`, so it can never race a decay tick already in flight). For every trait whose `effectType` isn't `None`, it multiplies the matching field(s) **in place** — these are the spawned instance's own fields (every `Instantiate` gets its own copy), so there's nothing else to keep in sync:

- `EnergyDecayMultiplier` → `energyDecayPerSecond`, `energyDecayPerSecondWorking`, `energyDecayPerSecondQuesting`, `energyDecayPerSecondForaging` (every decay context, not the sleep-regen gain rate)
- `MoodDecayMultiplier` → `moodDecayPerSecondWorking`, `moodDecayPerSecondIdlePacing` (decay only — the three mood *gain* rates are untouched)
- `HungerDecayMultiplier` → `hungerDecayPerSecond` only (not the per-carrot gain)
- `ThirstDecayMultiplier` → `thirstDecayPerSecond` only (not the per-drink gain)
- `MoveSpeedMultiplier` → `moveSpeed`
- `ProductionMultiplier` → not a field mutation — stored on a new public `NPCBunny.ProductionMultiplier` property (default 1), since it needs to be read from *outside* NPCBunny

**Diligent/Slacker (Production) only wired into Garden and Water rooms** (`GardenRoom.cs`/`WaterRoom.cs`'s production coroutines now multiply their per-tick output by `bunny.ProductionMultiplier`) — deliberately **not** wired into Coal Room. Coal's power output is computed by `PowerManager` from a flat room-level rate × active-worker *headcount*, not by summing individual per-bunny amounts like Garden/Water do, so a per-bunny multiplier can't drop in the same way without restructuring that math. The user explicitly deferred this ("I don't want to mess with the power system, since it was so hard to debug") — noted as a follow-up once the type/spawn system itself has been troubleshot via playtesting.

**Sluggish correction (2026-07-23):** originally implemented as 0.8x (the mathematical inverse of Quick-Footed's 1.25x, since the user's initial wording — "moves 1.25x slower" — didn't match the "X 0.75x as slowly/much" phrasing every other slow-side trait used). User clarified they meant 0.75x, matching the rest of the pattern. Now `MoveSpeedMultiplier` 0.75, same as every other slow-side trait in this batch.

**Seeding:** `BunnyTraitCatalog`'s trait list isn't asset-backed like `BunnyTypeDefinition`, so a new `Editor/BunnyTraitDataGenerator.cs` (`Burrowscape > Generate Bunny Trait Seed Data`) writes the 12 seed entries directly into whichever `BunnyTraitCatalog` is in the open scene via `SerializedObject`, matching existing entries by `id` so it's safe to re-run without duplicating or stomping hand-edited values.

The other 16 proposed combat/quest trait names (Brave/Cowardly, Mighty/Frail, etc.) are intentionally **not** in `TraitEffectType` or the seed data yet — no combat/quest system exists for them to hook into, and the user wants to flesh them out and playtest before implementing, per their own call.

### `BunnyTypeUnlockTracker` — sticky unlock ledger

Mirrors `RoomUnlockTracker` (`Room Scripts/RoomUnlockTracker.cs`), but self-latches instead of waiting for an external system to call `UnlockPermanently` — nothing else currently tracks "type has been unlocked":

```csharp
public class BunnyTypeUnlockTracker : MonoBehaviour
{
    public static BunnyTypeUnlockTracker Instance { get; private set; }

    private readonly HashSet<BunnyType> unlockedTypes = new HashSet<BunnyType>();

    // Called by WildBunnySpawner before every spawn. Latches permanently once true — matches the
    // "sticky" decision above. Cheap enough (~20 comparisons) to just re-check every spawn rather
    // than subscribing to PopulationManager.OnPopulationChanged.
    public bool IsUnlocked(BunnyTypeDefinition def)
    {
        if (unlockedTypes.Contains(def.type)) return true;

        int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
        if (population >= def.populationThreshold)
        {
            unlockedTypes.Add(def.type);
            return true;
        }
        return false;
    }
}
```

## Stat resolution (replaced 2026-07-23 — see Bunny Stat System Redesign)

**Superseded.** The linear `growthRate` formula this section originally described is gone. Current
formula, a modified Pokemon-style floor formula:

```
HP     = floor((2*Base + IV + floor(EV/4)) * Level / 50) + Level + 8
Stat   = floor((floor((2*Base + IV + floor(EV/4)) * Level / 50) + 4) * NatureMultiplier)   // Attack, Defense, Speed, Luck
```

`NatureMultiplier` is `1.1` on the bunny's Nature-boosted stat, `0.9` on its Nature-lowered stat, `1.0`
on the other two (or on all four if the bunny has the `Stoic` trait). Every stat (HP included) is
clamped to a minimum of 1 after the full formula resolves. `Base` is `BunnyTypeDefinition.baseHP`/
etc., unchanged/un-multiplied — the old HP x2/Luck x0.5 post-multipliers are gone along with
`growthRate`.

Split into two passes (see Locked-in decisions / Option B), matching how Traits already work as a
separate post-`Resolve` step:

- `BunnyStatCalculator.Resolve` (`BunnyStats.cs`) — pure function of `(type, level, IV*5, EV*5)` →
  `BunnyStats`, with `NatureMultiplier` implicitly `1.0` (Nature isn't known at this layer).
- `NPCBunny.ApplyNatureEffects` — run right after `ApplyTraitEffects` (same spot, before
  `HasEnteredBase` can ever be true), applies the real ±10%/x1 per the bunny's `Nature` (or x1 across
  the board if a `TraitEffectType.IgnoresNature` trait is present) and re-clamps to a minimum of 1.

IVs (1-32 per stat) and Nature (12-sign enum, see `BunnyNature.cs`) are rolled once at spawn via
`NPCBunny.RollIndividuality()`, called by `WildBunnySpawner` before `Resolve` (so the IVs exist in
time to feed into it). EVs (0-256 per stat, 512-total cap across all 5 — see `NPCBunny.MaxTotalEV`)
default to 0; no EV-granting mechanism exists yet, so nothing sets these today.

Note: this `Speed` stat is unrelated to `NPCBunny.moveSpeed` (`NPCBunny.cs:50`, the literal walk-animation speed) — see Open Items.

## Level resolution (population ramp)

`WildBunnySpawner.GetSpawnIntervalRangeSeconds` (`WildBunnySpawner.cs:72-83`) already computes a normalized ramp position `t` between `startPopulation` and `capPopulation`. Refactor that `t` computation into a small shared helper so both spawn-timing and level-picking read from the exact same ramp:

```csharp
private float GetPopulationRampT()
{
    int population = PopulationManager.Instance != null ? PopulationManager.Instance.TotalResidents : 0;
    float denominator = capPopulation - startPopulation;
    return denominator > 0f
        ? Mathf.Clamp01((population - startPopulation) / denominator)
        : (population >= capPopulation ? 1f : 0f);
}
```

New fields alongside the existing wait-time ones:

```csharp
[Header("Population-Based Level Ramp")]
[SerializeField] private int startMinLevel = 1;
[SerializeField] private int startMaxLevel = 5;
[SerializeField] private int capMinLevel = 40;
[SerializeField] private int capMaxLevel = 50;
```

```csharp
private int RollSpawnLevel()
{
    float t = GetPopulationRampT();
    int minLevel = Mathf.RoundToInt(Mathf.Lerp(startMinLevel, capMinLevel, t));
    int maxLevel = Mathf.RoundToInt(Mathf.Lerp(startMaxLevel, capMaxLevel, t));
    return Mathf.Clamp(Random.Range(minLevel, maxLevel + 1), 1, 50);
}
```

## Trait resolution

Wild-spawned bunnies are always adults → always roll 2 traits. (A future egg/breeding system would call the same function with count 1 for a newly-hatched kid — see Open Items; nothing wires that path yet since breeding doesn't exist.)

```csharp
public static List<BunnyTraitDefinition> RollTraits(int count)
{
    var pool = new List<BunnyTraitDefinition>(BunnyTraitCatalog.Instance.AllTraits);
    var chosen = new List<BunnyTraitDefinition>();

    while (chosen.Count < count && pool.Count > 0)
    {
        int i = Random.Range(0, pool.Count);
        BunnyTraitDefinition candidate = pool[i];
        pool.RemoveAt(i);

        bool conflicts = chosen.Exists(c =>
            c.incompatibleTraitIds.Contains(candidate.id) || candidate.incompatibleTraitIds.Contains(c.id));

        if (!conflicts) chosen.Add(candidate);
    }
    return chosen; // empty today — pool is empty until traits are authored
}
```

## Passive resolution

Pure function of `(type, level)` — every passive on the type definition whose `unlockLevel <= level`:

```csharp
public static List<BunnyPassiveDefinition> ResolvePassives(BunnyTypeDefinition def, int level)
{
    return def.passives.Where(p => p.unlockLevel <= level).ToList();
}
```

## `NPCBunny` changes

Add fields/properties alongside the existing `Gender`/`BunnyName` (`NPCBunny.cs:132-133`):

```csharp
public BunnyType Type { get; private set; }
public int Level { get; private set; }
public BunnyStats Stats { get; private set; }
public IReadOnlyList<BunnyTraitDefinition> Traits { get; private set; }
public IReadOnlyList<BunnyPassiveDefinition> ActivePassives { get; private set; }
```

New setter, mirroring `SetIdentity`'s shape (`NPCBunny.cs:739-744`), called once at spawn:

```csharp
public void SetTypeAndProgression(BunnyType type, int level, BunnyStats stats,
    List<BunnyTraitDefinition> traits, List<BunnyPassiveDefinition> passives)
{
    Type = type;
    Level = level;
    Stats = stats;
    Traits = traits;
    ActivePassives = passives;
}
```

### Leveling scaffold (built now, unwired today)

**Update (2026-07-23): XP curve and earning mechanism now designed** — see `Foraging_DesignDoc.md`.
Foraging is the source that finally wires up `AddExperience`/`LevelUp` below (Pokemon "Fast" group
curve, `0.8 * Level^3` cumulative). Nothing described here changed; this section is still accurate as
the scaffold Foraging plugs into.

Per the user: Level stays spawn-time-only for actual gameplay this pass, but the *hooks* a future XP system will need should exist in code now, not just be a note in this doc — matching how this codebase already scaffolds not-yet-built states (e.g. `energyDecayPerSecondQuesting`/`energyDecayPerSecondForaging` and their commented-out `switch` cases in `GetEnergyDecayRate()`, `NPCBunny.cs:74-75, 279-289` — fields and extension points exist ahead of the Quest/Forage states themselves).

Two pieces, both real and callable, neither called by anything yet:

```csharp
// Recomputes Stats/ActivePassives at a new level, reusing the exact same resolver functions used at
// spawn (BunnyStatCalculator.Resolve / ResolvePassives) — a future XP system's only integration point
// is calling this once it decides a level-up happened. Traits are deliberately NOT touched here: trait
// gain is bound to breeding/kid-bunny rules (see Open Items) that don't exist yet either, and this
// method's job is strictly "level changed, refresh what depends on it."
//
// Superseded 2026-07-23: statGrowthRate is gone (see "Stat resolution" above) — LevelUp now passes
// the bunny's own IVs/EVs into Resolve and re-runs ApplyNatureEffects afterward. Shown here in its
// original form for history; see NPCBunny.cs for the actual current body.
public void LevelUp(int newLevel)
{
    if (newLevel <= Level) return;

    Level = newLevel;
    Stats = BunnyStatCalculator.Resolve(typeDefinition, Level, statGrowthRate);
    ActivePassives = BunnyPassiveResolver.ResolvePassives(typeDefinition, Level);
}
```

```csharp
[Header("Experience (unwired — no XP curve exists yet)")]
[SerializeField] private float experience = 0f;
[Tooltip("Placeholder only. No system currently calls AddExperience, and no XP-required-per-level curve exists to decide when LevelUp should fire.")]
[SerializeField] private float experienceToNextLevel = 0f;

// TODO: no caller yet (working/questing/foraging don't grant XP today) and no curve to compare
// `experience` against `experienceToNextLevel` — this just accumulates a number until both exist.
// Once a real XP curve is designed, this is where it calls LevelUp(Level + 1) and resets the count.
public void AddExperience(float amount)
{
    experience += amount;
}
```

`SetTypeAndProgression` also needs to stash `typeDefinition` (the `BunnyTypeDefinition` the bunny spawned from) as a private field so `LevelUp` can re-resolve against the right base stats/passives later — not currently needed for anything else, but `LevelUp` can't work without it.

## `WildBunnySpawner` changes

Replace the flat `List<NPCBunny> bunnyPrefabs` (`WildBunnySpawner.cs:7`) with `[SerializeField] private List<BunnyTypeDefinition> bunnyTypes;` (all ~20 assets assigned in Inspector, same authoring gesture as today's prefab list). `SpawnWildBunny()` (`WildBunnySpawner.cs:85-126`) becomes:

1. Filter `bunnyTypes` to those with `prefab != null && BunnyTypeUnlockTracker.Instance.IsUnlocked(def)`.
2. Bail with a warning if the filtered list is empty (mirrors the existing empty-`bunnyPrefabs` guard, `WildBunnySpawner.cs:88-92`).
3. Pick one uniformly at random (equal weight among everything currently available — see Open Items).
4. Instantiate `chosenType.prefab` (replaces `chosenPrefab` today).
5. Roll level via `RollSpawnLevel()`.
6. Resolve stats via `BunnyStatCalculator.Resolve(chosenType, level, growthRate)`. **Superseded 2026-07-23**: also call `newBunny.RollIndividuality()` before this step, and pass the rolled IVs/EVs (see "Stat resolution" above) instead of `growthRate`, which no longer exists.
7. Roll gender/name (unchanged — existing `WildBunnyNames` calls).
8. Roll traits via `RollTraits(2)` (adult wild spawn).
9. Resolve passives via `ResolvePassives(chosenType, level)`.
10. Call `newBunny.SetIdentity(gender, name)` (unchanged) and the new `newBunny.SetTypeAndProgression(chosenType.type, level, stats, traits, passives)`.
11. Continue into the existing `RandomizeStartingNeeds` / gate-queue flow unchanged.

## Open items / assumptions to confirm before implementation

- **All population thresholds (25/50/75/100/150/200) and level ranges (1-5 → 40-50) are placeholders**, per the user's own framing — expect to retune both once Group 1 is playtested.
- ~~**`growthRate` default (`0.02`) is a guess**~~ — moot, `growthRate` no longer exists (see "Stat resolution"). The new formula's own tuning knob is each type's authored Base stats plus IV/EV/Nature spread, not a single shared constant — same "needs a real playtest pass" caveat applies once a stats UI exists.
- **Equal-weight type selection** among unlocked+available types is assumed for step 3 above (not stated by the user). If some types should be rarer/more common even within the same unlocked group, this needs a per-type weight field on `BunnyTypeDefinition` instead — cheap to add later, flagging now so it's a deliberate choice rather than an accident.
- **`Speed` (battle stat) vs. `moveSpeed` (`NPCBunny.cs:50`, literal walk animation speed) are unrelated fields that happen to share a name.** Not wiring the new Speed stat to actual movement in this pass — open question for later whether a high-Speed type should visually move faster.
- **XP/leveling gameplay is deferred, but the hooks are built now.** This pass only ever resolves Stats/Passives once, at spawn, from a level that's fixed for the bunny's lifetime — nothing calls `LevelUp` or `AddExperience` yet, and no XP-per-level curve exists. A future XP pass just needs to (a) design that curve, (b) call `AddExperience` from wherever XP should be earned (working? quests? foraging?), and (c) call `LevelUp` once the curve says a level was crossed. No rework of `LevelUp` itself or the stat/passive resolvers should be needed.
- **Egg/breeding path now exists** — see the Breeding System plan. Kid bunnies get exactly 1 trait via `BunnyTraitCatalog.RollInheritedTrait` (20% mom / 20% dad / 60% `RollTraits(1)`, not a plain uniform roll), resolved at `HatcheryRoom.HatchEgg`. `PopulationManager.ResidentCategory.Egg` is now wired end-to-end (reserved at conception, converted to InBase at hatch). Still open: level-up gaining a new trait later (`LevelUp` still deliberately doesn't touch Traits — see its own comment) and kid-bunny growth-to-adult (no timer exists yet, see `NPCBunny.IsKidBunny`'s own TODO).
- **Passive "effects"** (what a passive actually *does* mechanically) are entirely undesigned — this pass only stores/resolves data tags (id, name, description, unlock level), no gameplay hook. Same for trait effects.
- **Trait pool and incompatibility pairs are empty** until authored in `BunnyTraitCatalog`. The roll function degrades gracefully to 0 traits with an empty pool — not an error state.
- **Type chart** — intentionally not designed or stubbed here at all; revisit once whatever system consumes it (combat? foraging encounters?) actually exists.
