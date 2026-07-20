using UnityEngine;
using System;

public class WaterManager : MonoBehaviour
{
    public static WaterManager Instance { get; private set; }

    [SerializeField] private int currentWater = 0;
    public int CurrentWater => currentWater;

    public event Action<int> OnWaterCountChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddWater(int amount)
    {
        currentWater += amount;
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
}
