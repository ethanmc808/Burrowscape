using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// One catalog entry per buildable room type (including LiftRoom, which shares this same system rather
// than being bolted on separately). Only 4x2x6 ("1 Room Wide") prefabs should be wired into these for
// now — the 8x2x6/12x2x6 merged-size prefabs already exist as assets but are reserved for the future
// merge system, which swaps to one of those pre-made prefabs rather than letting the player place one
// directly.
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "Burrowscape/Room Definition")]
public class RoomDefinition : ScriptableObject
{
    [Header("Display")]
    public string displayName;
    public Sprite icon;

    [Header("Prefab & Footprint")]
    public GameObject prefab;
    [Tooltip("Width x Height x Depth in world units. Only the width (X) is currently consumed by grid/placement math — height and depth are stored for future reference.")]
    public Vector3Int footprint = new Vector3Int(4, 2, 6);

    [Header("Type & Grade")]
    [Tooltip("Must match the RoomTypeId authored on this prefab's RoomBase component. Every width/Grade variant of a room type shares the same RoomTypeId, which is how the merge/upgrade Swap step finds the right asset to swap to.")]
    public string roomTypeId;
    [Tooltip("Must match the Grade authored on this prefab's RoomBase component.")]
    public int grade = 1;

    [Header("Cost")]
    public int goldCost;

    [Tooltip("LiftRoom catalog entries use a different adjacency rule than standard rooms (see RoomPlacementValidator) and a 1x2x6 footprint.")]
    public bool isLiftSegment;

    [Header("Unlock Conditions")]
    [Tooltip("Empty = always unlocked. All conditions must pass (AND).")]
    public List<RoomUnlockCondition> unlockConditions = new List<RoomUnlockCondition>();

    // Uses this asset's own name as its unique ID (for permanent-unlock flag storage) rather than a
    // separate hand-typed ID field — asset names are already unique within a folder, one less thing to
    // keep in sync.
    public string Id => name;

    public bool IsUnlocked()
    {
        return unlockConditions.All(c => c.IsMet(Id));
    }
}
