using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Holds EVERY width/Grade RoomDefinition variant of every room type — not just the 4-wide Grade-1
// subset BuildMenuUI shows for direct player building. The merge/upgrade Swap step needs to look up
// "the 8-wide Grade-1 Garden" or "the 4-wide Grade-2 Garden" the same way BuildMenuUI looks up
// buildable entries, so this is a separate, superset catalog rather than a change to BuildMenuUI's
// own list (which stays exactly as-is — still just the buildable subset).
public class RoomCatalogRegistry : MonoBehaviour
{
    public static RoomCatalogRegistry Instance { get; private set; }

    [Tooltip("Every RoomDefinition variant that exists as an asset — every width x Grade combination for every room type, including the ones BuildMenuUI never shows directly.")]
    [SerializeField] private List<RoomDefinition> allDefinitions = new List<RoomDefinition>();

    // Hardcoded at 12 per the design doc, but kept as a serialized field (not a const) so it's
    // adjustable later — same mutable-not-const convention BaseLayoutManager already uses for its
    // buildable bounds.
    [SerializeField] private float maxMergedWidth = 12f;
    public float MaxMergedWidth => maxMergedWidth;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Exact-match lookup used by both the merge resolver (does a variant of this combined width exist?)
    // and the upgrade UI (does the next Grade up exist?). Returns null if nothing authored yet.
    public RoomDefinition FindVariant(string roomTypeId, int width, int grade)
    {
        if (string.IsNullOrEmpty(roomTypeId)) return null;

        return allDefinitions.FirstOrDefault(d =>
            d != null
            && d.roomTypeId == roomTypeId
            && d.footprint.x == width
            && d.grade == grade);
    }
}
