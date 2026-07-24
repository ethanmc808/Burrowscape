using System.Collections.Generic;
using UnityEngine;

// The MonoBehaviour singleton holding the trait pool (BunnyTraitDefinition.cs) — split into its own
// file, matching this file's name, so it gets its own MonoScript asset and shows up in Add Component
// (see BunnyTraitDefinition.cs's comment for why that matters).
public class BunnyTraitCatalog : MonoBehaviour
{
    public static BunnyTraitCatalog Instance { get; private set; }

    [Tooltip("Empty by default — no trait content exists yet. See BunnyTypeSystem_DesignDoc.md.")]
    [SerializeField] private List<BunnyTraitDefinition> traits = new List<BunnyTraitDefinition>();

    public IReadOnlyList<BunnyTraitDefinition> AllTraits => traits;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Kid bunnies (future egg/breeding system) roll count=1; adult wild spawns roll count=2. Degrades
    // gracefully to fewer than `count` traits if the pool is empty or incompatibilities exhaust it —
    // not an error state, just means nothing (or not enough) has been authored yet.
    public List<BunnyTraitDefinition> RollTraits(int count)
    {
        List<BunnyTraitDefinition> pool = new List<BunnyTraitDefinition>(traits);
        List<BunnyTraitDefinition> chosen = new List<BunnyTraitDefinition>();

        while (chosen.Count < count && pool.Count > 0)
        {
            int i = Random.Range(0, pool.Count);
            BunnyTraitDefinition candidate = pool[i];
            pool.RemoveAt(i);

            bool conflicts = chosen.Exists(c =>
                c.incompatibleTraitIds.Contains(candidate.id) || candidate.incompatibleTraitIds.Contains(c.id));

            if (!conflicts) chosen.Add(candidate);
        }
        return chosen;
    }
}
