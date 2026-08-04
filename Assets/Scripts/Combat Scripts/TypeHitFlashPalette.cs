using System.Collections.Generic;
using UnityEngine;

// Per-type hit-flash color, extracted directly from "Burrowscape Type Chart.xlsx"'s own authored cell
// fill colors (not typed-in hex text — the header cell backgrounds themselves) — same source-of-truth
// convention as TypeChart.cs. Consulted by AttackInstance.Resolve() via the attacker's Type, since the
// flash represents "what type of attack just landed on you," not the target's own type.
public static class TypeHitFlashPalette
{
    private static readonly Dictionary<BunnyType, Color32> Colors = new Dictionary<BunnyType, Color32>
    {
        // Overridden from the Type Chart's own near-white header color (0xEFE9DD) — that value blended
        // from a renderer's default white tint into an almost-imperceptible ~6% shift, confirmed via
        // Editor.log (2026-08-03: base=RGBA(1,1,1,1), blended=RGBA(0.938,0.915,0.868,1)). Ethan's ask.
        [BunnyType.Neutral] = new Color32(0xFA, 0xD7, 0x92, 0xFF),
        [BunnyType.Fire] = new Color32(0xFF, 0xA0, 0x00, 0xFF),
        [BunnyType.Water] = new Color32(0x27, 0x46, 0xFF, 0xFF),
        [BunnyType.Plant] = new Color32(0x06, 0xD0, 0x00, 0xFF),
        [BunnyType.Shock] = new Color32(0xFD, 0xFF, 0x00, 0xFF),
        [BunnyType.Ice] = new Color32(0x3E, 0xCA, 0xFF, 0xFF),
        [BunnyType.Mind] = new Color32(0xFF, 0x42, 0xB1, 0xFF),
        [BunnyType.Toxic] = new Color32(0xA6, 0x00, 0xD4, 0xFF),
        [BunnyType.Sound] = new Color32(0x00, 0x02, 0x90, 0xFF),
        [BunnyType.Insect] = new Color32(0x00, 0xFF, 0x99, 0xFF),
        [BunnyType.Melee] = new Color32(0xFF, 0x00, 0x00, 0xFF),
        [BunnyType.Stone] = new Color32(0xB3, 0xC8, 0xD5, 0xFF),
        [BunnyType.Earth] = new Color32(0xC1, 0x51, 0x00, 0xFF),
        [BunnyType.Air] = new Color32(0x98, 0xF9, 0xFF, 0xFF),
        [BunnyType.Pixie] = new Color32(0xFF, 0xA1, 0xE0, 0xFF),
        [BunnyType.Light] = new Color32(0xFE, 0xFF, 0x6D, 0xFF),
        [BunnyType.Metal] = new Color32(0x46, 0x47, 0x4A, 0xFF),
        [BunnyType.Ghost] = new Color32(0x52, 0x00, 0xB7, 0xFF),
        [BunnyType.Dark] = new Color32(0x12, 0x12, 0x12, 0xFF),
        [BunnyType.Draco] = new Color32(0x82, 0x00, 0x00, 0xFF),
    };

    public static Color GetColor(BunnyType type)
    {
        return Colors.TryGetValue(type, out Color32 color) ? color : Color.white;
    }
}
