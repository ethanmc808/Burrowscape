using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// Single GLOBAL singleton (not per-floor) — Power is one pool across the whole base: every
// consumesPower room draws continuously regardless of staffing, producesPower rooms (e.g. CoalRoom)
// only contribute while genuinely staffed (NotifyProducerActive/Inactive). A "Power Rationing Pool"
// absorbs short-term deficits before any floor loses power, and banks surplus for later — see Evaluate()
// below. Shutoff granularity is whole FLOORS, ranked by distance (in floor-index units) to whichever
// registered ACTIVE producer's floor is nearest.
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
        PowerManager existing = FindFirstObjectByType<PowerManager>();
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

    private readonly List<RoomBase> consumers = new List<RoomBase>();
    private readonly List<RoomBase> producers = new List<RoomBase>();

    // How many bunnies are currently actively Working each producer room — NOT just whether it has at
    // least one, so 2 workers in a Coal Room genuinely produce twice what 1 does. A room only appears
    // here at all while its count is >= 1 (removed entirely once it hits 0), so "is this room active"
    // is just "does it have an entry."
    private readonly Dictionary<RoomBase, int> activeProducerWorkerCounts = new Dictionary<RoomBase, int>();

    private float rationingPoolCurrent;
    private float rationingPoolMax;
    private bool rationingPoolInitialized;

    // Effective production used for this tick's floor arbitration (raw active production + whatever the
    // rationing pool contributed this tick).
    public float CurrentActiveProduction { get; private set; }

    // Raw active-producer output, NOT netted against consumption or topped up by the rationing pool —
    // "all the power rooms combined," as distinct from the effective value above.
    public float ActiveProductionRate { get; private set; }

    public float RationingPoolCurrent => rationingPoolCurrent;
    public float RationingPoolMax => rationingPoolMax;

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
        activeProducerWorkerCounts.Remove(room);
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

    // Called by a producer room (e.g. CoalRoom) once per bunny that starts/stops actively Working there
    // — an exact 1:1 increment/decrement, not a simple "is anyone here" flag, so N workers contribute N
    // times the room's configured rate.
    public void NotifyProducerActive(RoomBase room)
    {
        activeProducerWorkerCounts.TryGetValue(room, out int count);
        activeProducerWorkerCounts[room] = count + 1;
    }

    public void NotifyProducerInactive(RoomBase room)
    {
        if (!activeProducerWorkerCounts.TryGetValue(room, out int count)) return;

        if (count <= 1) activeProducerWorkerCounts.Remove(room);
        else activeProducerWorkerCounts[room] = count - 1;
    }

    // Used when a room mass-idles every active worker at once (OnRoomShutdown) — resets this room's
    // count to zero directly in one call, rather than requiring the caller to call NotifyProducerInactive
    // once per bunny that was active.
    public void NotifyProducerAllInactive(RoomBase room)
    {
        activeProducerWorkerCounts.Remove(room);
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
        foreach (KeyValuePair<RoomBase, int> kvp in activeProducerWorkerCounts)
            if (kvp.Key != null) activeProduction += SafeRate(kvp.Key.PowerProductionAmount, kvp.Key.PowerProductionInterval) * kvp.Value * kvp.Key.GradeMultiplier;
        ActiveProductionRate = activeProduction;

        float totalDemand = 0f;
        foreach (RoomBase c in consumers)
            if (c != null) totalDemand += SafeRate(c.PowerConsumptionAmount, c.PowerConsumptionInterval);

        float deficit = totalDemand - activeProduction;
        float effectiveProduction;

        if (deficit > 0f)
        {
            // Try to fully absorb the deficit from the pool. If the pool has enough, nobody loses power
            // this tick. If it has less (including already empty), drain whatever's left and convert
            // that partial amount back to a rate as bonus effective production.
            float amountNeededThisTick = deficit * evaluationInterval;
            float granted = Mathf.Min(amountNeededThisTick, rationingPoolCurrent);
            rationingPoolCurrent = Mathf.Clamp(rationingPoolCurrent - granted, 0f, rationingPoolMax);
            effectiveProduction = activeProduction + (granted / evaluationInterval);
        }
        else
        {
            // Production already covers demand — the surplus refills the pool (capped at max). Nobody
            // ever loses power in this branch.
            float surplusRate = -deficit;
            rationingPoolCurrent = Mathf.Clamp(rationingPoolCurrent + surplusRate * evaluationInterval, 0f, rationingPoolMax);
            effectiveProduction = activeProduction;
        }

        CurrentActiveProduction = effectiveProduction;

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
            bool powered = runningTotal <= effectiveProduction;
            foreach (RoomBase c in roomsOnFloor) c.SetPowered(powered);
        }

        OnAnyPowerChanged?.Invoke();
    }

    // No active producer anywhere -> every floor is "infinitely" far (float.MaxValue), so they all tie
    // and fall back to the ThenBy(f) ascending-floor-index tie-break above.
    private float DistanceToNearestActiveProducerFloor(int floorIndex)
    {
        if (activeProducerWorkerCounts.Count == 0) return float.MaxValue;

        float best = float.MaxValue;
        foreach (RoomBase p in activeProducerWorkerCounts.Keys)
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
