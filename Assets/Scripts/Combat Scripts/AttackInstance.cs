using UnityEngine;

// Runtime behavior for the "AttackInstance" prefab concept from Combat_DesignDoc.md's "Attack visuals"
// section — the moving (or stationary, for melee) carrier that a Collider + particle-effect VFX would be
// authored onto in-Editor. This script only handles the LOGIC side (homing, arrival, fizzle, calling
// CombatResolver); the actual Collider/ParticleSystem hierarchy on the prefab is still art/Editor work
// Ethan needs to do, same "null until content exists" boundary as everywhere else in this project.
//
// Deliberately does NOT use physics collision (OnTriggerEnter) to detect arrival — see the design doc's
// reasoning (fast small colliders can tunnel between physics steps, and this project has hit collision/
// click fragility issues before). Arrival is a plain distance check against the target's live position.
//
// NOT wired to anything yet — nothing in the codebase currently calls Launch(). Whatever attack-trigger
// logic eventually decides "this bunny/enemy attacks now" (part of the not-yet-built combat AI/state
// machine integration) should Instantiate this prefab and call Launch() once.
public class AttackInstance : MonoBehaviour
{
    [SerializeField] private float travelSpeed = 10f; // units/second, irrelevant when stationary
    [SerializeField] private float arrivalThreshold = 0.1f;

    // Optional one-shot impact VFX (e.g. a splat/burst particle system) — played on Resolve() before the
    // object despawns. Left unassigned, behavior is identical to before this field existed (immediate
    // destroy). Set impactLingerSeconds to roughly the burst's Start Lifetime so it finishes rendering.
    [SerializeField] private ParticleSystem impactVFX;
    [SerializeField] private float impactLingerSeconds = 1f;

    private ICombatant attacker;
    private ICombatant target;
    private bool isStationary;
    private bool resolved;

    // isStationary should be true for melee attacks (per AttackSource.isMelee on the attacker) — the
    // instance snaps straight to the target's position and resolves the same frame, rather than homing
    // across distance like a ranged projectile.
    public void Launch(ICombatant attacker, ICombatant target, bool isStationary)
    {
        this.attacker = attacker;
        this.target = target;
        this.isStationary = isStationary;

        if (isStationary && target.CombatTransform != null)
            transform.position = target.CombatTransform.position;
    }

    private void Update()
    {
        if (resolved) return;

        // Fizzle: target's GameObject is gone, or it's already been defeated by something else. Checked
        // via CombatGameObject (a real UnityEngine.Object) rather than `target == null` directly — a
        // destroyed MonoBehaviour referenced through an interface doesn't trip Unity's overridden null
        // check the way a direct Component/GameObject reference does.
        if (target == null || target.CombatGameObject == null || !target.IsAlive)
        {
            Fizzle();
            return;
        }

        if (isStationary)
        {
            Resolve();
            return;
        }

        Vector3 targetPos = target.CombatTransform.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, travelSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= arrivalThreshold)
            Resolve();
    }

    private void Resolve()
    {
        resolved = true;
        CombatResolver.ResolveHit(attacker, target);
        // Floating damage numbers / hit-vs-miss look / hit-reaction animation trigger are not built yet —
        // none of that UI exists. Whatever consumes CombatHitResult should hook in here once it does.

        if (impactVFX != null)
        {
            impactVFX.Play();
            Destroy(gameObject, impactLingerSeconds);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Fizzle()
    {
        resolved = true;
        // Per the design doc: disappears with no damage. Optionally plays impact VFX in place — no such
        // VFX exists yet, so this is a plain despawn for now.
        Destroy(gameObject);
    }
}
