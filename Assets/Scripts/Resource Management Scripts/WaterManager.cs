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
}
