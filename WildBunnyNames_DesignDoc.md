# Wild Bunny Name Pools — Design Doc

**Status: design only, 2026-07-28. Nothing coded yet.**

## Goal

Give each of the 5 launched bunny types (Neutral/Fire/Water/Plant/Shock) its own distinct name
identity instead of all types drawing from one shared male/female name list. Reduce the chance of a
small per-type pool repeating too often within a session by tracking which names have already been
handed out and excluding them from the draw until the whole pool is exhausted.

## What exists today

- `WildBunnyNames.cs` ([WildBunnyNames.cs](Assets/Scripts/NPC%20Bunny%20Scripts/WildBunnyNames.cs)) — one flat
  `maleNames`/`femaleNames` string array (~200 entries each), shared by every bunny type. Mixes
  several unrelated naming themes together (cute food names, fantasy names, edgy villain names,
  nature names) because it was written back when Neutral was the only type in the game.
- `GetRandomName(BunnyGender gender)` — pure uniform random pick from the relevant array, no
  memory of what's already been given out, no type awareness at all.
- Call site: `WildBunnySpawner.SpawnBunnyOfType` ([WildBunnySpawner.cs:227-229](Assets/Scripts/NPC%20Bunny%20Scripts/WildBunnySpawner.cs:227)) —
  `GetRandomGender()` then `GetRandomName(gender)`, called once per wild spawn. `chosenType` (a
  `BunnyTypeDefinition`) is already in scope there, so passing type through is a one-line change.
- No save/load system exists anywhere in this project yet (confirmed by search) — population, needs,
  XP, everything is in-memory only and resets on relaunch. This matters for the used-names tracking
  below.

## Decisions (confirmed by Ethan)

1. **Per-type pools.** Each of the 5 types gets its own male/female name arrays, authored by Ethan
   (pasted in via a separate Claude chat, then hand-reviewed for fit) — no code-side name generation
   or theming needed.
2. **Pool size target: ~40-50 names per type per gender.**
3. **No-repeat-until-exhausted mechanic.** When picking a name for a given (type, gender), exclude
   names already used this session for that exact (type, gender) pair. Once every name in that pool
   has been used, clear the used-set for that pair and start drawing from the full pool again
   (duplicates become possible again from that point, but not before the pool ran dry once).
4. **Tracking granularity is per (type, gender)**, not global and not per-type-only — a Fire male
   pool exhausting has no effect on Fire female or on any other type's pools.
5. **Gender content guideline (authoring-side, not code):** male names should stay masculine/neutral;
   female names have more leeway to be masculine/neutral/feminine, matching real-world naming
   conventions. Nothing to enforce in code — this is guidance for whoever authors/reviews the name
   lists.
6. **Persistence: in-memory only for now, with scaffolding for later.** Since no save system exists,
   the used-name tracking resets on relaunch like everything else in the project currently does. The
   tracking data will be built in an export/import-friendly shape (a plain serializable list, not
   something baked into transient-only state) so that whenever a real save system gets built, wiring
   this in is a small addition rather than a rewrite.
7. **Only the 5 types with art get pools for now** (Neutral/Fire/Water/Plant/Shock — the rest of the
   20-type roster has no prefab yet, same "data ahead of art" scaffolding pattern already used
   elsewhere in `BunnyType`). Adding a pool for a future type later should be Inspector-only, no code
   change.

## Planned architecture

- `WildBunnyNames.cs` reshaped around a `List<TypeNamePool>`, where `TypeNamePool` is a small
  serializable class: `BunnyType type; string[] maleNames; string[] femaleNames;`. Pre-seeded in the
  field initializer with 5 entries (one per launched type) so Ethan just opens the Inspector and
  pastes names into each array — no need to manually add list entries first.
- `GetRandomName(BunnyType type, BunnyGender gender)` (gains a `type` param) — looks up the matching
  `TypeNamePool`, builds the "remaining" list (pool minus that pair's used-set), refills/clears the
  used-set if remaining is empty, picks uniformly from remaining, records the pick as used, returns
  it. Falls back to `"Unnamed"` if the pool is null/empty (mirrors today's fallback behavior).
- Used-name tracking: `Dictionary<(BunnyType, BunnyGender), HashSet<string>>` in memory on
  `WildBunnyNames`, keyed per pool.
- Save scaffolding: `ExportUsedNames()` / `ImportUsedNames(...)` methods that flatten/rebuild the
  dictionary into a `List<UsedNameEntry>` (a small serializable struct: type, gender, names) — not
  wired to anything today, just present so a future save system's write/read hooks have an obvious
  place to plug in without touching the draw logic itself.
- `WildBunnySpawner.SpawnBunnyOfType` call site updates to
  `WildBunnyNames.Instance.GetRandomName(chosenType.type, gender)`.

## Non-goals

- No name generation/theming by Claude — Ethan supplies and reviews all actual name content.
- No save system build-out as part of this change — scaffolding only, per decision 6 above.
- No changes to `GetRandomGender()`'s 50/50 male/female split.
- No pools for the 15 not-yet-launched `BunnyType` entries.
