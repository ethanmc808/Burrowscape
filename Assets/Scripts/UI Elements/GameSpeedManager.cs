using System;
using UnityEngine;

// Same ledger pattern as PopulationManager/CarrotManager: a plain singleton that only changes when
// something explicitly tells it to. Cycles Time.timeScale through a fixed set of steps for the
// in-game fast-forward button, and exposes a reference-counted combat lock — combat isn't implemented
// yet, but when it is, an enemy-spawn system calls RegisterCombatSource()/UnregisterCombatSource()
// around each enemy's lifetime and gets this behavior for free: speed snaps to 1x the instant any
// enemy is present, and the button stays disabled until the LAST one is gone.
public class GameSpeedManager : MonoBehaviour
{
    public static GameSpeedManager Instance { get; private set; }

    [Tooltip("Cycled in order when the fast-forward button is clicked. First entry is treated as normal speed.")]
    [SerializeField] private float[] speedSteps = { 1f, 2f, 3f };

    private int currentIndex = 0;
    private int combatLockCount = 0;

    public event Action OnSpeedChanged;

    public float CurrentSpeed => speedSteps[currentIndex];
    public bool IsCombatLocked => combatLockCount > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        currentIndex = 0;
        Time.timeScale = speedSteps[currentIndex];
    }

    // Called by the fast-forward button. No-op while combat-locked.
    public void CycleSpeed()
    {
        if (IsCombatLocked) return;

        currentIndex = (currentIndex + 1) % speedSteps.Length;
        ApplySpeed();
    }

    // Future combat/enemy-spawn system: call once per enemy, when it spawns in/at the base.
    public void RegisterCombatSource()
    {
        combatLockCount++;

        if (combatLockCount == 1 && currentIndex != 0)
        {
            currentIndex = 0;
            ApplySpeed();
        }
    }

    // Future combat/enemy-spawn system: call once per enemy, when it dies/leaves. Speed stays at 1x
    // even after the last enemy is gone — the player has to press the button again to resume
    // fast-forward, same as if they'd pressed it themselves.
    public void UnregisterCombatSource()
    {
        combatLockCount = Mathf.Max(0, combatLockCount - 1);
    }

    private void ApplySpeed()
    {
        Time.timeScale = speedSteps[currentIndex];
        OnSpeedChanged?.Invoke();
    }
}
