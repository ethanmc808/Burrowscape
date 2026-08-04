using System.Collections;
using UnityEngine;

// Dynamically-spawned "Miss!"/"Crit!" popup — same visual idea as the bunny rig's permanent
// Effect_Trigger_LevelUp child (a single static sprite animated via position/scale/alpha), just spawned
// on demand and self-destroying instead of living permanently on a rig. AttackInstance.Resolve spawns
// this as a child of whichever ICombatant got hit, so this prefab's own authored local position (tune in
// the Inspector) places it relative to that target's CombatTransform.
//
// Does NOT use CounterFlipX (unlike Effect_Trigger_LevelUp) — CounterFlipX counters a flip on its own
// IMMEDIATE parent's composed world scale, which works for Effect_Trigger_LevelUp because it's a genuine
// child of the flip transform itself. This effect is parented at CombatTransform instead (needed for
// correct world position/scale — the real flip transform, bunnyScaleRoot/visualScaleRoot, sits 1-2 levels
// deeper and scaled far smaller), and CombatTransform's OWN scale never changes for any current rig, so
// CounterFlipX had nothing real to read and was rendering a constant, non-reactive orientation the whole
// time — confirmed backwards on slimes despite looking fine on bunnies (2026-08-03).
//
// Reads the target's mirror sign EXACTLY ONCE — at the moment of impact — then freezes it for the rest of
// this popup's ~0.7s life, even if the target turns around again while it's still on screen (Ethan's
// explicit ask, confirmed via a caught-in-the-act repro 2026-08-03: a bunny got crit, the text appeared
// correctly, the bunny then turned to face a new target, and the ALREADY-VISIBLE text flipped along with
// it — wrong, since a floating combat popup should reflect the moment of the hit, not keep tracking the
// target's facing afterward). That one read is deferred by a frame (see PlayRoutine's own comment) rather
// than taken synchronously in Show() — Show() runs during another object's Update() (AttackInstance.
// Resolve), with no ordering guarantee relative to the SAME target's own Update() (e.g. NPCBunny.
// HandleDefending re-evaluating ITS OWN attack facing every frame, entirely independent of who's hitting
// IT) — reading immediately could catch either the pre- or post-flip value non-deterministically. This was
// confirmed as the actual cause of the ORIGINAL "sometimes backwards" symptom on bunnies specifically
// (2026-08-03) — bunnies re-evaluate their own facing continuously while defending, slimes almost never
// do, so the race landed far more often on bunnies despite the same underlying bug existing on both.
[RequireComponent(typeof(SpriteRenderer))]
public class FloatingComboEffect : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("How far this drifts upward (local space) over floatFadeSeconds.")]
    [SerializeField] private float floatDistance = 0.4f;
    [Tooltip("Quick scale-up on spawn, before the hold/float/fade.")]
    [SerializeField] private float popInSeconds = 0.08f;
    [SerializeField] private float holdSeconds = 0.25f;
    [SerializeField] private float floatFadeSeconds = 0.4f;

    private ICombatant target;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    // Called immediately after Instantiate — sets which sprite to show and starts the animation.
    // Destroys itself instantly if sprite is null (art not made yet) rather than showing a blank popup,
    // same "null until content exists" convention as everywhere else in this project.
    public void Show(Sprite sprite, ICombatant hitTarget)
    {
        if (sprite == null) { Destroy(gameObject); return; }
        spriteRenderer.sprite = sprite;
        target = hitTarget;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        // Waiting one frame here (yield return null resumes right after every Update() has run for the
        // frame, before LateUpdate/rendering — see Unity's script execution order) guarantees the target's
        // own facing has already settled for this frame before we read it below, closing the race described
        // in this class's own header comment.
        yield return null;

        // Baked in ONCE, right here — see this class's own header comment for why this must never be
        // re-applied after this point. Reads correctly at the target's default (unmirrored) pose with world
        // scale.x negative — same calibration CounterFlipX originally used for the bunny rig's
        // Effect_Trigger_LevelUp child. Flips positive when the target was mirrored at the moment of impact.
        if (target != null && target.CombatGameObject != null)
        {
            Vector3 mirrorScale = transform.localScale;
            float mirrorMagnitude = Mathf.Abs(mirrorScale.x);
            mirrorScale.x = target.IsVisuallyMirrored ? mirrorMagnitude : -mirrorMagnitude;
            transform.localScale = mirrorScale;
        }

        Vector3 baseScale = transform.localScale;
        Vector3 basePos = transform.localPosition;
        Color baseColor = spriteRenderer.color;

        float elapsed = 0f;
        while (elapsed < popInSeconds)
        {
            elapsed += Time.deltaTime;
            transform.localScale = baseScale * Mathf.Clamp01(elapsed / popInSeconds);
            yield return null;
        }
        transform.localScale = baseScale;

        yield return new WaitForSeconds(holdSeconds);

        elapsed = 0f;
        while (elapsed < floatFadeSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / floatFadeSeconds);
            transform.localPosition = basePos + Vector3.up * floatDistance * t;
            spriteRenderer.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(1f, 0f, t));
            yield return null;
        }

        Destroy(gameObject);
    }
}
