using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Per-trip mutable state, tracked externally by ForagingManager rather than on NPCBunny itself — mirrors
// how GardenRoom tracks its own activeProductionRoutines rather than NPCBunny knowing about production.
// Deliberately a plain data class (not a MonoBehaviour) since the whole placeholder resolver this feeds
// is disposable — see Foraging_DesignDoc.md's "Placeholder encounter resolver" section. Public fields
// throughout since this is read directly by ForagingScreenUI's status list (HP/Energy/carry-fullness).
public class ForagingTripState
{
    public ForagingLocationDefinition location;
    public ForagingAccessoryDefinition equippedAccessory;
    public int potionsRemaining;
    public int carryCapacity;

    public int carriedCarrots;
    public int carriedGold;
    public int foundPotionCount;
    public List<ForagingAccessoryDefinition> foundAccessories = new List<ForagingAccessoryDefinition>();

    public float elapsedTripTime;
    public float xpAccumulator;
    public bool manualRecallRequested;

    // Carrots + found Potions + found Accessories all count toward the carry limit like a physical item
    // would — Gold is deliberately excluded (see the design doc's "Loot economy" section: weightless
    // coinage vs. bulky cargo).
    public int CarriedItemCount => carriedCarrots + foundPotionCount + foundAccessories.Count;
}

// Orchestrates Foraging dispatch, the per-bunny trip simulation (tick timer -> loot/encounter/gate-check
// rolls -> return triggers -> return countdown), and loot/XP deposit on arrival home. See
// Foraging_DesignDoc.md for the full design; every numeric default below is a [SerializeField] tunable,
// not a final balance claim — see the design doc's repeated "must be tunable" requirement.
public class ForagingManager : MonoBehaviour
{
    public static ForagingManager Instance { get; private set; }

    [Header("Locations")]
    [SerializeField] private List<ForagingLocationDefinition> locations;
    [SerializeField] private ForagingDifficultyTierConfig tierConfig;

    [Header("Trip Visuals")]
    [Tooltip("The offscreen point a departing bunny walks to once past the gate, and where a returning bunny starts its walk back in. Should sit at the same position WildBunnySpawner uses for wild arrivals (BaseEntrance + its offscreenSpawnOffsetX) for visual consistency, though it's a separate Transform since WildBunnySpawner only ever computes that position inline.")]
    [SerializeField] private Transform foragingStagingPoint;

    [Header("Carry Capacity")]
    [SerializeField] private int baseCarryCapacity = 20;

    [Header("Tick Rate (Speed + type-match both shorten the interval, additively — see 'Tick rate')")]
    [SerializeField] private float baseTickInterval = 8f;
    [SerializeField] private float tickRateSpeedDivisor = 200f;
    [SerializeField] private float typeMatchTickRateBonus = 0.25f;

    [Header("Per-Tick Roll Chances")]
    [Range(0f, 1f)] [SerializeField] private float lootFindChancePerTick = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float encounterChancePerTick = 0.3f;
    [Range(0f, 1f)] [SerializeField] private float gateCheckChancePerTick = 0.2f;

    [Header("XP")]
    [SerializeField] private float xpPerTick = 2f;
    [SerializeField] private float xpPerCommonItem = 1f;
    [SerializeField] private float xpPerUncommonItem = 3f;
    [SerializeField] private float xpPerRareItem = 8f;
    [Tooltip("Flat multiplier applied to the WHOLE trip's total XP (all four sources combined) if the bunny's Type is in the location's recommendedTypes — see the design doc's 'Recommended-type bonus'.")]
    [SerializeField] private float typeMatchXPMultiplier = 1.3f;

    [Header("Loot Rarity Roll (base weights + additive Luck/Treasure-Finder/Binoculars bias)")]
    [SerializeField] private float baseCommonWeight = 70f;
    [SerializeField] private float baseUncommonWeight = 25f;
    [SerializeField] private float baseRareWeight = 5f;
    [Tooltip("Percentage points shifted from Common into Uncommon/Rare per point of Luck.")]
    [SerializeField] private float luckRareBonusPerPoint = 0.3f;

    [Header("Encounter Resolver (placeholder — see 'Placeholder encounter resolver')")]
    [Tooltip("Effective power = Attack + Defense*this + Luck*luckWeight, compared against the tier's enemyEffectivePower as a win-chance ratio.")]
    [SerializeField] private float defenseWeightInEffectivePower = 0.5f;
    [SerializeField] private float luckWeightInEffectivePower = 0.25f;
    [Tooltip("Fraction of max HP at/below which a carried potion auto-uses after a loss.")]
    [Range(0f, 1f)] [SerializeField] private float lowHPPotionThreshold = 0.3f;
    [SerializeField] private int potionHealAmount = 30;

    [Header("Return Delay")]
    [Tooltip("Base fraction of elapsed trip time — see 'Return delay' (0.75 per the design doc).")]
    [SerializeField] private float returnDurationFraction = 0.75f;
    [SerializeField] private float returnSpeedStatDivisor = 300f;

    private readonly Dictionary<NPCBunny, ForagingTripState> activeTrips = new Dictionary<NPCBunny, ForagingTripState>();

    private static readonly NatureStat[] GateCheckStats = { NatureStat.Attack, NatureStat.Defense, NatureStat.Speed, NatureStat.Luck };

    public IReadOnlyList<ForagingLocationDefinition> Locations => locations;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool IsCurrentlyForaging(NPCBunny bunny) => bunny != null && activeTrips.ContainsKey(bunny);
    public IEnumerable<NPCBunny> GetActiveTrips() => activeTrips.Keys;
    public ForagingTripState GetTripState(NPCBunny bunny) => activeTrips.TryGetValue(bunny, out ForagingTripState trip) ? trip : null;

    public int GetCarryCapacity(ForagingAccessoryDefinition accessory)
    {
        int bonus = (accessory != null && accessory.effectType == ForagingAccessoryEffectType.CarryCapacityBonus)
            ? Mathf.RoundToInt(accessory.magnitude) : 0;
        return baseCarryCapacity + bonus;
    }

    // ---------- DISPATCH ----------

    public bool TryDispatch(NPCBunny bunny, ForagingLocationDefinition location, int potionCount, ForagingAccessoryDefinition accessory)
    {
        if (bunny == null || location == null) return false;

        if (!bunny.CanDepartForForaging())
        {
            NotificationToast.Instance?.Show($"{bunny.BunnyName} can't be sent foraging right now.");
            return false;
        }

        if (activeTrips.ContainsKey(bunny)) return false;

        if (ForagingLocationUnlockTracker.Instance != null && !ForagingLocationUnlockTracker.Instance.IsUnlocked(location))
        {
            NotificationToast.Instance?.Show($"{location.displayName} isn't unlocked yet.");
            return false;
        }

        if (GateQueueManager.Instance == null || GateQueueManager.Instance.EntranceRoom == null
            || GateQueueManager.Instance.GateExitPoint == null || foragingStagingPoint == null)
        {
            Debug.LogWarning("ForagingManager: missing gate/entrance/staging references — can't dispatch.");
            return false;
        }

        int carryCapacity = GetCarryCapacity(accessory);
        potionCount = Mathf.Clamp(potionCount, 0, carryCapacity);

        if (ForagingInventoryManager.Instance == null) return false;

        if (!ForagingInventoryManager.Instance.TryWithdrawPotions(potionCount))
        {
            NotificationToast.Instance?.Show("Not enough potions in stock.");
            return false;
        }

        if (accessory != null && !ForagingInventoryManager.Instance.TryWithdrawAccessory(accessory))
        {
            ForagingInventoryManager.Instance.ReturnPotions(potionCount);
            NotificationToast.Instance?.Show($"No {accessory.displayName} available.");
            return false;
        }

        bool departed = bunny.DepartForForaging(GateQueueManager.Instance.EntranceRoom, GateQueueManager.Instance.GateExitPoint, foragingStagingPoint);
        if (!departed)
        {
            ForagingInventoryManager.Instance.ReturnPotions(potionCount);
            if (accessory != null) ForagingInventoryManager.Instance.ReturnAccessory(accessory);
            NotificationToast.Instance?.Show($"{bunny.BunnyName} has no route to the gate right now.");
            return false;
        }

        ForagingTripState trip = new ForagingTripState
        {
            location = location,
            equippedAccessory = accessory,
            potionsRemaining = potionCount,
            carryCapacity = carryCapacity,
        };
        activeTrips[bunny] = trip;

        // Same timing convention GateQueueManager already uses for arrivals: population accounting
        // happens the moment passage is granted/departure begins, not gated on the walk animation.
        PopulationManager.Instance.MoveResident(ResidentCategory.InBase, ResidentCategory.Foraging);

        StartCoroutine(RunDispatchRoutine(bunny, trip));
        return true;
    }

    public void RecallBunny(NPCBunny bunny)
    {
        if (activeTrips.TryGetValue(bunny, out ForagingTripState trip))
            trip.manualRecallRequested = true;
    }

    // ---------- TRIP LIFECYCLE ----------

    private IEnumerator RunDispatchRoutine(NPCBunny bunny, ForagingTripState trip)
    {
        // Don't start ticking until the bunny has actually finished walking out and reached the staging
        // point (DepartForForaging's own departure-through-gate leg) — see NPCBunny.
        // OnArrivedAtForagingStagingPoint, which is what flips CurrentState to Foraging.
        yield return new WaitUntil(() => bunny == null || bunny.CurrentState == BunnyState.Foraging);
        if (bunny == null) { activeTrips.Remove(bunny); yield break; }

        yield return RunActiveTripPhase(bunny, trip);
        yield return RunReturnCountdownPhase(bunny, trip);

        // CurrentState alone can't tell "waiting in the gate queue" apart from "actually walked all the
        // way home" — both are BunnyState.Idle (see NPCBunny.MoveToQueueSpot) — so also require the
        // bunny to no longer be sitting in GateQueueManager's queue before depositing this trip's results.
        yield return new WaitUntil(() => bunny == null || (bunny.CurrentState == BunnyState.Idle
            && (GateQueueManager.Instance == null || !GateQueueManager.Instance.IsInQueue(bunny))));

        if (bunny != null)
            DepositTripResults(bunny, trip);

        activeTrips.Remove(bunny);
    }

    private IEnumerator RunActiveTripPhase(NPCBunny bunny, ForagingTripState trip)
    {
        while (!trip.manualRecallRequested)
        {
            float interval = CalculateTickInterval(bunny, trip);
            float waited = 0f;
            while (waited < interval && !trip.manualRecallRequested)
            {
                waited += Time.deltaTime;
                trip.elapsedTripTime += Time.deltaTime;
                yield return null;
            }

            if (trip.manualRecallRequested) yield break;

            ResolveTick(bunny, trip);

            if (CheckReturnTriggers(bunny, trip)) yield break;
        }
    }

    private IEnumerator RunReturnCountdownPhase(NPCBunny bunny, ForagingTripState trip)
    {
        float duration = CalculateReturnDuration(bunny, trip);

        bunny.SetForagingReturnCountdownActive(true);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        bunny.SetForagingReturnCountdownActive(false);

        // Routes into the same gate queue every wild arrival uses (see NPCBunny.ReturnFromForaging /
        // GateQueueManager.Enqueue) instead of requesting gate passage directly — the gate now only opens
        // once this bunny is actually at the front of the queue, not the instant this countdown ends.
        // Population accounting for the ReturningFromForaging arrival type happens in
        // GateQueueManager.Update() at the moment clearance is actually granted, not here.
        bunny.ReturnFromForaging();
    }

    // ---------- RETURN TRIGGERS ----------

    private bool CheckReturnTriggers(NPCBunny bunny, ForagingTripState trip)
    {
        if (trip.CarriedItemCount >= trip.carryCapacity) return true;
        if (bunny.HPValue <= 1 && trip.potionsRemaining <= 0) return true;
        if (bunny.EnergyValue <= 0f) return true;
        return false;
    }

    // ---------- TICK RESOLUTION ----------

    private void ResolveTick(NPCBunny bunny, ForagingTripState trip)
    {
        ForagingTierData tier = tierConfig != null ? tierConfig.GetTier(trip.location.difficultyTier) : new ForagingTierData();

        trip.xpAccumulator += xpPerTick;

        if (Random.value < lootFindChancePerTick)
            ResolveLootFind(bunny, trip);

        if (Random.value < tier.goldFindChance)
            trip.carriedGold += Random.Range(tier.minGoldPerFind, tier.maxGoldPerFind + 1);

        if (Random.value < encounterChancePerTick)
            ResolveEncounter(bunny, trip, tier);

        if (Random.value < gateCheckChancePerTick)
            ResolveGateCheck(bunny, trip, tier);
    }

    private void ResolveLootFind(NPCBunny bunny, ForagingTripState trip)
    {
        List<ForagingLootEntry> table = trip.location.lootTable;
        if (table == null || table.Count == 0) return;

        ForagingLootRarity rarity = RollRarity(bunny, trip);
        List<ForagingLootEntry> candidates = table.Where(e => e.rarity == rarity).ToList();
        // This rarity band simply isn't on this location's table (e.g. Volcano has no Carrots at all,
        // not just a rare one) — a miss, not an error.
        if (candidates.Count == 0) return;

        ForagingLootEntry entry = candidates[Random.Range(0, candidates.Count)];
        int amount = Random.Range(entry.minAmount, entry.maxAmount + 1);

        switch (entry.kind)
        {
            case ForagingLootKind.Carrot:
                trip.carriedCarrots += amount;
                break;
            case ForagingLootKind.Potion:
                trip.foundPotionCount += amount;
                break;
            case ForagingLootKind.Accessory:
                if (entry.accessory != null) trip.foundAccessories.Add(entry.accessory);
                break;
        }

        trip.xpAccumulator += GetItemFindXP(rarity);
    }

    private float GetItemFindXP(ForagingLootRarity rarity)
    {
        switch (rarity)
        {
            case ForagingLootRarity.Uncommon: return xpPerUncommonItem;
            case ForagingLootRarity.Rare: return xpPerRareItem;
            default: return xpPerCommonItem;
        }
    }

    // Additive bias: Luck (per-point) + NPCBunny.ForagingRareLootBonus (Treasure Finder) + a Binoculars-
    // style accessory all shift weight from Common into Uncommon/Rare — two separate inputs to the same
    // roll, per the design doc, never competing with each other.
    private ForagingLootRarity RollRarity(NPCBunny bunny, ForagingTripState trip)
    {
        float accessoryBonus = (trip.equippedAccessory != null && trip.equippedAccessory.effectType == ForagingAccessoryEffectType.RareLootChanceBonus)
            ? trip.equippedAccessory.magnitude : 0f;
        float rareBonus = bunny.Stats.Luck * luckRareBonusPerPoint + bunny.ForagingRareLootBonus + accessoryBonus;

        float commonWeight = Mathf.Max(0f, baseCommonWeight - rareBonus);
        float uncommonWeight = baseUncommonWeight + rareBonus * 0.7f;
        float rareWeight = baseRareWeight + rareBonus * 0.3f;
        float total = commonWeight + uncommonWeight + rareWeight;

        float roll = Random.value * total;
        if (roll < commonWeight) return ForagingLootRarity.Common;
        if (roll < commonWeight + uncommonWeight) return ForagingLootRarity.Uncommon;
        return ForagingLootRarity.Rare;
    }

    // Explicitly disposable placeholder — see 'Placeholder encounter resolver'. Win chance is a smooth
    // power-ratio (higher effective power = more likely to win) rather than a hard threshold, so a
    // slightly-underpowered bunny isn't guaranteed to lose every single encounter at a given tier.
    private void ResolveEncounter(NPCBunny bunny, ForagingTripState trip, ForagingTierData tier)
    {
        float effectivePower = bunny.Stats.Attack + bunny.Stats.Defense * defenseWeightInEffectivePower + bunny.Stats.Luck * luckWeightInEffectivePower;
        float winChance = effectivePower / Mathf.Max(1f, effectivePower + tier.enemyEffectivePower);

        if (Random.value < winChance)
        {
            trip.xpAccumulator += tier.xpPerEnemyDefeated;
            return;
        }

        int damage = Random.Range(tier.minDamageOnLoss, tier.maxDamageOnLoss + 1);
        bunny.ApplyForagingDamage(damage);

        int lowHPThreshold = Mathf.Max(1, Mathf.RoundToInt(bunny.Stats.HP * lowHPPotionThreshold));
        if (bunny.HPValue <= lowHPThreshold && trip.potionsRemaining > 0)
        {
            trip.potionsRemaining--;
            bunny.HealHP(potionHealAmount);
        }
    }

    // Failing has no downside at all — a pure miss, no XP, nothing else (see 'Gate-checks'). Compares
    // against the bunny's actual resolved stat value, not a per-type theoretical min/max.
    private void ResolveGateCheck(NPCBunny bunny, ForagingTripState trip, ForagingTierData tier)
    {
        if (tierConfig == null) return;

        NatureStat stat = GateCheckStats[Random.Range(0, GateCheckStats.Length)];
        StatCheckRange range = tierConfig.GetStatCheckRange(tier, stat);
        int actual = GetStatValue(bunny, stat);

        float passChance = range.ceiling > range.floor
            ? Mathf.Clamp01((actual - range.floor) / (float)(range.ceiling - range.floor))
            : (actual >= range.ceiling ? 1f : 0f);

        if (Random.value < passChance)
            trip.xpAccumulator += tier.xpPerGateCheckPassed;
    }

    private static int GetStatValue(NPCBunny bunny, NatureStat stat)
    {
        switch (stat)
        {
            case NatureStat.Attack: return bunny.Stats.Attack;
            case NatureStat.Defense: return bunny.Stats.Defense;
            case NatureStat.Speed: return bunny.Stats.Speed;
            case NatureStat.Luck: return bunny.Stats.Luck;
            default: return 0;
        }
    }

    // ---------- TIMING ----------

    private float CalculateTickInterval(NPCBunny bunny, ForagingTripState trip)
    {
        bool typeMatch = trip.location.recommendedTypes != null && trip.location.recommendedTypes.Contains(bunny.Type);
        float speedBonus = bunny.Stats.Speed / tickRateSpeedDivisor;
        float typeBonus = typeMatch ? typeMatchTickRateBonus : 0f;
        return baseTickInterval / (1f + speedBonus + typeBonus);
    }

    private float CalculateReturnDuration(NPCBunny bunny, ForagingTripState trip)
    {
        float baseDuration = trip.elapsedTripTime * returnDurationFraction;
        float speedFactor = Mathf.Clamp01(1f - bunny.Stats.Speed / returnSpeedStatDivisor);
        float accessoryFactor = (trip.equippedAccessory != null && trip.equippedAccessory.effectType == ForagingAccessoryEffectType.ReturnSpeedMultiplier)
            ? trip.equippedAccessory.magnitude : 1f;
        return Mathf.Max(0f, baseDuration * speedFactor * accessoryFactor);
    }

    // ---------- DEPOSIT ----------

    private void DepositTripResults(NPCBunny bunny, ForagingTripState trip)
    {
        if (trip.carriedCarrots > 0) CarrotManager.Instance?.AddCarrots(trip.carriedCarrots);
        if (trip.carriedGold > 0) GoldManager.Instance?.AddGold(trip.carriedGold);

        int potionsToReturn = trip.potionsRemaining + trip.foundPotionCount;
        if (potionsToReturn > 0) ForagingInventoryManager.Instance?.ReturnPotions(potionsToReturn);

        if (trip.equippedAccessory != null)
            ForagingInventoryManager.Instance?.ReturnAccessory(trip.equippedAccessory);

        foreach (ForagingAccessoryDefinition found in trip.foundAccessories)
            ForagingInventoryManager.Instance?.AddAccessory(found, 1);

        bool typeMatch = trip.location.recommendedTypes != null && trip.location.recommendedTypes.Contains(bunny.Type);
        float finalXP = trip.xpAccumulator * (typeMatch ? typeMatchXPMultiplier : 1f);
        bunny.AddExperience(finalXP);

        NotificationToast.Instance?.Show($"{bunny.BunnyName} returned from {trip.location.displayName}.");
    }
}
