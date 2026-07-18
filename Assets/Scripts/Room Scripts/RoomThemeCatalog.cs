using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// One entry per room TYPE (not per width/Grade) — every 4/8/12-wide, every Grade-1/2/3 instance of a
// given RoomTypeId shares the same colors, matching how wall geometry itself is already shared via the
// DefaultRoom nesting (see RoomBase.ApplyRoomTheme). Upper/lower split lets the wall read as two-tone,
// matching the split already present on the DefaultRoom structural prefabs' side walls (and BackWall,
// once split — see Docs/RoomVisualSystems_Design.md).
[System.Serializable]
public class RoomTheme
{
    public string roomTypeId; // must match RoomBase.RoomTypeId, e.g. "Garden"
    public Material upperWallMaterial;
    public Material lowerWallMaterial;
    public Material ceilingMaterial;
    public Material floorMaterial;
}

// Central palette lookup so adding/changing a room type's colors never requires touching any of the
// ~76 individual room prefabs — see Docs/RoomVisualSystems_Design.md Feature C for why this replaced
// both unpacking DefaultRoom and per-instance material overrides. Lives in a Resources folder so
// RoomBase can load it by name without any per-prefab serialized reference.
[CreateAssetMenu(fileName = "RoomThemeCatalog", menuName = "Burrowscape/Room Theme Catalog")]
public class RoomThemeCatalog : ScriptableObject
{
    private const string ResourcesPath = "RoomThemeCatalog";

    // Cached after the first load — this asset is static for the whole play session, so there's no
    // reason for every single room's Awake() to hit Resources.Load again.
    private static RoomThemeCatalog cachedInstance;
    private static bool hasTriedLoad;

    [SerializeField] private List<RoomTheme> themes = new List<RoomTheme>();

    public RoomTheme GetTheme(string roomTypeId)
    {
        if (string.IsNullOrEmpty(roomTypeId)) return null;
        return themes.FirstOrDefault(t => t != null && t.roomTypeId == roomTypeId);
    }

    // Returns null (not an error) if the asset doesn't exist yet, or this room type has no entry —
    // both are expected/normal states while the palette is still being authored incrementally.
    public static RoomTheme Load(string roomTypeId)
    {
        if (!hasTriedLoad)
        {
            cachedInstance = Resources.Load<RoomThemeCatalog>(ResourcesPath);
            hasTriedLoad = true;
        }
        return cachedInstance != null ? cachedInstance.GetTheme(roomTypeId) : null;
    }
}
