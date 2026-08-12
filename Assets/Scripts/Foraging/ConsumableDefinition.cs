using UnityEngine;

// Only HealHP exists today (heals a flat amount, same role ForagingManager.PotionHealAmount used to play
// before potions became a real item) — scaffolding for future consumable effects (cure status, boost
// mood, etc.) once the Laboratory grows beyond healing potions.
public enum ConsumableEffectType { HealHP }

// Replaces ForagingInventoryManager's old bare `potionStock` int — potions (and any future Laboratory
// output) are now a real item asset with identity/icon, same shape as ForagingMaterialDefinition/
// ForagingTrinketDefinition, instead of an unnamed counter. One asset per consumable tier (Potion, Great
// Potion, Super Potion, ...) — see BunnyTypeNiches/LaboratoryRoom design notes. No rarity field: a
// consumable's "strength" is expressed by which RecipeDefinition/herb tier produces it, not by a rarity
// tag on the output itself.
[CreateAssetMenu(fileName = "ConsumableDefinition", menuName = "Burrowscape/Consumable Definition")]
public class ConsumableDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Effect")]
    public ConsumableEffectType effectType = ConsumableEffectType.HealHP;
    [Tooltip("HealHP: flat HP restored. Meaning depends on effectType once more effect types exist.")]
    public float effectAmount;
}
