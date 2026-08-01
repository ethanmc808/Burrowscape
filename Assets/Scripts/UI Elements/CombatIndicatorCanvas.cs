using UnityEngine;

// Marker singleton — drop this on the screen-space Canvas meant to host every CombatHPIndicator
// instance. Indicators are spawned dynamically (one per currently-Defending/Fainted bunny), not hand-
// placed per bunny prefab, so CombatHPIndicator resolves its parent through here instead of every bunny
// prefab needing its own Canvas reference wired in the Inspector.
public class CombatIndicatorCanvas : MonoBehaviour
{
    public static CombatIndicatorCanvas Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
}
