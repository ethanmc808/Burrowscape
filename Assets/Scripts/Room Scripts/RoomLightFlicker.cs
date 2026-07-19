using UnityEngine;
using System.Collections;

// Opt-in companion component — NOT baked into RoomBase, which already carries substantial
// responsibility from the rest of the Power system. Drop this onto a room prefab once it actually has
// Light fixtures in its art; RoomBase.SetPowered's GetComponent<RoomLightFlicker>()?.OnPowerChanged(...)
// call is a safe no-op on any prefab that doesn't have this component yet, so it can be rolled out
// incrementally with zero effect elsewhere. Deliberately keyed to IsPowered only (never the combined
// IsOperational state) — a light needs electricity, not water.
public class RoomLightFlicker : MonoBehaviour
{
    [SerializeField] private float flickerDuration = 0.4f; // total time spent flickering before settling
    [SerializeField] private int flickerToggleCount = 5;   // how many random on/off toggles during that time

    private Light[] lights;
    private float[] originalIntensities;
    private Coroutine flickerRoutine;

    private void Awake()
    {
        lights = GetComponentsInChildren<Light>(true);
        originalIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            originalIntensities[i] = lights[i].intensity;
    }

    public void OnPowerChanged(bool powered)
    {
        if (lights.Length == 0) return;

        if (flickerRoutine != null)
            StopCoroutine(flickerRoutine);
        flickerRoutine = StartCoroutine(FlickerRoutine(powered));
    }

    private IEnumerator FlickerRoutine(bool endOn)
    {
        float stepDuration = flickerDuration / Mathf.Max(1, flickerToggleCount);

        for (int i = 0; i < flickerToggleCount; i++)
        {
            SetLightsOn(Random.value > 0.5f);
            yield return new WaitForSeconds(stepDuration);
        }

        SetLightsOn(endOn);
        flickerRoutine = null;
    }

    private void SetLightsOn(bool on)
    {
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].enabled = on;
            if (on) lights[i].intensity = originalIntensities[i];
        }
    }
}
