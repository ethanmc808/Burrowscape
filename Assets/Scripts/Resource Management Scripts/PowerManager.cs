using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// One instance per floor, created lazily (no scene wiring needed) the moment a room on that floor
// registers as a Power consumer or producer — keyed off the same floor index BaseLayoutManager already
// assigns every RoomBase (RoomBase.FloorIndex). Unlike CarrotManager/WaterManager (passive int counters
// with no logic of their own), this actively arbitrates: on a fixed evaluation tick it recomputes, from
// scratch, which consumers on this floor can be powered by the floor's currently-active production,
// ranking by distance to the nearest active producer (RoomBase.GridX, the same ad-hoc metric
// BaseLayoutManager.FindLiftServicing already uses for lift-routing). A full recompute each tick rather
// than incremental cut/restore bookkeeping is deliberately simpler while producing the exact same
// steady state: closest-to-a-producer consumers are powered first, furthest go dark first when supply
// is short, and everyone is powered when there's enough supply to go around.
public class PowerManager : MonoBehaviour
{
    private static readonly Dictionary<int, PowerManager> instancesByFloor = new Dictionary<int, PowerManager>();

    // Fired after every floor's evaluation tick — PowerCountDisplay listens on this rather than any
    // single floor's own instance, since a fresh floor's PowerManager can be created (or an old one
    // destroyed) at any time as rooms are built/removed.
    public static event Action OnAnyPowerChanged;

    public static PowerManager GetOrCreate(int floorIndex)
    {
        if (instancesByFloor.TryGetValue(floorIndex, out PowerManager existing) && existing != null)
            return existing;

        GameObject go = new GameObject($"PowerManager (Floor {floorIndex})");
        PowerManager manager = go.AddComponent<PowerManager>();
        manager.floorIndex = floorIndex;
        instancesByFloor[floorIndex] = manager;
        return manager;
    }

    // Summed across every floor currently tracking a producer — this is the single aggregate number
    // PowerCountDisplay shows; per-floor arbitration underneath is unaffected by this simplification.
    public static float TotalActiveProduction => instancesByFloor.Values.Where(p => p != null).Sum(p => p.CurrentActiveProduction);

    [SerializeField] private float evaluationInterval = 1f;

    private int floorIndex;
    private readonly List<RoomBase> consumers = new List<RoomBase>();
    private readonly List<RoomBase> producers = new List<RoomBase>();
    private readonly HashSet<RoomBase> activeProducers = new HashSet<RoomBase>();

    public float CurrentActiveProduction { get; private set; }

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
    }

    public void UnregisterProducer(RoomBase room)
    {
        producers.Remove(room);
        activeProducers.Remove(room);
    }

    // Called by a producer room (e.g. CoalRoom) at the same two points it starts/stops its own
    // production coroutine — i.e. only while it has at least one bunny actively Working, not merely
    // claimed/walking toward a spot.
    public void NotifyProducerActive(RoomBase room)
    {
        activeProducers.Add(room);
    }

    public void NotifyProducerInactive(RoomBase room)
    {
        activeProducers.Remove(room);
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
        float production = 0f;
        foreach (RoomBase p in activeProducers)
            if (p != null) production += SafeRate(p.PowerProductionAmount, p.PowerProductionInterval);

        CurrentActiveProduction = production;

        List<RoomBase> ordered = consumers
            .Where(c => c != null)
            .OrderBy(c => DistanceToNearestActiveProducer(c))
            .ToList();

        float runningTotal = 0f;
        foreach (RoomBase c in ordered)
        {
            runningTotal += SafeRate(c.PowerConsumptionAmount, c.PowerConsumptionInterval);
            c.SetPowered(runningTotal <= production);
        }

        OnAnyPowerChanged?.Invoke();
    }

    // Deferred: with multiple active producers on the same floor, a consumer is ranked by distance to
    // whichever is nearest — reasonable until a second producer type actually exists. No active producer
    // at all means the floor has no power source, so every consumer sorts last (float.MaxValue) and goes
    // dark once consumption exceeds the (zero) production.
    private float DistanceToNearestActiveProducer(RoomBase consumer)
    {
        if (activeProducers.Count == 0) return float.MaxValue;

        float best = float.MaxValue;
        foreach (RoomBase p in activeProducers)
            best = Mathf.Min(best, Mathf.Abs(consumer.GridX - p.GridX));
        return best;
    }

    private static float SafeRate(float amount, float interval)
    {
        return interval > 0f ? amount / interval : 0f;
    }

    private void OnDestroy()
    {
        if (instancesByFloor.TryGetValue(floorIndex, out PowerManager current) && current == this)
            instancesByFloor.Remove(floorIndex);
    }
}
