using UnityEngine;

// One asset per accessory (Backpack, Binoculars, Lightning Shoes, ...) — mirrors BunnyTraitDefinition's
// proof-of-concept "one effect type + one magnitude" shape exactly, so new accessories are just data
// rows, no new code needed. Utility-flavored only (capacity/luck/speed), deliberately never combat — see
// the design doc's "Held items" section.
public enum ForagingAccessoryEffectType
{
    // Additive to ForagingManager's base carry capacity — stacks with any future capacity trait rather
    // than competing with it (see the design doc's "Carry-capacity trait/item overlap" open item).
    CarryCapacityBonus,
    // Additive percentage points, same shape as NPCBunny.ForagingRareLootBonus / Treasure Finder — the
    // roll sums every additive input rather than picking one.
    RareLootChanceBonus,
    // Multiplies the return-countdown duration (see ForagingManager.CalculateReturnDuration) — below 1
    // means a faster return, e.g. Lightning Shoes.
    ReturnSpeedMultiplier
}

[CreateAssetMenu(fileName = "ForagingAccessoryDefinition", menuName = "Burrowscape/Foraging Accessory Definition")]
public class ForagingAccessoryDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Effect (proof of concept — one magnitude per accessory, mirrors BunnyTraitDefinition)")]
    public ForagingAccessoryEffectType effectType;
    public float magnitude = 1f;
}
