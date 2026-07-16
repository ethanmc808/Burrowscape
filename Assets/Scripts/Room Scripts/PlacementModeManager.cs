using UnityEngine;

public enum PlacementMode
{
    None,
    Build,
    Delete
}

// Keeps Build mode and Delete mode mutually exclusive without either controller needing to know the
// other's internals — each just calls RequestMode(...) and reacts to OnModeChanged if it's the one
// being switched away from.
public class PlacementModeManager : MonoBehaviour
{
    public static PlacementModeManager Instance { get; private set; }

    public PlacementMode CurrentMode { get; private set; } = PlacementMode.None;

    public event System.Action<PlacementMode> OnModeChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RequestMode(PlacementMode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        OnModeChanged?.Invoke(mode);
    }
}
