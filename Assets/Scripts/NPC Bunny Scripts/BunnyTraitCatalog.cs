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

    // Adult wild spawns roll count=2. Kid bunnies (see the Breeding System plan) don't call this directly
    // — they go through RollInheritedTrait below instead, which rolls exactly one trait but weights it
    // toward the parents' own traits. Degrades gracefully to fewer than `count` traits if the pool is
    // empty or incompatibilities exhaust it — not an error state, just means nothing (or not enough) has
    // been authored yet.
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

    // Used by Neutral's "Adaptable" passive (NPCBunny.TryRerollTrait, see BunnyTypeSystem_DesignDoc.md) —
    // rolls exactly ONE replacement trait for a bunny discarding `discarded` while keeping `keptTraits`,
    // enforcing the same incompatibility rule RollTraits already does. Excludes `discarded` itself from the
    // candidate pool so a spent cooldown can never roll back into the same trait it just gave up. Returns
    // null only if no valid replacement exists (empty catalog, or every remaining candidate conflicts with
    // something kept) — same graceful-degradation contract as RollTraits/RollInheritedTrait; the caller
    // treats null as "nothing happened," not an error.
    public BunnyTraitDefinition RollReplacementTrait(BunnyTraitDefinition discarded, List<BunnyTraitDefinition> keptTraits)
    {
        List<BunnyTraitDefinition> pool = new List<BunnyTraitDefinition>(traits);
        if (discarded != null) pool.RemoveAll(t => t.id == discarded.id);

        while (pool.Count > 0)
        {
            int i = Random.Range(0, pool.Count);
            BunnyTraitDefinition candidate = pool[i];
            pool.RemoveAt(i);

            bool conflicts = keptTraits != null && keptTraits.Exists(k =>
                k.incompatibleTraitIds.Contains(candidate.id) || candidate.incompatibleTraitIds.Contains(k.id));

            if (!conflicts) return candidate;
        }
        return null;
    }

    // A kid bunny's single trait: 20% chance drawn uniformly from mom's own traits, 20% from dad's, 60%
    // (or whenever the rolled-favorite parent has no traits of their own to draw from) fully random from
    // the whole catalog via RollTraits(1) — see decision #4 in the Breeding System plan. Called once per
    // sibling independently for a multi-kid litter, so twins/triplets can still end up with different
    // traits from each other despite sharing a Type and their inherited IV stat(s).
    //
    // Returns null only if the whole catalog is empty (mirrors RollTraits' own graceful degradation) —
    // callers should treat that the same way a wild spawn's empty-catalog RollTraits(1) result would be.
    public BunnyTraitDefinition RollInheritedTrait(IReadOnlyList<BunnyTraitDefinition> momTraits, IReadOnlyList<BunnyTraitDefinition> dadTraits)
    {
        float roll = Random.value;

        if (roll < 0.2f && momTraits != null && momTraits.Count > 0)
            return momTraits[Random.Range(0, momTraits.Count)];

        if (roll < 0.4f && dadTraits != null && dadTraits.Count > 0)
            return dadTraits[Random.Range(0, dadTraits.Count)];

        List<BunnyTraitDefinition> fallback = RollTraits(1);
        return fallback.Count > 0 ? fallback[0] : null;
    }
}
