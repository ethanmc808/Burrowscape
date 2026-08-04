using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Single GLOBAL singleton (not per-floor) — Power is one pool across the whole base: every
// consumesPower room draws continuously regardless of staffing, producesPower rooms (e.g. CoalRoom)
// only contribute while genuinely staffed (SetProducerWeight). A "Power Rationing Pool" is a
// plain battery — it drains at the real deficit rate and refills at the real surplus rate, never masking
// how much is actually being produced. Modeled on Fallout Shelter's power bar: a "required" threshold
// scales with current total demand (reserveBufferSeconds), and as long as the banked pool clears that
// bar, momentary dips are absorbed for free and nobody is cut; once it can't, floor shutoff is judged
// directly against real, current production — continuous and immediate, farthest floor first, never an
// all-at-once cliff. See Evaluate(). Shutoff granularity is whole FLOORS, ranked by distance (in
// floor-index units) to whichever registered ACTIVE producer's floor is nearest.
//
// Works whether manually placed in the scene (like CarrotManager/GoldManager, so baseRationingPoolAmount
// is Inspector-tweakable) or left alone to self-create via EnsureInstance() with the code default.
public class PowerManager : MonoBehaviour
{
    public static PowerManager Instance { get; private set; }

    // Fired after every evaluation tick. UI (PowerCountDisplay) listens on this rather than caching an
    // instance reference, since Instance can be destroyed/recreated across scene loads.
    public static event Action OnAnyPowerChanged;

    public static PowerManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Check the scene for a manually-placed instance before creating one — covers the case where a
        // room's OnEnable calls this before the placed instance's own Awake() has run yet, so a
        // hand-tuned baseRationingPoolAmount is never silently replaced or destroyed by a creation-order
        // race.
        PowerManager existing = FindAnyObjectByType<PowerManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("PowerManager (Global)");
        return go.AddComponent<PowerManager>();
    }

    // Preserves the exact public surface PowerCountDisplay.cs already depends on.
    public static float TotalActiveProduction => Instance != null ? Instance.CurrentActiveProduction : 0f;

    // What PowerCountDisplay actually shows: raw production (all active Coal Rooms combined, not netted
    // against consumption) plus whatever's currently banked in the Rationing Pool — the two together are
    // "how much power the player has available," as opposed to CurrentActiveProduction above, which is
    // the effective-production value the arbitration math itself uses.
    public static float TotalPowerAvailable => Instance != null ? Instance.ActiveProductionRate + Instance.RationingPoolCurrent : 0f;

    [SerializeField] private float evaluationInterval = 1f;

    // Flat baseline the base always has, regardless of how many Coal Rooms exist — tweakable for
    // gameplay balance, and what lets a brand-new game start with some real power buffer before the
    // player has built anything.
    [SerializeField] private float baseRationingPoolAmount = 50f;

    // How many seconds' worth of CURRENT total demand must be banked in the pool before it's considered
    // a "safe" reserve — mirrors Fallout Shelter's power bar, where a requirement tick mark scales with
    // total consumption; stay above it and momentary dips are absorbed for free, drop below it and
    // shutoff begins (farthest floor first, judged against real production — see Evaluate()). Raise this
    // for a bigger grace window before any floor can flicker; lower it (or set to 0) for near-instant,
    // no-grace cutoff.
    [SerializeField] private float reserveBufferSeconds = 15f;

    // NEW — when on, logs the full arbitration pass (production/demand/pool totals, plus every floor's
    // distance/demand/running-total/on-off verdict) to the Console every evaluation tick. Purely a
    // debugging aid for tuning/verifying the distance-ranked shutoff — leave off for normal play, it's
    // noisy at the default 1s evaluationInterval.
    [Header("Debug")]
    [SerializeField] private bool debugLogFloorArbitration = false;

    private readonly List<RoomBase> consumers = new List<RoomBase>();
    private readonly List<RoomBase> producers = new List<RoomBase>();

    // Each producer room's current WEIGHTED output — not a plain headcount. The room itself (CoalRoom)
    // computes this from its own active workers' slot-rank diminishing-returns multiplier, type-match
    // bonus, and per-bunny ProductionMultiplier, and pushes the total here via SetProducerWeight whenever
    // a worker starts/stops. PowerManager just multiplies it by PowerProductionAmount/Interval and
    // GradeMultiplier below — it has no idea how the weight was computed. A room only appears here at all
    // while its weight is > 0 (removed entirely once it hits 0), so "is this room active" is just "does
    // it have an entry."
    private readonly Dictionary<RoomBase, float> activeProducerWeights = new Dictionary<RoomBase, float>();

    private float rationingPoolCurrent;
    private float rationingPoolMax;
    private bool rationingPoolInitialized;

    // Raw active-producer output — same value as ActiveProductionRate below. Kept as a separate property
    // (rather than removing it and repointing callers at ActiveProductionRate) purely to preserve the
    // existing public surface (TotalActiveProduction) that other scripts may already depend on.
    public float CurrentActiveProduction { get; private set; }

    // Raw active-producer output, NOT netted against consumption or topped up by the rationing pool —
    // "all the power rooms combined," as distinct from the effective value above.
    public float ActiveProductionRate { get; private set; }

    // Total demand from every registered consumer, units/sec — same totalDemand value Evaluate() already
    // computes for arbitration, just also stored here so balance-tuning tools (ResourceBalanceDebugPanel)
    // can read it without duplicating the summation.
    public float TotalDemandRate { get; private set; }

    public float RationingPoolCurrent => rationingPoolCurrent;
    public float RationingPoolMax => rationingPoolMax;

    // Mirrors Evaluate()'s own local reserveHealthy — exposed so NotificationManager's LowPower alert
    // (below) can edge-detect the healthy->unhealthy transition without duplicating the arbitration math.
    public bool ReserveHealthy { get; private set; } = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        RecomputeRationingPoolMax();
    }

    private void Start()
    {
        StartCoroutine(EvaluationRoutine());
    }

    public void RegisterConsumer(RoomBase room)
    {
        if (!consumers.Contains(room)) consumers.Add(room);
    }

    public void UnregisterConsumer(RoomBase room)
    {
        consumers.Remove(room);
    }

    public void RegisterProducer(RoomBase room)
    {
        if (!producers.Contains(room)) producers.Add(room);
        RecomputeRationingPoolMax();
    }

    public void UnregisterProducer(RoomBase room)
    {
        producers.Remove(room);
        activeProducerWeights.Remove(room);
        RecomputeRationingPoolMax();
    }

    // Recomputes rationingPoolMax from scratch (base + sum of every currently-registered producer's
    // PowerRationingPoolAmount) rather than incrementally adding/subtracting — avoids float-drift bugs
    // and naturally satisfies both directions: capacity grows the instant a new producer registers, and
    // shrinks (clamping current down immediately) the instant one unregisters. Does NOT top current back
    // up when max grows — building a room raises the ceiling, it doesn't grant free power.
    private void RecomputeRationingPoolMax()
    {
        rationingPoolMax = baseRationingPoolAmount + producers.Where(p => p != null).Sum(p => p.PowerRationingPoolAmount);
        rationingPoolCurrent = Mathf.Min(rationingPoolCurrent, rationingPoolMax);
    }

    // Called by a producer room (e.g. CoalRoom) every time its active-worker roster changes — the room
    // recomputes its own full weighted total (see CoalRoom.ReportWeightToPowerManager) and pushes it here
    // wholesale, rather than PowerManager tracking increments/decrements itself. A weight of 0 (or below)
    // removes the room's entry entirely, same as it having no active workers at all.
    public void SetProducerWeight(RoomBase room, float weight)
    {
        if (weight <= 0f) activeProducerWeights.Remove(room);
        else activeProducerWeights[room] = weight;
    }

    private IEnumerator EvaluationRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(evaluationInterval);
            Evaluate();
        }
    }

    private void Evaluate()
    {
        // Start the pool FULL based on whatever's registered by the time this first runs — checked once
        // here rather than at Awake/creation time, since registration order across multiple rooms
        // enabling in the same scene-load frame isn't guaranteed, but every room will have finished
        // OnEnable long before the first evaluationInterval (default 1s) elapses.
        if (!rationingPoolInitialized)
        {
            rationingPoolCurrent = rationingPoolMax;
            rationingPoolInitialized = true;
        }

        float activeProduction = 0f;
        foreach (KeyValuePair<RoomBase, float> kvp in activeProducerWeights)
            if (kvp.Key != null) activeProduction += SafeRate(kvp.Key.PowerProductionAmount, kvp.Key.PowerProductionInterval) * kvp.Value * kvp.Key.GradeMultiplier;
        ActiveProductionRate = activeProduction;

        float totalDemand = 0f;
        foreach (RoomBase c in consumers)
            if (c != null) totalDemand += SafeRate(c.PowerConsumptionAmount, c.PowerConsumptionInterval);
        TotalDemandRate = totalDemand;

        float deficit = totalDemand - activeProduction;

        // Plain battery — no masking. The pool drains at the real deficit rate and refills at the real
        // surplus rate; it never pretends production covers more than it actually does (that was the old
        // bug: it used to fully absorb the deficit for as long as it had charge, which hid every floor
        // until the pool emptied and then cut all of them in the same tick).
        if (deficit > 0f)
        {
            float drained = Mathf.Min(deficit * evaluationInterval, rationingPoolCurrent);
            rationingPoolCurrent = Mathf.Clamp(rationingPoolCurrent - drained, 0f, rationingPoolMax);
        }
        else
        {
            float surplusRate = -deficit;
            rationingPoolCurrent = Mathf.Clamp(rationingPoolCurrent + surplusRate * evaluationInterval, 0f, rationingPoolMax);
        }

        CurrentActiveProduction = activeProduction;

        // Fallout Shelter's power bar has a "required" tick mark that scales with total consumption —
        // stay above it and the vault runs fine through momentary dips; drop below it and rooms start
        // failing, farthest first. reserveThreshold is that same idea: "how much banked charge counts as
        // safe" scales with current total demand. While the pool is healthy, nobody gets cut (a Coal Room
        // worker briefly leaving to eat doesn't flicker anything); once the pool can't clear that bar,
        // arbitration switches to real, current production — continuous and immediate, no masking, no
        // all-at-once cliff.
        float reserveThreshold = totalDemand * reserveBufferSeconds;
        bool reserveHealthy = rationingPoolCurrent >= reserveThreshold;
        float arbitrationCeiling = reserveHealthy ? totalDemand : activeProduction;

        // Edge-detected (not fired every tick while unhealthy) — only the falling transition is a
        // "just started running low" moment worth alerting the player about.
        if (ReserveHealthy && !reserveHealthy)
            NotificationManager.Instance?.Show(NotificationType.LowPower);
        ReserveHealthy = reserveHealthy;

        if (debugLogFloorArbitration)
        {
            Debug.Log($"[Power] production={activeProduction:F2} demand={totalDemand:F2} pool={rationingPoolCurrent:F1}/{rationingPoolMax:F1} reserveThreshold={reserveThreshold:F1} healthy={reserveHealthy}");
        }

        // ---- Whole-floor shutoff arbitration ----
        // Group registered consumers by floor (floors with zero consumesPower rooms never appear here
        // and are never touched). Rank floors by distance (floor-index units) to the nearest ACTIVE
        // producer's floor; ties broken deterministically by ascending floor index (Dictionary
        // enumeration order is not a stable/guaranteed ordering once entries have been added/removed
        // over a session).
        Dictionary<int, List<RoomBase>> consumersByFloor = new Dictionary<int, List<RoomBase>>();
        foreach (RoomBase c in consumers)
        {
            if (c == null) continue;
            if (!consumersByFloor.TryGetValue(c.FloorIndex, out List<RoomBase> list))
            {
                list = new List<RoomBase>();
                consumersByFloor[c.FloorIndex] = list;
            }
            list.Add(c);
        }

        List<int> orderedFloors = consumersByFloor.Keys
            .OrderBy(f => DistanceToNearestActiveProducerFloor(f))
            .ThenBy(f => f)
            .ToList();

        // Monotonically non-decreasing running total (every floor's demand is >= 0), so once a floor
        // fails to fit, every floor ranked after it necessarily also fails — no early-break needed.
        float runningTotal = 0f;
        foreach (int floor in orderedFloors)
        {
            List<RoomBase> roomsOnFloor = consumersByFloor[floor];
            float floorDemand = roomsOnFloor.Sum(c => SafeRate(c.PowerConsumptionAmount, c.PowerConsumptionInterval));
            runningTotal += floorDemand;
            bool powered = runningTotal <= arbitrationCeiling;
            foreach (RoomBase c in roomsOnFloor) c.SetPowered(powered);

            if (debugLogFloorArbitration)
            {
                Debug.Log($"[Power]   floor {floor}: dist={DistanceToNearestActiveProducerFloor(floor):F0} floorDemand={floorDemand:F2} runningTotal={runningTotal:F2} -> {(powered ? "ON" : "OFF")}");
            }
        }

        OnAnyPowerChanged?.Invoke();
    }

    // No active producer anywhere -> every floor is "infinitely" far (float.MaxValue), so they all tie
    // and fall back to the ThenBy(f) ascending-floor-index tie-break above.
    private float DistanceToNearestActiveProducerFloor(int floorIndex)
    {
        if (activeProducerWeights.Count == 0) return float.MaxValue;

        float best = float.MaxValue;
        foreach (RoomBase p in activeProducerWeights.Keys)
            best = Mathf.Min(best, Mathf.Abs(floorIndex - p.FloorIndex));
        return best;
    }

    private static float SafeRate(float amount, float interval)
    {
        return interval > 0f ? amount / interval : 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
