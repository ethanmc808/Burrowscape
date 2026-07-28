using System;
using UnityEngine;

// Drives the _HueShift/_SatMul/_ValMul properties on a HueShiftLit material via
// a MaterialPropertyBlock, so palette-swapped instances (e.g. per-prop color
// variety) don't need a separate material asset per variant.
[RequireComponent(typeof(Renderer))]
public class HueShiftController : MonoBehaviour
{
    [Serializable]
    public struct ColorPreset
    {
        public string name;
        [Range(0f, 1f)] public float hue;
        [Range(0f, 2f)] public float saturationMultiplier;
        [Range(0f, 2f)] public float valueMultiplier;
    }

    [Tooltip("Named hue/saturation/value combos selectable via SetVariant. Edit to match the actual color scheme.")]
    [SerializeField]
    private ColorPreset[] presets = new ColorPreset[]
    {
        new ColorPreset { name = "Default", hue = 0f, saturationMultiplier = 1f, valueMultiplier = 1f },
        new ColorPreset { name = "Variant A", hue = 0.15f, saturationMultiplier = 1f, valueMultiplier = 1f },
        new ColorPreset { name = "Variant B", hue = 0.45f, saturationMultiplier = 1f, valueMultiplier = 1f },
        new ColorPreset { name = "Variant C", hue = 0.75f, saturationMultiplier = 1f, valueMultiplier = 1f },
    };

    private static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
    private static readonly int SatMulId = Shader.PropertyToID("_SatMul");
    private static readonly int ValMulId = Shader.PropertyToID("_ValMul");

    private Renderer targetRenderer;
    private MaterialPropertyBlock propBlock;

    private void Awake()
    {
        targetRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
    }

    public void SetVariant(int presetIndex)
    {
        if (presetIndex < 0 || presetIndex >= presets.Length)
        {
            Debug.LogWarning($"HueShiftController: preset index {presetIndex} out of range on {name}.");
            return;
        }

        ColorPreset preset = presets[presetIndex];
        ApplyHue(preset.hue, preset.saturationMultiplier, preset.valueMultiplier);
    }

    public void SetVariant(string presetName)
    {
        for (int i = 0; i < presets.Length; i++)
        {
            if (presets[i].name == presetName)
            {
                SetVariant(i);
                return;
            }
        }

        Debug.LogWarning($"HueShiftController: no preset named '{presetName}' on {name}.");
    }

    public void SetHue(float hue01, float saturationMultiplier = 1f, float valueMultiplier = 1f)
    {
        ApplyHue(hue01, saturationMultiplier, valueMultiplier);
    }

    private void ApplyHue(float hue01, float satMul, float valMul)
    {
        targetRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat(HueShiftId, Mathf.Repeat(hue01, 1f));
        propBlock.SetFloat(SatMulId, satMul);
        propBlock.SetFloat(ValMulId, valMul);
        targetRenderer.SetPropertyBlock(propBlock);
    }

    public int PresetCount => presets.Length;
}
