using UnityEngine;

// Central home for every breeding-related tunable (see the Breeding System plan doc) — a single global
// asset rather than per-Bedroom/per-prefab fields, since these numbers are meant to read consistently
// across every Bedroom instance, not vary room to room. Same "single global ScriptableObject, loaded via
// Resources" shape as WorkWanderConfig — see that file's own header comment for the full rationale.
//
// Must live at Assets/Resources/BreedingConfig.asset — created via Burrowscape > Generate Breeding Config
// (Editor/BreedingConfigGenerator.cs), not hand-authored, same reasoning as every other generator-seeded
// asset in this project.
[CreateAssetMenu(fileName = "BreedingConfig", menuName = "Burrowscape/Breeding Config")]
public class BreedingConfig : ScriptableObject
{
    private const string ResourcesPath = "BreedingConfig";
    private static BreedingConfig cachedInstance;
    private static bool hasTriedLoad;

    // Logs once and falls back to a transient default-valued instance (rather than throwing) if the
    // asset hasn't been generated yet — same "degrade gracefully, content not yet authored isn't an
    // error state" philosophy as WorkWanderConfig.Instance.
    public static BreedingConfig Instance
    {
        get
        {
            if (!hasTriedLoad)
            {
                cachedInstance = Resources.Load<BreedingConfig>(ResourcesPath);
                hasTriedLoad = true;
                if (cachedInstance == null)
                {
                    Debug.LogWarning("BreedingConfig: no asset found at Resources/BreedingConfig — using untuned defaults. Run Burrowscape > Generate Breeding Config.");
                    cachedInstance = CreateInstance<BreedingConfig>();
                }
            }
            return cachedInstance;
        }
    }

    [Header("Mating Roll Timing")]
    [Tooltip("How often an assigned male+female pair rolls for a chance to mate.")]
    public float matingIntervalMinSeconds = 30f;
    public float matingIntervalMaxSeconds = 60f;

    [Header("Mating Success Odds")]
    [Tooltip("Chance a mating roll succeeds when both bunnies share the same Type.")]
    [Range(0f, 1f)] public float sameTypeMatingChance = 0.5f;
    [Tooltip("Chance a mating roll succeeds when the pair's Types differ.")]
    [Range(0f, 1f)] public float differentTypeMatingChance = 0.25f;

    [Header("Mating Sequence Timing")]
    [Tooltip("How long the pair stays visible at MatingSpot playing the happy animation before walking behind the wall.")]
    public float happyAnimationSeconds = 2f;
    [Tooltip("How long the pair stays behind the wall (out of line of sight via sprite sorting, not hidden) before walking back to work.")]
    public float hiddenBehindWallSeconds = 2.5f;
    [Tooltip("Both occupants path to the same single MatingSpot Transform — this is how far each is nudged sideways on arrival (male toward -X, female toward +X) so their sprites don't render stacked directly on top of each other. Purely cosmetic, applied once at arrival.")]
    public float matingSpotOffsetX = 0.15f;

    [Header("Litter Size Odds")]
    [Tooltip("Chance a successful mating produces triplets instead of a single kid.")]
    [Range(0f, 1f)] public float tripletChance = 0.05f;
    [Tooltip("Chance a successful mating produces twins instead of a single kid. Checked after triplets, so the two can never both fire off the same conception.")]
    [Range(0f, 1f)] public float twinChance = 0.10f;

    [Header("Pregnancy & Incubation")]
    public float pregnancyDurationMinSeconds = 300f;
    public float pregnancyDurationMaxSeconds = 600f;
    public float eggIncubationMinSeconds = 300f;
    public float eggIncubationMaxSeconds = 600f;

    [Header("Kid Bunny")]
    [Tooltip("Uniform scale applied to a kid bunny's transform at hatch (NPCBunny.SetIsKidBunny) so it reads as visibly smaller than an adult — same prefab/rig, no separate kid art. 1 = adult size.")]
    [Range(0.1f, 1f)] public float kidBunnyScale = 0.7f;

    [Header("Egg Prefab")]
    [Tooltip("The single shared Egg prefab (Egg.cs) instantiated by NPCBunny.OnArrivedAtHatcherySpot on lay. One universal prefab, not per-type — the type-specific look comes from Egg applying BunnyTypeDefinition.eggSprite dynamically, not from separate prefabs.")]
    public GameObject eggPrefab;

    [Header("Hatchery Tending (bunnies assigned to a Hatchery's TendingSpots speed up incubation — Fire/Light types more than other types)")]
    [Tooltip("Incubation speed bonus contributed PER actively-tending Fire or Light bunny, summed across everyone currently tending (e.g. 0.15 = +15% per Fire/Light tender, so two give +30%).")]
    [Range(0f, 2f)] public float fireOrLightTenderHatchSpeedBonus = 0.15f;
    [Tooltip("Incubation speed bonus contributed PER actively-tending bunny of any OTHER type, summed the same way (e.g. 0.05 = +5% per other-type tender, so two give +10%). A mixed room (one Fire/Light + one other) sums both rates.")]
    [Range(0f, 2f)] public float otherTypeTenderHatchSpeedBonus = 0.05f;
}
