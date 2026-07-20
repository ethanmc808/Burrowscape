using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

// The player-facing Water stockpile — same "simple accumulating currency" shape as Carrots/Gold (bunnies
// drink from it, WaterRoom production adds to it), but UNLIKE Carrots/Gold it now has a hard storage cap,
// same formula shape as PowerManager's rationing pool: a flat baseline plus each registered Water Room's
// own contribution (WaterStorageCapacityAmount). This exists specifically to stop a couple of early Water
// Rooms from letting the player hoard an unlimited stockpile and never have to think about water again —
// mirrors how Fallout Shelter's production rooms cap how much of a resource you can bank at once (build
// more producers to raise the ceiling; production past a full stockpile is simply wasted, exactly like
// that game). This is a SEPARATE cap from WaterRationingManager's emergency backup pool — that one only
// matters once THIS stockpile is empty; this one caps how high THIS stockpile can ever get.
//
// WHY this is a stockpile and not a rate comparison like PowerManager: bunny drinking is a bursty,
// event-driven draw (a bunny walks to a WaterRoom, claims a spot, and pulls water in discrete ticks
// whenever it happens to show up — see WaterRoom.DrinkingRoutine), not a continuous per-second rate the
// way Power consumption and Water's own room-operational draw are. A stockpile is what lets an arbitrary
// number of bunnies each make an atomic "do you have 1 unit right now" withdrawal at whatever random
// moment they individually arrive — there's no equivalent need for Power, since nothing ever "visits" to
// consume it. Room-operational Water draw (consumesWater rooms) IS a steady rate like Power, which is why
// WaterRationingManager.Evaluate() mirrors PowerManager.Evaluate()'s floor-ranking shape — it's only
// bunny drinking that forces this class to be a real accumulating currency instead.
public class WaterManager : MonoBehaviour
{
    public static WaterManager Instance { get; private set; }

    [SerializeField] private int currentWater = 0;
    public int CurrentWater => currentWater;

    // Flat baseline storage the base always has before any Water Room is built — mirrors
    // PowerManager.baseRationingPoolAmount. Tweak this for how much "free" water storage a brand-new
    // game should start with.
    [SerializeField] private int baseWaterStorageAmount = 50;

    public event Action<int> OnWaterCountChanged;

    // Registered purely for this cap's math — separate from WaterRationingManager's own producer list
    // (which tracks the SAME WaterRoom instances, but for a different pool's cap and for distance-ranked
    // floor shutoff). Two managers, two independent registrations, same rooms.
    private readonly List<WaterRoom> producers = new List<WaterRoom>();

    private int storageMax;
    public int WaterStorageMax => storageMax;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        RecomputeStorageMax();
    }

    // Called by WaterRoom.OnEnable/OnDisable.
    public void RegisterProducer(WaterRoom room)
    {
        if (!producers.Contains(room)) producers.Add(room);
        RecomputeStorageMax();
    }

    public void UnregisterProducer(WaterRoom room)
    {
        producers.Remove(room);
        RecomputeStorageMax();
    }

    // Recomputes from scratch (base + sum of every currently-registered Water Room's
    // WaterStorageCapacityAmount) rather than incrementally adding/subtracting — same reasoning as
    // PowerManager.RecomputeRationingPoolMax: avoids drift, and immediately clamps currentWater down if
    // a room's removal just lowered the ceiling below what's currently banked.
    private void RecomputeStorageMax()
    {
        storageMax = baseWaterStorageAmount + producers.Where(p => p != null).Sum(p => p.WaterStorageCapacityAmount);
        currentWater = Mathf.Min(currentWater, storageMax);
    }

    // Clamped to storageMax — production that arrives while the stockpile is already full is simply
    // wasted (same as Fallout Shelter: a full resource bar with production still running just doesn't
    // grow further until something consumes it).
    public void AddWater(int amount)
    {
        currentWater = Mathf.Min(currentWater + amount, storageMax);
        OnWaterCountChanged?.Invoke(currentWater);
    }

    // Returns true if successful, false if not enough water
    public bool TryConsumeWater()
    {
        if (currentWater <= 0) return false;
        currentWater -= 1;
        OnWaterCountChanged?.Invoke(currentWater);
        return true;
    }

    // Atomic multi-unit draw, used by WaterRationingManager for a floor's whole per-tick room demand —
    // succeeds only if the full amount is available; otherwise leaves the pool untouched entirely (no
    // partial consumption on failure). Bunny drinking keeps using the single-unit overload above.
    public bool TryConsumeWater(int amount)
    {
        if (amount <= 0) return true;
        if (currentWater < amount) return false;
        currentWater -= amount;
        OnWaterCountChanged?.Invoke(currentWater);
        return true;
    }

    // ---------- Balance-tuning instrumentation (ResourceBalanceDebugPanel) ----------
    // Purely additive logging alongside AddWater/TryConsumeWater above — doesn't replace or change any
    // existing behavior. Production past the storage cap still gets recorded here even though AddWater
    // clamps it away, since "you're overproducing and wasting X/sec" is a real balance signal.
    private int producedThisSecond;
    private int consumedThisSecond;

    public void RecordProduction(int amount) => producedThisSecond += amount;
    public void RecordConsumption(int amount) => consumedThisSecond += amount;

    public int ReadAndResetProducedThisSecond()
    {
        int value = producedThisSecond;
        producedThisSecond = 0;
        return value;
    }

    public int ReadAndResetConsumedThisSecond()
    {
        int value = consumedThisSecond;
        consumedThisSecond = 0;
        return value;
    }
}