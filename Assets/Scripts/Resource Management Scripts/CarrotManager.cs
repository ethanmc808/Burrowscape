using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

// The player-facing Carrot stockpile — simplest of the three resource caps, since Carrots have no
// rationing/backup pool and no room ever operationally consumes them at a rate (unlike Power/Water):
// it's strictly GardenRoom production adding in, and a hungry bunny physically eating in a CafeteriaRoom
// pulling out, one unit at a time. Same "base + per-room contribution" cap formula as
// WaterManager.storageMax and PowerManager.rationingPoolMax, but the contributing rooms here are
// CONSUMPTION-side rooms (Cafeteria, later Kitchen/Cold Storage — the places carrots are actually stored
// and served from) rather than production-side rooms (GardenRoom never contributes, same as Fallout
// Shelter tying food storage to Diners rather than Gardens).
public class CarrotManager : MonoBehaviour
{
    public static CarrotManager Instance { get; private set; }

    [SerializeField] private int currentCarrots = 0;
    public int CurrentCarrots => currentCarrots;

    // Flat baseline storage the base always has before any capacity-contributing room is built — mirrors
    // WaterManager.baseWaterStorageAmount / PowerManager.baseRationingPoolAmount.
    [SerializeField] private int baseCarrotStorageAmount = 50;

    public event Action<int> OnCarrotCountChanged;

    // Any RoomBase with contributesToCarrotStorage = true — currently only CafeteriaRoom sets that flag,
    // but Kitchen/Cold Storage can register the same way later purely via their own Inspector values,
    // with no code changes needed here (see RoomBase.OnEnable/OnDisable).
    private readonly List<RoomBase> capacityContributors = new List<RoomBase>();

    private int storageMax;
    public int CarrotStorageMax => storageMax;

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

    // Called by RoomBase.OnEnable/OnDisable for any room with contributesToCarrotStorage = true.
    public void RegisterCapacityContributor(RoomBase room)
    {
        if (!capacityContributors.Contains(room)) capacityContributors.Add(room);
        RecomputeStorageMax();
    }

    public void UnregisterCapacityContributor(RoomBase room)
    {
        capacityContributors.Remove(room);
        RecomputeStorageMax();
    }

    // Recomputes from scratch (base + sum of every currently-registered contributor's
    // CarrotStorageCapacityAmount) rather than incrementally adding/subtracting — same reasoning as
    // WaterManager.RecomputeStorageMax / PowerManager.RecomputeRationingPoolMax: avoids drift, and
    // immediately clamps currentCarrots down if a room's removal just lowered the ceiling below what's
    // currently banked.
    private void RecomputeStorageMax()
    {
        storageMax = baseCarrotStorageAmount + capacityContributors.Where(r => r != null).Sum(r => r.CarrotStorageCapacityAmount);
        currentCarrots = Mathf.Min(currentCarrots, storageMax);
    }

    // Clamped to storageMax — production that arrives while the stockpile is already full is simply
    // wasted, same as Water/Fallout Shelter.
    public void AddCarrots(int amount)
    {
        currentCarrots = Mathf.Min(currentCarrots + amount, storageMax);
        OnCarrotCountChanged?.Invoke(currentCarrots);
    }

    // Returns true if successful, false if not enough carrots
    public bool TryConsumeCarrot()
    {
        if (currentCarrots <= 0) return false;
        currentCarrots -= 1;
        OnCarrotCountChanged?.Invoke(currentCarrots);
        return true;
    }
}