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
    [Tooltip("How long a kid bunny stays a kid before automatically growing up (NPCBunny.GrowUp) — a flat duration, not a random range (see the Kid Bunny Growth plan's decision #1). Set to 60 (1 minute) for quick testing; the real target is 3600 (1 hour).")]
    public float kidGrowthDurationSeconds = 60f;
    [Tooltip("How long the visual scale fade from kidBunnyScale up to 1 (adult size) takes once GrowUp fires — purely cosmetic, doesn't gate job/forage eligibility (that unlocks instantly the moment growth completes).")]
    public float kidGrowthFadeSeconds = 1.5f;

    [Header("Egg Prefab")]
    [Tooltip("The single shared Egg prefab (Egg.cs) instantiated by NPCBunny.OnArrivedAtHatcherySpot on lay. One universal prefab, not per-type — the type-specific look comes from Egg applying BunnyTypeDefinition.eggSprite dynamically, not from separate prefabs.")]
    public GameObject eggPrefab;
    [Tooltip("World-Y nudge applied on top of the claimed HatcherySpot's own position (Egg.Initialize) — the egg sprites are imported with a CENTER pivot, so placing the object exactly at spot-height renders the bottom half of the sprite sinking below the floor. This lifts it by roughly half the sprite's world-space height instead. Approximate, not computed from the sprite's actual pixel dimensions — nudge to taste in Play mode if it's not quite sitting right.")]
    public float eggPlacementYOffset = 0.25f;

    [Header("Egg Progression (see the Egg Progression plan — hand-authored Animator clips on Egg.cs, not procedural code; this config only holds the pacing split, not the animation itself)")]
    [Tooltip("How far through incubation (0-1) before Egg fires its CloseToHatching Animator trigger — e.g. 0.75 = the hand-authored 'egg_incubating' breathing loop plays for the first 75% of incubation, then 'egg_closetohatching' takes over for the last 25%. No sprite swap accompanies this — deliberately just the one eggSprite for an egg's whole life (see BunnyTypeDefinition.eggSprite's own comment).")]
    [Range(0f, 1f)] public float eggHatchingSpriteThreshold = 0.75f;

    [Header("Egg Hatch VFX/SFX (see the Egg Progression plan)")]
    [Tooltip("One-shot VFX prefab instantiated at the egg's position the instant it hatches (Egg.HatchNow, right before the egg itself is destroyed and HatcheryRoom.HatchEgg spawns the litter). One shared prefab for every type, same 'one universal asset, not per-type' reasoning as eggPrefab above. Carries the whole hatch moment on its own — deliberately no 'split open' animation/sprite (see the Egg Progression plan). Null is a graceful no-op — no VFX authored yet isn't an error state.")]
    public GameObject eggHatchVFXPrefab;
    [Tooltip("How long the spawned eggHatchVFXPrefab instance is kept alive before being destroyed — a flat fallback duration rather than reading the particle system's own timing, so any VFX prefab (even a placeholder with unusual Start Lifetime/Duration values) gets cleaned up reliably instead of leaking a stopped-but-not-destroyed GameObject.")]
    public float eggHatchVFXLifetimeSeconds = 2f;
    [Tooltip("One-shot SFX played (AudioManager.PlaySFXAtPosition, same pattern as NPCBunny's levelUpClip) at the same moment/position as eggHatchVFXPrefab above. One shared clip for every type. Null is a graceful no-op — PlaySFXAtPosition already skips a null clip.")]
    public AudioClip eggHatchClip;

    [Header("Hatchery Tending (bunnies assigned to a Hatchery's TendingSpots speed up incubation — Fire/Light types more than other types)")]
    [Tooltip("Incubation speed bonus contributed PER actively-tending Fire or Light bunny, summed across everyone currently tending (e.g. 0.15 = +15% per Fire/Light tender, so two give +30%).")]
    [Range(0f, 2f)] public float fireOrLightTenderHatchSpeedBonus = 0.15f;
    [Tooltip("Incubation speed bonus contributed PER actively-tending bunny of any OTHER type, summed the same way (e.g. 0.05 = +5% per other-type tender, so two give +10%). A mixed room (one Fire/Light + one other) sums both rates.")]
    [Range(0f, 2f)] public float otherTypeTenderHatchSpeedBonus = 0.05f;
}
