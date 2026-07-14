using UnityEngine;
using System;

public class CarrotManager : MonoBehaviour
{
    public static CarrotManager Instance { get; private set; }

    [SerializeField] private int currentCarrots = 0;
    public int CurrentCarrots => currentCarrots;

    public event Action<int> OnCarrotCountChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddCarrots(int amount)
    {
        currentCarrots += amount;
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