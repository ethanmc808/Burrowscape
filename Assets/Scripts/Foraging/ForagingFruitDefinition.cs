using UnityEngine;

// One asset per fruit (Peach/Orange/Apple/Banana/Strawberry — see Foraging Trip Detail Panel + Item
// Expansion design doc) — mirrors ForagingAccessoryDefinition's one-effect-one-magnitude shape. Fed to a
// bunny via ForagingInventoryManager.TryFeedFruit, which grants evAmount toward boostedStat via
// NPCBunny.AddEV.
[CreateAssetMenu(fileName = "ForagingFruitDefinition", menuName = "Burrowscape/Foraging Fruit Definition")]
public class ForagingFruitDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Effect")]
    public BunnyStatType boostedStat;
    [Tooltip("EV granted per fruit fed — tunable per asset, not a shared constant.")]
    public int evAmount = 4;
}
