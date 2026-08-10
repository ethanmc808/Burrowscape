# Bunny Type Passives — Implementation Plan

## Context

Every bunny type is meant to have a bespoke Passive, but only 2 of 20 have ever been implemented
(Plant's Regrowth, Neutral's Adaptability) — everything else is `passives: []` on its
`BunnyTypeDefinition` asset. Ethan finalized the full 20-passive roster in this conversation (table
below) after an earlier brainstorm session. The goal of this change is to turn that finalized list into
real gameplay: wire each passive into the actual combat/economy code paths it needs, following the
patterns this codebase already uses for the two existing passives and for `StatusEffectController`/
`GuardBuffController` (the established "optional component, chained at the point of use" idiom).

This was scoped through three parallel Explore passes over the combat/passive/status architecture, a
Plan pass that produced a concrete file-by-file design, and my own direct verification of the highest-risk
call sites the Plan pass cited (all confirmed correct except one, noted in §7). Four open design questions
were resolved directly with Ethan (see "Decisions" below) — follow those, don't re-relitigate them.

## The 20 finalized passives

| Type | Name | Description |
|---|---|---|
| Neutral | Adaptability | Re-roll one trait. 30 min CD. *(already implemented — only the CD value changes)* |
| Fire | Firey Temper | When hit by a crit, user's next attack is a guaranteed crit. |
| Plant | Regrowth | Heals 5% Max HP/sec while in combat. 3x normal passive regen out of combat. *(out-of-combat half already implemented)* |
| Water | Cleanse | Automatically cleanses an ally's status effect during battle (self included). 7s CD. |
| Shock | Overcharge | Each landed hit cuts 1s off the next attack's cooldown. Caps at 3 hits, resets on a miss. |
| Air | Nimble | Each dodged attack gives +20% Speed. Caps at 3 dodges (+60%). |
| Dark | Star Favor | At ≤50% Max HP: Luck +100%, and attacks that land reduce the target's Luck by 75% for 5s ("Eclipse"). Both halves share the same ≤50% HP gate — neither is always-on. |
| Draco | Dragon Scales | Immune to all status effects. |
| Earth | Stamina | Each hit taken gives +20% Defense. Caps at 3 hits (+60%). |
| Toxic | Potent Poison | +50% damage vs. a target that's currently Poisoned. |
| Ice | Frosty | On being hit by a melee attack, 35% chance to Chill the attacker for 3s. |
| Stone | Bulwark | +30% Defense to every ally (self included) during battle. |
| Insect | Swarm | +25% Attack & Defense per ally fighting alongside it. Caps at 3 allies (+75%). |
| Mind | Telepathy | Flat 30% chance to fully negate an incoming attack, on top of the normal accuracy roll. |
| Light | Light Speed | Attacks never miss. |
| Metal | Durable | Immune to crits. |
| Pixie | Fairy's Blessing | Auto-heals whichever ally (self included) first drops to ≤50% Max HP, by 30% of their Max HP. 7s CD. |
| Sound | Echo | Attack hits every enemy in the room. |
| Ghost | Second Life | A hit that would faint the user instead leaves it at 1 HP; can't faint for the next 5s. 1 min CD. |
| Melee | Fighting Spirit | At ≤50% Max HP, Attack +50%. |

## Decisions already made with Ethan — follow exactly

1. **Ice's "Chill can stack, no cap"** → Keep it simple: re-applying Chill just resets its duration timer,
   which `StatusEffectController.ApplyStatus` already does unconditionally today. **No stacking magnitude
   code is needed at all.** The only new behavior is *direction*: Frosty is the defender applying Chill
   back onto the attacker (today status only ever flows attacker→defender).
2. **Pixie's Fairy's Blessing targeting** — reactive threshold watch, not a periodic scan: while off
   cooldown, continuously watch defending allies (self included); the *first* one whose HP drops to ≤50%
   is healed, then the 7s CD starts.
3. **Water's Cleanse targeting** — reactive watch: while off cooldown, cleanse whichever ally first has an
   active status; if the user itself is statused, it always takes priority; otherwise pick randomly among
   currently-statused allies.
4. **Cooldown tracking** — bespoke `private float` field per ability, ticked in `Update()`, exactly
   mirroring the existing `traitRerollCooldownRemaining` pattern. **Do not** build a generic cooldown
   dictionary.
5. **Passive UI** — build a small **generic** passive-detail row (icon + description, cooldown readout for
   the 3 cooldown-based ones: Water/Pixie/Ghost) that replaces the current flat comma-joined name string in
   `BunnyInfoUI`, shown uniformly for every passive — not another one-off bespoke block like Adaptability's
   trait-picker got.

## Status effect model — primary vs. secondary (clarified with Ethan)

Two distinct tiers, don't conflate them:
- **Primary status effects** — the existing 5 (Burn/Poison/Chill/Paralyze/Sleep), tracked in
  `StatusEffectController.activeStatus`. Still single-slot, still mutually exclusive, still "a new one
  replaces the old one" — unchanged by anything in this plan.
- **Secondary status effects** — any other stat-increasing/decreasing effect (self-buffs like Air's Nimble
  or Dark's own Star Favor Luck buff, or effects inflicted onto another combatant like Dark's Eclipse or
  Stone's Bulwark aura). **Unlimited concurrent secondary effects, from different sources, all compose
  together rather than overwriting** — e.g. a bunny could be under an Attack buff from one source and a
  Speed buff from another simultaneously, and both apply. This is exactly why Eclipse's Luck-debuff (§3)
  was designed as its own independent field pair on `StatusEffectController` rather than reusing
  `activeStatus`'s single slot — that pattern (a dedicated field/method per secondary effect, chained
  alongside the others at the point of use, never sharing the primary slot) is the general rule for
  **every** secondary effect in this feature (Nimble's Speed stacks, Stamina's Defense stacks, Star
  Favor's Luck buff, Bulwark's Defense aura, Eclipse's Luck debuff, Swarm's Attack/Defense scaling) — all
  of them already independently chained through `PassiveEffectController`'s or `StatusEffectController`'s
  separate `Modify*` methods, so they already compose correctly with each other and with the 5 primary
  statuses. No redesign needed elsewhere in this plan beyond keeping this rule explicit for whoever
  implements it.

**Same-source dedup rule (clarified with Ethan):** if the *same* named secondary effect would be applied
by more than one source at once (e.g. two Stone bunnies both defending the same room, or a target hit by
Eclipse twice), it does **not** stack in magnitude — it's a single named effect, present or refreshed, not
counted per-instance. Different named effects (different passives, different stats) still freely coexist.
Concretely, this is already how every secondary effect in this plan is built, with no extra dedup code
needed, precisely *because* each one is a single dedicated field/check rather than a shared list — one
name, one slot, by construction:
- **Bulwark** is a boolean "is any Bulwark-having ally present" scan (`PassiveEffectController.ModifyDefense`,
  §4) — confirmed: 2 Stone bunnies in the same room still grant the ally a flat +30% Defense, not +60%.
- **Eclipse** is a single overwritten field pair on `StatusEffectController` (§3) — confirmed: reapplying
  it (by the same or a different Dark bunny) just refreshes the 5s timer, never compounds the -75%.
Each secondary effect should be clearly labeled by its actual passive name in code (comments/identifiers
— "Eclipse", "Bulwark", "Nimble", not generic terms) so it stays traceable if more get added later.

## Architecture

**New component — `PassiveEffectController`** (`Assets/Scripts/Combat Scripts/PassiveEffectController.cs`),
an optional `MonoBehaviour` added to bunny prefabs (not enemies), looked up via `GetComponent` in
`CombatResolver.ResolveHit` exactly like `StatusEffectController`/`GuardBuffController` already are. It owns
every **per-hit** modifier/trigger passive: Fire, Air, Earth, Dark, Melee, Toxic, Mind, Light, Metal, Draco,
Ice, Shock's streak, Stone's aura, Insect's ally-count scaling. Small combat-session state (Shock's streak,
Air's dodge stacks, Earth's hit stacks, Fire's pending-crit flag) lives here too, reset via a new
`ResetCombatStacks()` called from `NPCBunny.BeginDefending` (`NPCBunny.cs:4028`).

**Reactive tick-based passives stay directly in `NPCBunny.Update()`**, bespoke-field style like
`traitRerollCooldownRemaining`: Water's Cleanse, Pixie's Fairy's Blessing, Ghost's Second Life
cooldown/immune-window, Plant's new in-combat heal branch. These fire independent of any specific attack, so
they don't belong in `CombatResolver`.

**Shared helper on `NPCBunny`** (generalizes the existing `GetAdaptablePassive`/`GetPassiveHPRegenMultiplier`
live-scan idiom instead of re-implementing it 15+ times):
```csharp
public BunnyPassiveDefinition GetDiscoveredPassive(PassiveEffectType type)
{
    if (typeDefinition == null || typeDefinition.passives == null) return null;
    foreach (BunnyPassiveDefinition p in typeDefinition.passives)
        if (p != null && p.effectType == type && p.discovered && Level >= p.unlockLevel)
            return p;
    return null;
}
```

## File-by-file changes

### 1. `Assets/Scripts/Combat Scripts/CombatBalanceConfig.cs` (+ nothing needed in the Editor generator)
Add one `[Header("Passives — <Type> (<Name>)")]` block per passive with a numeric knob, following the
existing convention exactly (verified `Editor/CombatBalanceConfigGenerator.cs:14-31` just does
`ScriptableObject.CreateInstance<CombatBalanceConfig>()` and no-ops if the asset already exists — new
fields on the class get their C# default via normal Unity serialization the next time the Editor
reimports the asset; **no generator code change needed**, just re-tune in the Inspector if desired).

Fields needed (name — default, matching the spec numbers):
- Plant: `regrowthInCombatHealFractionPerSecond` = 0.05
- Shock: `overchargeCooldownReductionPerHit` = 1f, `overchargeMaxStacks` = 3
- Air: `nimbleSpeedBonusPerDodge` = 0.20, `nimbleMaxStacks` = 3
- Dark: `starFavorHPThresholdFraction` = 0.5, `starFavorLuckBonus` = 1.0, `eclipseLuckDebuffMultiplier` = 0.75, `eclipseLuckDebuffDurationSeconds` = 5f
- Earth: `staminaDefenseBonusPerHit` = 0.20, `staminaMaxStacks` = 3
- Toxic: `potentPoisonDamageMultiplier` = 1.5
- Ice: `frostyChillChance` = 0.35, `frostyChillDurationSeconds` = 3f
- Stone: `bulwarkAllyDefenseBonus` = 0.30
- Insect: `swarmStatBonusPerAlly` = 0.25, `swarmMaxAllies` = 3
- Mind: `telepathyNegateChance` = 0.30
- Pixie: `fairysBlessingHealFractionOfMaxHP` = 0.30, `fairysBlessingTriggerHPThresholdFraction` = 0.5
- Ghost: `secondLifeImmuneWindowSeconds` = 5f
- Melee: `fightingSpiritHPThresholdFraction` = 0.5, `fightingSpiritAttackBonus` = 0.5

No field needed for Fire, Draco, Light, Metal, Sound, Water, Neutral (pure booleans, or the number is
already covered by `abilityCooldownSeconds` — see next point).

**Cooldown lengths (Water 7s / Pixie 7s / Ghost 60s / Neutral 1800s) reuse the existing
`BunnyPassiveDefinition.abilityCooldownSeconds` field on each type's asset** — that field already exists
for exactly this purpose (it's what Neutral's Adaptability uses today). `CombatBalanceConfig` is for
cross-cutting mechanic constants, not per-type cooldown lengths.

### 2. `Assets/Scripts/NPC Bunny Scripts/BunnyTypeDefinition.cs`
- Add 18 new `PassiveEffectType` cases (reuse `HPRegenMultiplier` for Plant and `TraitReroll` for Neutral
  unchanged): `GuaranteedCritAfterBeingCrit`, `CleanseAlly`, `AttackSpeedStreak`, `DodgeSpeedStack`,
  `LowHPLuckBoost`, `StatusImmunity`, `HitDefenseStack`, `BonusDamageVsStatusedTarget`,
  `ChillAttackerOnMeleeHitTaken`, `AllyDefenseAura`, `AllyCountAttackDefenseScaling`, `NegateAttackChance`,
  `NeverMiss`, `CritImmunity`, `HealAllyBelowHalfHP`, `AoEAttack`, `SurviveFatalHit`, `LowHPAttackBoost`.
- Add `public Sprite icon;` to `BunnyPassiveDefinition`, same null-tolerant convention as
  `BunnyTypeDefinition.icon`/`eggSprite` ("shown in BunnyInfoUI's passive row; null until authored, the
  Image just hides itself").

### 3. `Assets/Scripts/Combat Scripts/StatusEffectController.cs`
Two additions, both on this component since it's already attached to both `NPCBunny` and `EnemyInstance`
(per its own header comment — "put it on any NPCBunny or EnemyInstance GameObject"):

- Backward-compatible optional-duration overload for Frosty's non-standard 3s Chill (all 5 existing
  single-argument call sites keep working unchanged):
  ```csharp
  public void ApplyStatus(StatusEffectType type, float? durationOverride = null)
  {
      activeStatus = type;
      tickTimer = 0f;
      remainingDuration = durationOverride ?? type switch { /* existing cases, unchanged */ };
  }
  ```
- **New independent side-channel for Dark's Eclipse Luck-debuff.** Eclipse isn't one of the 5 official
  `StatusEffectType` values and must be able to coexist with whatever *is* in `activeStatus` (an
  Eclipsed enemy that's also Poisoned shouldn't lose either effect) — so it's a second, separate pair of
  fields, not routed through `activeStatus`:
  ```csharp
  private float eclipseLuckDebuffRemaining;
  private float eclipseLuckDebuffMultiplier; // 1f - eclipseLuckDebuffMultiplier config value, stored at apply time

  public void ApplyLuckDebuff(float multiplier, float durationSeconds)
  {
      eclipseLuckDebuffMultiplier = multiplier;
      eclipseLuckDebuffRemaining = durationSeconds;
  }

  public int ModifyLuck(int baseLuck) =>
      eclipseLuckDebuffRemaining > 0f ? Mathf.RoundToInt(baseLuck * (1f - eclipseLuckDebuffMultiplier)) : baseLuck;
  ```
  Ticked in the existing `Update()` alongside `remainingDuration`, independently:
  `if (eclipseLuckDebuffRemaining > 0f) eclipseLuckDebuffRemaining -= Time.deltaTime;`

### 4. New `Assets/Scripts/Combat Scripts/PassiveEffectController.cs`
```csharp
public class PassiveEffectController : MonoBehaviour
{
    private NPCBunny owner;
    private int overchargeStreak;       // Shock
    private int nimbleDodgeStacks;      // Air
    private int staminaHitStacks;       // Earth
    private bool guaranteedCritPending; // Fire

    private void Awake() => owner = GetComponent<NPCBunny>();

    public void ResetCombatStacks() { overchargeStreak = 0; nimbleDodgeStacks = 0; staminaHitStacks = 0; guaranteedCritPending = false; }

    // Attacker-side
    public int ModifyLuck(int baseLuck);                                        // Dark's own Star Favor self-buff (+100% Luck, ≤50% HP only)
    public bool ShouldTriggerEclipse();                                          // Dark — true only while owner.Type == Dark, Star Favor discovered, and HPValue <= Stats.HP * cfg.starFavorHPThresholdFraction
    public bool ConsumeGuaranteedCrit();                                        // Fire
    public bool HasLightSpeed();                                                // Light
    public float GetPotentPoisonMultiplier(StatusEffectController defenderStatus); // Toxic
    public void OnOwnAttackResolved(bool hit);                                  // Shock

    // Defender-side
    public void OnHitByCrit();                  // Fire (arms its own future guaranteed crit)
    public bool IsCritImmune();                 // Metal
    public bool TryNegateAttack();               // Mind
    public bool IsStatusImmune();                // Draco
    public void OnDodgedAttack();                // Air
    public void OnHitTaken();                    // Earth
    public bool TryRollFrostyChillOnAttacker();   // Ice

    // Stat modifiers, chained exactly like StatusEffectController/GuardBuffController
    public int ModifyAttack(int baseAttack);     // Melee, Insect
    public int ModifyDefense(int baseDefense);   // Earth, Insect, Stone (Stone scans DwellerRoster.Instance.GetBunniesDefendingRoom(owner.DefendingRoom) for an ally with Bulwark)
    public int ModifySpeed(int baseSpeed);       // Air
}
```
Add this component to the NPC Bunny prefab(s) in the Unity Editor (an asset-authoring step, not code —
flag this explicitly when implementing, it's easy to forget and the component degrades silently/inertly
if missing, same as an unattached `StatusEffectController`).

### 5. `Assets/Scripts/Combat Scripts/CombatResolver.cs`
Add `attackerPassives`/`defenderPassives` `GetComponent<PassiveEffectController>()` lookups alongside the
existing status/guard-buff ones (`CombatResolver.cs:26-34`), then thread them through:

- **Speed chain** (lines 36-39): append each side's `ModifySpeed` after the existing status/guard chain.
- **Accuracy roll** (lines 41-43): Light's `HasLightSpeed()` bypasses the roll entirely; on a miss, call
  `defenderPassives?.OnDodgedAttack()` (Air) and `attackerPassives?.OnOwnAttackResolved(false)` (Shock)
  before returning `Miss`. After a successful roll, Mind's `TryNegateAttack()` can still turn it into a
  `Miss` (also ticking Shock's reset).
- **Crit roll** (line 47): chain Luck first — new plumbing, mirrors the Speed/Attack/Defense chains exactly,
  in this order: `attackerStatus?.ModifyLuck` (Eclipse's debuff, if this attacker was hit by a Dark bunny's
  Eclipse earlier and is still within its 5s window) → `attackerPassives?.ModifyLuck` (Dark's own Star Favor
  self-buff, if this attacker IS a ≤50%-HP Dark bunny). Then: Fire's `ConsumeGuaranteedCrit()` can force
  `crit = true`; Metal's `IsCritImmune()` then forces `crit = false` and wins regardless (crit immunity is
  absolute). If `crit` ends up true, call `defenderPassives?.OnHitByCrit()` (Fire — arms the *defender's*
  own future guaranteed crit).
- **New: Dark's Eclipse**, applied right after `defender.TakeCombatDamage(damage)` (same spot as Earth's
  `OnHitTaken()` below): if `attackerPassives != null && attackerPassives.ShouldTriggerEclipse()` and
  `defenderStatus != null`, call
  `defenderStatus.ApplyLuckDebuff(cfg.eclipseLuckDebuffMultiplier, cfg.eclipseLuckDebuffDurationSeconds)`.
  No status-immunity gate needed here — Eclipse is not one of the 5 `StatusEffectType`s, so Draco's
  `IsStatusImmune()` deliberately does not block it (open call: flag this to Ethan if Draco should resist
  it too; nothing in the spec says so, and it's a stat debuff rather than a "status effect" by this
  codebase's own definition, so leaving it unblocked is the literal reading).
- **Attack/Defense chain** (lines 53-56): append each side's `ModifyAttack`/`ModifyDefense` after the
  existing chains.
- **Damage formula** (lines 59-66): multiply in `attackerPassives?.GetPotentPoisonMultiplier(defenderStatus) ?? 1f`
  (Toxic — reads `defenderStatus.ActiveStatus == StatusEffectType.Poison`) alongside the existing
  crit/random/stab/type multipliers. Right after `defender.TakeCombatDamage(damage)`, call
  `defenderPassives?.OnHitTaken()` (Earth).
- **New: Ice's Frosty**, inserted right after that, before the existing forward status-roll block: if
  `attacker.AttackSource.isMelee` and `defenderPassives.TryRollFrostyChillOnAttacker()` succeeds, apply
  Chill to the *attacker* (`attackerStatus.ApplyStatus(StatusEffectType.Chill, cfg.frostyChillDurationSeconds)`),
  gated on the attacker not being status-immune (`!(attackerPassives?.IsStatusImmune() ?? false)`).
- **Existing forward status roll** (lines 69-78): gate the `ApplyStatus` call on
  `!(defenderPassives?.IsStatusImmune() ?? false)` (Draco).
- **Before the final return**: call `attackerPassives?.OnOwnAttackResolved(true)` (Shock — successful
  landed hit reduces its own next cooldown; wire the actual reduction via a new
  `NPCBunny.ReduceAttackCooldown(float seconds)` that does
  `attackCooldownRemaining = Mathf.Max(0f, attackCooldownRemaining - seconds);`, mutating the existing
  private field already used by `CombatEngagement.TryBeginAttack` at `NPCBunny.cs:4070`/`4120`).

### 6. Sound's Echo — `Assets/Scripts/Combat Scripts/AttackInstance.cs`, `Resolve()` (line 286)
Right after the existing `CombatHitResult result = CombatResolver.ResolveHit(attacker, target);` (line
289), leaving `CombatResolver.ResolveHit` itself as a strict single-pair resolver (per its own "called
exactly once" contract):
```csharp
if (attacker is NPCBunny bunny && bunny.DefendingRoom != null
    && bunny.GetDiscoveredPassive(PassiveEffectType.AoEAttack) != null
    && InvasionManager.Instance != null)
{
    foreach (EnemyInstance enemy in InvasionManager.Instance.GetEnemiesInRoom(bunny.DefendingRoom))
    {
        if (enemy == null || ReferenceEquals(enemy, target) || !((ICombatant)enemy).IsAlive) continue;
        CombatHitResult extra = CombatResolver.ResolveHit(attacker, enemy);
        if (extra.Hit) ((ICombatant)enemy).PlayHitFlash(TypeHitFlashPalette.GetColor(attacker.Type));
    }
}
```
Note: `EnemyInstance.PlayHitFlash` is an **explicit** `ICombatant` interface implementation (verified,
`EnemyInstance.cs:163`) — must be called through an `ICombatant`-typed reference/cast, not directly on the
`EnemyInstance` variable, or it won't compile.

**Stated presentation compromise**: the projectile/VFX/SFX still only visually targets the primary
`target` — every other enemy in the room gets an independently-rolled hit/miss/crit/damage/status result
with just a hit-flash tint, no dedicated projectile. This is the cheapest correct implementation given no
AoE visual machinery exists anywhere in the project today; a fan-out VFX pass is future polish, not
required for this to be functionally correct.

### 7. Ghost's Second Life — `Assets/Scripts/NPC Bunny Scripts/NPCBunny.cs`, `TakeCombatDamage` (line ~1907)
Insert between the shield-absorb block and the existing `currentHP = Mathf.Max(0, currentHP - remaining);`:
```csharp
int projectedHP = currentHP - remaining;
if (projectedHP <= 0)
{
    if (ghostImmuneToFaintRemaining > 0f) { currentHP = 1; return; } // reuses ApplyForagingDamage's floor-at-1 precedent (NPCBunny.cs:1775)

    BunnyPassiveDefinition secondLife = GetDiscoveredPassive(PassiveEffectType.SurviveFatalHit);
    if (Type == BunnyType.Ghost && secondLife != null && ghostSecondLifeCooldownRemaining <= 0f)
    {
        currentHP = 1;
        ghostImmuneToFaintRemaining = CombatBalanceConfig.Instance.secondLifeImmuneWindowSeconds;
        ghostSecondLifeCooldownRemaining = secondLife.abilityCooldownSeconds; // per-asset, 60s
        return;
    }
}
currentHP = Mathf.Max(0, projectedHP);
if (currentHP != 0) return;
// ...unchanged OnDefeated/Fainted logic below...
```
New fields `ghostSecondLifeCooldownRemaining`/`ghostImmuneToFaintRemaining` (both `private float`), ticked
in `Update()`'s existing `if (HasEnteredBase) { ... }` block right next to the
`traitRerollCooldownRemaining` tick line (`NPCBunny.cs:687-688`).

### 8. Water's Cleanse / Pixie's Fairy's Blessing / Plant's in-combat heal — `NPCBunny.Update()`
New tick methods called from the existing `if (HasEnteredBase)` block, following the targeting rules from
"Decisions" above and using `DwellerRoster.Instance.GetBunniesDefendingRoom(defendingRoom)` (filtered to
`CurrentState != BunnyState.Fainted`) to enumerate allies:
- `TickPlantInCombatRegrowth()` — while `CurrentState == BunnyState.Defending`, heal
  `cfg.regrowthInCombatHealFractionPerSecond * Stats.HP * Time.deltaTime`. No targeting, no cooldown.
- `TickWaterCleanse()` — while `CurrentState == BunnyState.Defending` and
  `waterCleanseCooldownRemaining <= 0`: if self has an active status, cleanse self; else scan
  `DwellerRoster` allies for anyone statused and clear one at random; on any cleanse, start the 7s CD
  (`GetDiscoveredPassive(PassiveEffectType.CleanseAlly).abilityCooldownSeconds`).
- `TickPixieBlessing()` — same gating, but watches for the first ally (self included) at ≤50% HP fraction
  and heals them by `cfg.fairysBlessingHealFractionOfMaxHP * theirMaxHP`, then starts its own 7s CD.

New bespoke cooldown fields (per Decision #4): `waterCleanseCooldownRemaining`, `pixieBlessingCooldownRemaining`.

### 9. Neutral's Adaptability
Data-only: change `Neutral.asset`'s `passives[0].abilityCooldownSeconds` from `5` to `1800`. No code change.

### 10. `Assets/Data/Bunny Types/*.asset` — all 20 need hand-authored `passives`
Confirmed `Editor/BunnyDataGenerator.cs` never touches `passives` — every entry has always been, and must
continue to be, hand-authored per asset (same shape as the existing Plant/Neutral entries: `id`,
`displayName`, `description` = the spec text verbatim, `unlockLevel: 1`, `effectType`, `discovered: true`,
plus `abilityCooldownSeconds` for Water/Pixie/Ghost/Neutral, plus `icon` once art exists). **Author these
via the Unity Inspector, not hand-typed YAML** — `PassiveEffectType` is a serialized enum and Unity
resolves the dropdown by name; hand-typing the underlying YAML integer risks an off-by-one silently wiring
the wrong passive to a type.

### 11. Passive UI — `Assets/Scripts/UI Elements/`
- New `PassiveRowUI.cs` (mirrors `TraitRerollRowUI`'s explicit-field-wiring shape): `iconImage`,
  `nameText`, `descriptionText`, `cooldownRoot` (GameObject, active only for the 3 cooldown-based
  passives), `cooldownText`.
- `BunnyInfoUI.cs`: replace the current `PopulateList(passiveListContainer, bunny.ActivePassives, p =>
  p.displayName)` call (`BunnyInfoUI.cs:226`) with a new `PopulatePassiveRows(NPCBunny bunny)` that
  instantiates one `passiveRowPrefab` (new serialized field) per entry in `bunny.ActivePassives`, filling
  icon (hidden if null)/name/description, and toggling `cooldownRoot` for
  `CleanseAlly`/`HealAllyBelowHalfHP`/`SurviveFatalHit` entries. Add a small
  `RefreshPassiveCooldowns()` called from the existing `Update()` alongside `RefreshAdaptableBlock()` that
  just updates the cached cooldown `TextMeshProUGUI`s' strings (via new public
  `WaterCleanseCooldownRemaining`/`PixieBlessingCooldownRemaining`/`GhostSecondLifeCooldownRemaining`
  accessors on `NPCBunny`, mirroring the existing `TraitRerollCooldownRemaining`) — don't rebuild the row
  list every frame.
- The existing bespoke Adaptability block (`adaptableRoot`/`rerollTraitButton`/`traitPickerRoot`) is left
  untouched; Adaptability will *also* now show up as an ordinary row in the new generic list (name/icon/
  description, no cooldown readout there since the dedicated block already shows that). This is expected
  duplication, not a bug.
- New `passiveRowPrefab` needs building/wiring in the Editor (Inspector work, not code).

## Implementation order

1. **Infrastructure, no visible behavior change**: `CombatBalanceConfig` fields → `PassiveEffectType` enum
   + `icon` field → `StatusEffectController`'s overload → `PassiveEffectController` skeleton (add to bunny
   prefab in Editor) → `GetDiscoveredPassive`/`ReduceAttackCooldown` on `NPCBunny` → wire the (still mostly
   stub) lookups into `CombatResolver`.
2. **Simple self-contained modifiers**: Metal, Light, Draco, Mind, Melee, Dark.
3. **Self-only stacking/streak modifiers**: Shock, Air, Earth, Fire (adds `ResetCombatStacks()` into
   `BeginDefending`).
4. **Cross-bunny/room-scoped modifiers**: Insect, Stone, Toxic, Ice.
5. **Reactive tick-based**: Plant's in-combat branch, then Water, then Pixie (most complex targeting).
6. **New mechanisms**: Ghost's Second Life, Sound's Echo.
7. **Data + UI last**, once the code it displays/reads is stable: author all 20 `.asset` `passives` lists,
   update Neutral's CD, build `PassiveRowUI` + wire `BunnyInfoUI`.

## Verification

No Unity Editor/Play Mode or C# compiler is available in this environment (confirmed: no `.csproj`/`.sln`,
no `dotnet`/`mono`/`mcs`), so this can't be run end-to-end here. Verification is code-review-based:
- Every new call site's signature checked by hand against what's confirmed in this plan (all cited
  signatures above were verified directly against the source during planning, not guessed).
- Null-safety: every new `PassiveEffectController`/`StatusEffectController` lookup uses the same `?.`/
  `!= null` guarding the existing code does, so a bunny prefab missing the new component degrades
  gracefully.
- After authoring each `.asset`'s `passives` entry, spot-check `effectType` resolved to the intended enum
  case (build via Inspector dropdown, not hand-typed YAML, per §10).
- Walk the 20-row spec table against the corresponding implementation one more time after writing the
  code — the closest thing to a regression check available without Play Mode.
- Ghost's Second Life and Sound's Echo are the only genuinely new control-flow shapes (damage-floor
  interception, multi-target loop) — give those an extra manual trace; everything else is "one more
  modifier chained onto an existing pattern already proven safe by `StatusEffectController`/
  `GuardBuffController`."
- Once in the Unity Editor: actually open `CombatBalanceConfig.asset` after adding the new fields to
  confirm they appear with their declared defaults, and drag the `PassiveEffectController` component +
  new UI prefab references onto the bunny/`BunnyInfoUI` prefabs (both are Editor-only steps this plan
  can't perform from here).
