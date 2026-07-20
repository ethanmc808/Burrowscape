using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// The Water Rationing Pool — a shared emergency reserve for BOTH bunny drinking and operational room
// consumption (Garden/Kitchen/Coal Room's consumesWater draw), tapped only as a fallback once the normal
// WaterManager stockpile runs dry. This is deliberately NOT a rate-vs-rate arbitrator the way PowerManager
// is: Power has no separate "normal pool" (nothing else draws power directly the way bunnies drink
// water), so Power's pool has to do double duty as both the live resource and the buffer. Water already
// has a real, continuously-topped-up stockpile (WaterManager, fed by WaterRoom production, uncapped) — so
// rooms here draw from THAT first, exactly like bunnies already do, and only reach for this pool when the
// normal one can't cover the draw. This pool's own capacity (never auto-refilled by a production/
// consumption calculation, only spent down as a backup) comes from a flat baseline plus whatever built
// Water Rooms contribute via WaterRationingPoolAmount.
//
// Floor-priority shutoff is still centrally arbitrated here (same distance-ranked shape as PowerManager),
// but the criterion per floor is now a real, atomic draw against WaterManager-then-this-pool for that
// floor's whole tick demand, rather than a rate comparison — see Evaluate().
public class WaterRationingManager : MonoBehaviour
{
    public static WaterRationingManager Instance { get; private set; }

    public static event Action OnAnyWaterRationingChanged;

    public static WaterRationingManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Same scene-aware bootstrap as PowerManager.EnsureInstance() — check for a manually-placed
        // instance before creating one, so a hand-tuned baseRationingPoolAmount is never silently
        // replaced or destroyed by a creation-order race.
        WaterRationingManager existing = FindAnyObjectByType<WaterRationingManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("WaterRationingManager (Global)");
        return go.AddComponent<WaterRationingManager>();
    }

    [SerializeField] private float evaluationInterval = 1f;

    // Flat baseline the base always has, on top of whatever Water Room prefabs contribute. Defaults to 0
    // since WaterManager's own starting `currentWater` value already covers the equivalent "some water
    // before you've built anything" need — raise this if you want an extra emergency cushion specifically
    // for when the normal pool runs dry.
    [SerializeField] private float baseRationingPoolAmount = 0f;

    // NEW — when on, logs the full arbitration pass (per-floor distance/demand/units-needed and the
    // watered/not-watered verdict) to the Console every evaluation tick. Purely a debugging aid for
    // tuning/verifying the distance-ranked shutoff — leave off for normal play.
    [Header("Debug")]
    [SerializeField] private bool debugLogFloorArbitration = false;

    private readonly List<RoomBase> consumers = new List<RoomBase>();
    private readonly List<WaterRoom> producers = new List<WaterRoom>();

    // Per-floor fractional demand banked between ticks. A room with a 10s interval only owes 0.1 units
    // per 1s evaluation tick — without this, rounding that up to a whole unit every tick would drain 10x
    // too fast. Instead each tick adds the floor's true fractional demand here and only draws whole units
    // once enough has banked up, leaving the remainder for next time.
    private readonly Dictionary<int, float> floorDemandAccumulators = new Dictionary<int, float>();

    private float rationingPoolCurrent;
    private float rationingPoolMax;
    private bool rationingPoolInitialized;

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

    // Water Rooms register here purely for two reasons now: contributing to this pool's max capacity,
    // and marking a floor as "has a water source" for the distance-ranking below. Registration doesn't
    // depend on staffing — a built-but-empty Water Room still counts, since neither purpose above needs
    // to know whether anyone's actively working it (unlike Power, this pool no longer tracks a live
    // production rate at all).
    public void RegisterProducer(WaterRoom room)
    {
        if (!producers.Contains(room)) producers.Add(room);
        RecomputeRationingPoolMax();
    }

    public void UnregisterProducer(WaterRoom room)
    {
        producers.Remove(room);
        RecomputeRationingPoolMax();
    }

    private void RecomputeRationingPoolMax()
    {
        rationingPoolMax = baseRationingPoolAmount + producers.Where(p => p != null).Sum(p => p.WaterRationingPoolAmount);
        rationingPoolCurrent = Mathf.Min(rationingPoolCurrent, rationingPoolMax);
    }

    // The shared fallback draw — used by BOTH bunny drinking (WaterRoom.DrinkingRoutine, 1 unit at a
    // time) and this manager's own per-floor room consumption (Evaluate(), below), always only after the
    // normal WaterManager pool has already failed to cover the same request. Also latches the "start
    // full" initialization the first time it's touched, in case a draw happens before the first
    // Evaluate() tick.
    public bool TryDraw(float amount)
    {
        if (!rationingPoolInitialized)
        {
            rationingPoolCurrent = rationingPoolMax;
            rationingPoolInitialized = true;
        }

        if (amount <= 0f || rationingPoolCurrent < amount) return false;

        rationingPoolCurrent = Mathf.Clamp(rationingPoolCurrent - amount, 0f, rationingPoolMax);
        return true;
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
        if (!rationingPoolInitialized)
        {
            rationingPoolCurrent = rationingPoolMax;
            rationingPoolInitialized = true;
        }

        // Group registered consumers by floor (floors with zero consumesWater rooms never appear here).
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

        // Rank floors by distance to the nearest REGISTERED Water Room (built, not necessarily staffed —
        // this pool no longer tracks a live production rate, so there's no "active" distinction left to
        // rank against), tie-broken by ascending floor index for determinism.
        List<int> orderedFloors = consumersByFloor.Keys
            .OrderBy(f => DistanceToNearestProducerFloor(f))
            .ThenBy(f => f)
            .ToList();

        if (debugLogFloorArbitration)
        {
            Debug.Log($"[Water] pool={rationingPoolCurrent:F1}/{rationingPoolMax:F1} normalPool={(WaterManager.Instance != null ? WaterManager.Instance.CurrentWater.ToString("F1") : "n/a")}");
        }

        // Once a closer floor can't be fully covered (by normal pool + rationing pool combined), every
        // floor ranked after it is also cut without even attempting a draw — preserves floor-priority
        // meaning (a farther floor shouldn't "cut the line" and drain water a closer floor needed).
        bool shortageEncountered = false;

        foreach (int floor in orderedFloors)
        {
            List<RoomBase> roomsOnFloor = consumersByFloor[floor];
            float floorDemandRate = roomsOnFloor.Sum(c => SafeRate(c.WaterConsumptionAmount, c.WaterConsumptionInterval));

            floorDemandAccumulators.TryGetValue(floor, out float banked);
            banked += floorDemandRate * evaluationInterval;
            int unitsNeeded = Mathf.FloorToInt(banked);

            bool watered;
            if (unitsNeeded <= 0)
            {
                // Nothing owed yet this tick (e.g. a 10s interval only banks 0.1/tick) — room stays
                // watered without touching either pool; the fractional amount stays banked below.
                watered = true;
            }
            else if (shortageEncountered)
            {
                watered = false;
            }
            else if (WaterManager.Instance != null && WaterManager.Instance.TryConsumeWater(unitsNeeded))
            {
                // Normal pool covered this floor's whole tick demand — same as bunnies drawing first.
                watered = true;
                banked -= unitsNeeded;
                WaterManager.Instance.RecordConsumption(unitsNeeded);
            }
            else if (TryDraw(unitsNeeded))
            {
                // Normal pool couldn't cover it — fall back to the rationing pool, same as bunny drinking's fallback.
                watered = true;
                banked -= unitsNeeded;
                if (WaterManager.Instance != null) WaterManager.Instance.RecordConsumption(unitsNeeded);
            }
            else
            {
                // Draw failed — leave the banked amount untouched so it's retried (not lost) next tick.
                watered = false;
                shortageEncountered = true;
            }

            floorDemandAccumulators[floor] = banked;
            foreach (RoomBase c in roomsOnFloor) c.SetWatered(watered);

            if (debugLogFloorArbitration)
            {
                Debug.Log($"[Water]   floor {floor}: dist={DistanceToNearestProducerFloor(floor):F0} unitsNeeded={unitsNeeded} -> {(watered ? "WATERED" : "DRY")}");
            }
        }

        OnAnyWaterRationingChanged?.Invoke();
    }

    // No registered Water Room anywhere -> every floor is "infinitely" far, so they all tie and fall
    // back to the ThenBy(f) ascending-floor-index tie-break above.
    private float DistanceToNearestProducerFloor(int floorIndex)
    {
        if (producers.Count == 0) return float.MaxValue;

        float best = float.MaxValue;
        foreach (WaterRoom p in producers)
            if (p != null) best = Mathf.Min(best, Mathf.Abs(floorIndex - p.FloorIndex));
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