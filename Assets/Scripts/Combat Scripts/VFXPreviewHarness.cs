using UnityEngine;

// Disposable test harness for previewing attack VFX in Play mode through the SAME path real combat
// uses, without needing the real combat AI/trigger layer (which doesn't exist yet) or InvasionManager/
// room setup. Drop this on any empty GameObject in a test scene, assign Attacker Object / Target Object
// to real NPCBunny or EnemyInstance GameObjects already placed in the scene, then press Play and hit the
// Fire Key (or enable Auto Repeat). Fire() calls the attacker's TestFireAttack, which triggers its actual
// Attacking animation via its real Animator param — the Attacking clip's existing Animation Event then
// calls ReleasePendingAttack -> CombatEngagement.ReleaseAttack, spawning the VFX at the animation's real
// "release" frame exactly like true combat, not a scripted instant-spawn. Swap which GameObject is
// assigned to either field mid-Play-mode to compare bunnies/enemies without restarting — resolved fresh
// every Fire(), not cached at Awake.
//
// NOT part of the shipped combat system. Delete this once all attack VFX prefabs are built and wired the
// real way via the eventual combat AI triggering NPCBunny.HandleDefending/EnemyInstance.Update normally.
public class VFXPreviewHarness : MonoBehaviour
{
    [Tooltip("A real NPCBunny or EnemyInstance GameObject placed in the scene to act as the attacker.")]
    [SerializeField] private GameObject attackerObject;
    [Tooltip("Only needed if Attacker Object is an NPCBunny placed directly in the scene (never spawned via WildBunnySpawner, so it has no type/stats yet) — assign the matching Bunny Types asset, e.g. Water.asset. Ignored if the bunny already has real progression, or if Attacker Object is an EnemyInstance.")]
    [SerializeField] private BunnyTypeDefinition attackerTypeOverride;
    [Tooltip("A real NPCBunny or EnemyInstance GameObject placed in the scene to act as the target.")]
    [SerializeField] private GameObject targetObject;
    [Tooltip("Same as Attacker Type Override, but for Target Object.")]
    [SerializeField] private BunnyTypeDefinition targetTypeOverride;

    [SerializeField] private KeyCode fireKey = KeyCode.Space;
    [SerializeField] private bool autoRepeat;
    [SerializeField] private float autoRepeatInterval = 1.5f;

    private float autoRepeatTimer;

    private void Update()
    {
        if (Input.GetKeyDown(fireKey))
            Fire();

        if (autoRepeat)
        {
            autoRepeatTimer += Time.deltaTime;
            if (autoRepeatTimer >= autoRepeatInterval)
            {
                autoRepeatTimer = 0f;
                Fire();
            }
        }
    }

    private void Fire()
    {
        // Resolved for its auto-init side effect (see ResolveCombatant) even though attacker is then
        // re-fetched as a concrete type below — TestFireAttack isn't part of ICombatant (it's a test-only
        // hook, deliberately kept off the shared production interface), so it needs the concrete
        // NPCBunny/EnemyInstance component to call, not just the ICombatant surface.
        ICombatant attacker = ResolveCombatant(attackerObject, attackerTypeOverride);
        ICombatant target = ResolveCombatant(targetObject, targetTypeOverride);
        if (attacker == null || target == null)
        {
            Debug.LogWarning("VFXPreviewHarness: assign Attacker Object and Target Object (each needs an NPCBunny or EnemyInstance component) before firing.");
            return;
        }

        if (attackerObject.TryGetComponent(out NPCBunny bunny))
            bunny.TestFireAttack(target);
        else if (attackerObject.TryGetComponent(out EnemyInstance enemy))
            enemy.TestFireAttack(target);
    }

    // Resolves the real ICombatant on a dropped-in GameObject every call (not cached) so reassigning
    // Attacker Object/Target Object mid-Play-mode takes effect on the very next Fire(). Auto-initializes
    // whichever real progression step this GameObject would otherwise only get from its normal spawner —
    // EnemyInstance.Initialize (currentHP starts at 0 = dead until then) or NPCBunny.TestInitializeForPreview
    // (typeDefinition starts null, so AttackSource would silently be null) — either of which would
    // otherwise just silently no-op the attack with no visible VFX and no error.
    private ICombatant ResolveCombatant(GameObject obj, BunnyTypeDefinition typeOverride)
    {
        if (obj == null) return null;

        ICombatant combatant = obj.GetComponent<ICombatant>();
        if (combatant == null)
        {
            Debug.LogWarning($"VFXPreviewHarness: '{obj.name}' has no NPCBunny or EnemyInstance component.");
            return null;
        }

        if (combatant is EnemyInstance enemy && !combatant.IsAlive)
        {
            if (enemy.Definition == null)
            {
                Debug.LogWarning($"VFXPreviewHarness: '{obj.name}' is an uninitialized EnemyInstance with no Definition assigned to auto-init from.");
                return null;
            }
            enemy.Initialize(enemy.Definition, null);
        }
        else if (combatant is NPCBunny bunny && bunny.TypeDefinition == null)
        {
            if (typeOverride == null)
            {
                Debug.LogWarning($"VFXPreviewHarness: '{obj.name}' is an NPCBunny with no type set yet — assign its matching Bunny Types asset to the Type Override field.");
                return null;
            }
            bunny.TestInitializeForPreview(typeOverride);
        }

        return combatant;
    }
}
