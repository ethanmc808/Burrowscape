# Bunny Type Checklist — Design Doc

## Why this doc exists

There used to be a document listing the steps to take a bunny type from nothing to fully
implemented. It's been lost (not findable anywhere in the repo or its history as of
2026-08-14). This is a from-scratch rebuild, researched directly against the current
codebase rather than reconstructed from memory, so it reflects what's actually true today.

**Important framing:** all 20 `BunnyType` enum cases and their `BunnyTypeDefinition` assets
already exist, stat-tuned and unlock-gated (see `BunnyTypeSystem_DesignDoc.md` and
`BunnyTypePopulationCurve_DesignDoc.md`). So in practice, "implement a new bunny type" today
almost always means **finishing an already-enumerated placeholder type** — Phases 1 onward
below. Phase 0 (a genuinely new 21st+ type) is included for completeness but should rarely
be needed.

## Current roster status (as of 2026-08-14)

| Type | Enum + `BunnyTypeDefinition` asset | Art | Prefab + spawner-ready | Combat (attack + VFX) |
|---|---|---|---|---|
| Neutral | done | done | done | done (Slash) |
| Fire | done | done | done | done (Fire Ball) |
| Water | done | done | done | done (Bubble Beam) |
| Plant | done | done | done | done (Sap Drain) |
| Shock | done | done | done | done (Lightning Bolt) |
| Insect | done | **in progress** (Eyes Angry/Closed drafted, no body art yet) | not started | not started |
| Pixie | done | **in progress** (Eyes Angry/Closed drafted, no body art yet) | not started | not started |
| Toxic | done | **in progress** (Eyes Angry/Closed drafted, no body art yet) | not started | data authored ahead of art (attackName "Sludge Hurl" + VFX already assigned) |
| Melee, Stone, Mind, Ice, Sound, Air, Earth, Light, Metal, Ghost, Dark, Draco | done | not started | not started | not started |

Everything in the "not started" columns is exactly the work Phases 1–5 below cover. Re-run
this table's checks yourself with:

```bash
ls "Assets/Prefabs/Rabbits/NPC Bunnies"                     # which types have a rig+default prefab
ls "Assets/Art/Characters/Bunnies/Base Art/{Type}"           # empty = no body art yet for that type
grep "attackVFXPrefab:\|attackName:\|prefab:" "Assets/Data/Bunny Types/{Type}.asset"
```

## Phase 0 — Registering a genuinely new type (only past the current 20)

Skip this phase entirely if you're finishing one of the 20 already-enumerated types above.

1. **Add a case to the `BunnyType` enum** (`Assets/Scripts/NPC Bunny Scripts/NPCBunny.cs`,
   near the top). **Append it at the very end of the list — never insert it in the middle.**
   `BunnyType` is saved directly as a raw int enum in `SaveData` (`bunnyType`,
   `litterType`, `unlockedBunnyTypes` fields) — inserting mid-list silently reassigns every
   later type's saved int, corrupting existing saves. This is the exact same hazard
   `NotificationType.cs`'s own header comment documents for itself; `BunnyType` has no such
   comment today, so this doc is the only place the rule is written down.
2. Decide base stats (HP/Attack/Defense/Speed/Luck) and unlock `group`/`populationThreshold`,
   following the curve in `BunnyTypePopulationCurve_DesignDoc.md`. Add the type's row to that
   doc's table and to `BunnyTypeNiches_DesignDoc.md`'s "Next steps" ordering.
3. Run **`Burrowscape > Generate Bunny Type Definitions`** (`BunnyDataGenerator.cs`) — creates
   the missing `Assets/Data/Bunny Types/{Type}.asset`. Safe/idempotent: matches by `type`
   field, only fills in what's missing, never overwrites hand-tuned assets.

## Phase 1 — Art production (Illustrator / Photoshop)

1. **Palette first, in isolation**, before tail/ear/eye/mouth shape — every other
   differentiator gets judged relative to color, so locking color first avoids redoing later
   work. Use the validated workflow: duplicate the named Global swatch and manually reassign
   via `Select > Same > Fill Color` for precise 1:1 recoloring, then Photoshop
   Curves/Hue-Saturation **adjustment layers** on the actual flattened character art (not an
   equal-area swatch strip — color proportions on the real design are very uneven, e.g. body
   ~70% vs. ear/foot tips ~5%) for bulk grading. Do **not** use Illustrator's
   `Recolor Artwork` — its Assign tab reassigns by perceptual similarity, not swatch order,
   and silently shuffles colors.
2. **Identifiability rule:** every type should be distinguishable by color alone, except a
   few intentionally-similar thematic pairs (e.g. Stone/Metal, Water/Ice) where tail/ear/eye
   shape is expected to carry more of the identification weight instead. Small eye/mouth
   changes carry a surprising amount of perceived personality — worth deciding those
   differentiators deliberately, not as an afterthought.
3. **Trace/redraw the full layered part set over an existing rigged type, at the exact same
   canvas pixel dimensions** (Image → Image Size in Photoshop, not just "same size" in
   Illustrator units — a DPI mismatch makes a copy-pasted skeleton render at the wrong scale
   later). Same part structure every type shares: Eyes (Open/Closed/Angry), Mouth (+
   Mouth_Eating), Head, Ears, Arms, Thighs, Feet, Torso, Tail, etc. — only the art per part
   changes, not the structure.
4. Export to PSD, open in Photoshop, save as PSB at
   `Assets/Art/Characters/Bunnies/Base Art/{Type}/Rabbit_{Type}_Type.psb` (matches the
   existing per-type folder convention — these folders already exist for all 20 types, most
   still empty).
5. Also produce, same session if possible:
   - **Egg sprite**: `Assets/Art/Characters/Bunnies/Base Art/Eggs/Sprite_Bunny_Egg_{Type}`
     (used by `BunnyTypeDefinition.eggSprite` for the Hatchery — a litter's shared type
     selects which color egg shows).
   - **Type icon**: `Assets/Art/Icons/TypeSymbols/{Type}Icon.png` (used by
     `BunnyTypeDefinition.icon`, shown in `BunnyInfoUI`; `BunnyDataGenerator` auto-links it
     by this exact path convention if present when the generator next runs).

## Phase 2 — Unity rig import

1. Import the PSB via the **PSD Importer in Character Mode**. If the new sprite set doesn't
   have the exact same sprite count/names as the source type yet (e.g. an added part),
   temporarily hide/exclude the extra layer from import (`importHiddenLayers: 0`) so counts
   match for the next step.
2. In the **Skinning Editor**, **Copy** the skeleton from a known-good existing type's rig,
   **Paste** onto the new sprite set — preserves bone names/hierarchy/rest-pose, which is
   what keeps existing `.anim` clips valid (Animator binds by GameObject hierarchy path
   string, not asset identity). Requires source/destination to have identical sprite
   count/names.
3. **Set each sprite's pivot to the same anatomical landmark as the source** (base of ear,
   shoulder joint, etc.) before/while pasting bones for that part — pivot mismatch is the
   single most common failure mode here. For a pixel-aligned trace (every part in the same
   position/size as the source, just recolored), use **`Burrowscape > Copy Rig Data Between
   Sprites`** (`BunnyRigDataCopierWindow.cs`) → "Batch By Type Folder" to bulk-copy pivots for
   every matching part in one click; it reports and skips anything that doesn't match by
   name so those can be fixed by hand. It only copies pivot/alignment/border data, not
   skeleton/mesh/weights — those still need Skinning Editor Copy/Paste (previous step) plus
   manual Auto Geometry/Weights per type.
4. **Auto Geometry + Auto Weights** per sprite against the pasted skeleton; spot-check bendy
   joints in the Skinning Editor.
5. **Enable Auto Rebind on every swapped `SpriteSkin`.** `SpriteSkin.m_BoneTransforms` maps
   bone influence by list position, not name, by default — a sprite whose auto-regenerated
   internal bone order doesn't match the prefab's existing (position-based) list will
   visually stretch as if driven by the root bone, with no error. Auto Rebind fixes this by
   matching bones by name instead; re-trigger it by reassigning the sprite's Sprite field
   after enabling it.
6. **New rigid (non-SpriteSkin) face-overlay parts** (a custom Eyes/Mouth variant) should
   parent directly to `Bone_Head` — same pattern as the existing `Eyes` group — not to the
   static `Head` container (which only holds the SpriteSkin-driven base head sprite and never
   animates). Parenting to `Bone_Head` gets head-following for free via normal Transform
   inheritance, no per-clip animation curves needed.

## Phase 3 — Prefab & animation wiring

1. Build `Rabbit_{Type}_Idle.prefab` under
   `Assets/Prefabs/Rabbits/NPC Bunnies/{Type}_Type/`, swapping in the newly-rigged sprites
   **one `SpriteRenderer.Sprite` field at a time** (bone Transforms and
   `SpriteSkin.m_BoneTransforms` don't need to change). Test Idle/Walking in Play mode after
   each part rather than swapping everything at once — much easier to isolate which part
   broke if something stretches.
2. Create `NPC_Rabbit_{Type}_Controller.overrideController` under
   `Assets/Art/Characters/Bunnies/Animations/Controllers/`, overriding whichever clips
   actually differ from the shared base `Rabbit_Neutral_Controller.controller` (most types
   only need to override a handful of clips, e.g. `Rabbit_{Type}_Attacking.anim` once combat
   is authored — see the existing Fire/Plant/Shock/Water override controllers as examples).
3. Build `NPC_Bunny_{Type}_Default.prefab` (wraps the rig prefab + `NPCBunny` component),
   same `{Type}_Type` folder — this is the prefab that goes on `BunnyTypeDefinition.prefab`
   and gets Instantiated by the spawner.
4. Author any type-specific animation clips under
   `Assets/Art/Characters/Bunnies/Animations/{Type}/` (Attacking, etc., as needed).

## Phase 4 — Data wiring on `BunnyTypeDefinition`

Open `Assets/Data/Bunny Types/{Type}.asset` in the Inspector (or use
**`Burrowscape > Bunny Base Stats Editor`** for the stats grid across every type at once):

1. Assign **`icon`**, **`eggSprite`**, **`prefab`** (the `NPC_Bunny_{Type}_Default.prefab`
   from Phase 3).
2. Confirm **base stats** (`baseHP/Attack/Defense/Speed/Luck`) — already seeded from
   `BunnyBaseStats.xlsx` for all 20 types; hand-tune here if needed.
3. **Combat fields** — only needed if this type should actually fight in the base-invasion
   system: `attackName`, `isMelee`, `attackIntervalSeconds`, `attackBasePower`, and
   `attackVFXPrefab` (an `AttackInstance` prefab — Collider + ParticleSystem hierarchy; see
   Fire Ball and Sap Drain/Giga Drain as worked reference implementations, and
   `AttackInstance.cs`'s shared arc/trail motion fields). **Gotcha:** every impact
   `ParticleSystem` needs **Play On Awake off**, or it double-plays on spawn.
4. `group`/`populationThreshold` — already assigned per the curve in
   `BunnyTypePopulationCurve_DesignDoc.md`; only revisit for a genuinely new Phase-0 type.

## Phase 5 — Spawner & name pool wiring

1. Add the new `NPC_Bunny_{Type}_Default.prefab`'s `BunnyTypeDefinition` asset to
   **`WildBunnySpawner.bunnyTypes`** (Inspector list on the `WildBunnySpawner` component in
   `Home_Base.unity`) if it isn't already linked. `SpawnWildBunny` only ever selects from
   entries in this list with a non-null `prefab` **and** an unlocked type
   (`BunnyTypeUnlockTracker.IsUnlocked`) — a type with no prefab is silently skipped, not an
   error.
2. Add a `TypeNamePool` entry to **`WildBunnyNames.namePools`**
   (`Assets/Scripts/NPC Bunny Scripts/WildBunnyNames.cs`) with male/female name lists —
   `GetPool` falls back to null ("Unnamed") for any type without one.
3. Decide whether this type belongs in `WildBunnySpawner.baseStartingTypes` (the day-one
   starting roster). Types in that set never trigger the "New Bunny Type!" reveal
   notification (`NotificationType.NewBunnyType`) — only genuinely new arrivals outside it
   do, via `typesEverSpawned` in `SpawnBunnyOfType`.

## Phase 6 — Room / niche pairing (optional)

Not required for the type to be spawnable/playable — purely economic-niche content.

1. Decide the type's paired room and bonus, per `BunnyTypeNiches_DesignDoc.md` (the fuller
   per-type mechanic reference — check its "Next steps" ordering for what's already
   settled/unsettled for this type).
2. Add the type to that room's **`recommendedTypes`** list (`RoomBase`, visible directly in
   the room prefab's default Inspector under "Type-Match Bonus Pairing") once the room script
   itself reads it for a bonus.
3. If the pairing needs a room type that doesn't exist yet (Library, Radio Room, Cold Room,
   Crafting Room, Shrine Room, Vault Room per the niches doc), that's an entire separate
   room-build effort — see the room build system doc — not part of "implementing the bunny
   type" itself.

## Phase 7 — Traits / Passives (optional)

- **Traits** are a shared flat pool (`BunnyTraitCatalog`), not per-type — no new code or
  per-type authoring needed here at all.
- **Passives** are authored directly in the type's own `passives` list on
  `BunnyTypeDefinition` (Inspector: id, display name, description, unlock level,
  `PassiveEffectType`). Today's effect types are `None`, `HPRegenMultiplier` (Plant's
  Regrowth), and `TraitReroll` (Neutral's Adaptable). A genuinely new passive *mechanic*
  needs a new `PassiveEffectType` case plus a resolver hook in `NPCBunny` (see
  `GetPassiveHPRegenMultiplier`/`TryRerollTrait` as examples) — reusing an existing effect
  type needs no code at all.

## Phase 8 — Playtest checklist

- Console stays clean on Play — no missing-reference or null-ref warnings from the new
  prefab/assets.
- Type becomes spawnable once population crosses its `populationThreshold` (or immediately
  if 0); the "New Bunny Type!" notification fires **exactly once**, and does **not** replay
  after a save/reload of an already-unlocked type (the fix pattern for this is
  `WildBunnySpawner.SeedAlreadyRevealedTypes`, gated behind `loadingFromSave` — verify a spawn
  after reload does *not* re-trigger the reveal).
- Idle/Walking/Eating/Drinking/Sleeping/Working animations all look correct in Play mode —
  watch specifically for a part stretching toward the root bone (the SpriteSkin bone-order
  bug from Phase 2, step 5).
- If combat-enabled: the attack fires on cadence, VFX plays exactly once per hit (Play On
  Awake check), damage resolves via `CombatResolver`/`TypeChart`, no errors during an
  invasion.
- If room-paired: the type-match bonus actually applies while working in the recommended
  room.
- Save → reload round-trip: type stays unlocked (sticky), stats/traits/passives persist,
  reveal notification still doesn't replay.
