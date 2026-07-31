using System.Collections;
using UnityEngine;

// Punchy alternative to LineAttackRenderer's continuous procedural jitter (parked for a future type,
// per design call — this attack wants a strong single snapshot instead of an animating line). Picks one
// of a set of hand-drawn bolt sprite variations at random each time it's enabled, stretches/rotates it to
// span attacker->target (so it reads as one connected bolt regardless of their distance apart), then
// flashes it in and fades it out.
//
// Art convention this depends on: each bolt sprite must be drawn horizontally (pointing left-to-right)
// with its pivot set to Center — the stretch math below rotates around and scales along local X, so a
// sprite drawn any other way will skew instead of stretching cleanly.
[RequireComponent(typeof(SpriteRenderer))]
public class LightningBoltFlash : MonoBehaviour
{
    [SerializeField] private Sprite[] boltVariations;
    [SerializeField] private float flashInSeconds = 0.05f;
    [SerializeField] private float holdSeconds = 0.1f;
    [SerializeField] private float fadeOutSeconds = 0.25f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private SpriteRenderer spriteRenderer;
    private AttackInstance ownerAttack;
    private MaterialPropertyBlock propBlock;
    private Color baseTint = Color.white;
    // The prefab-authored Y/Z scale, captured once — kept as a fixed baseline so the flash-in/fade-out
    // Y animation and the every-frame X re-stretch (see StretchBetweenEndpoints) never fight over the
    // same axis or compound off each other's already-modified value.
    private Vector3 authoredScale = Vector3.one;
    private float stretchedScaleX = 1f;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        ownerAttack = GetComponentInParent<AttackInstance>();
        propBlock = new MaterialPropertyBlock();
        authoredScale = transform.localScale;
        // Captures whatever tint is authored on the material (e.g. turquoise) so fading can scale that
        // color's brightness toward black instead of overwriting it with a flat grayscale value.
        if (spriteRenderer.sharedMaterial != null && spriteRenderer.sharedMaterial.HasColor(BaseColorId))
            baseTint = spriteRenderer.sharedMaterial.GetColor(BaseColorId);
        if (ownerAttack != null)
            ownerAttack.OnLaunched += HandleLaunched;
    }

    private void OnDestroy()
    {
        if (ownerAttack != null)
            ownerAttack.OnLaunched -= HandleLaunched;
    }

    // Runs at the actual moment Launch() assigns attacker/target — NOT OnEnable, which fires at
    // Instantiate() time, before AttackerTransform/TargetTransform have anything to report yet.
    private void HandleLaunched()
    {
        if (boltVariations != null && boltVariations.Length > 0)
            spriteRenderer.sprite = boltVariations[Random.Range(0, boltVariations.Length)];

        StretchBetweenEndpoints();

        StopAllCoroutines();
        StartCoroutine(PlayFlash());
    }

    // Re-anchors this object at the LIVE midpoint between attacker and target, rotated/stretched to span
    // their current distance — called every frame for the flash's whole lifetime (see PlayFlash/Fade/
    // Hold below), not just once at launch. The parent AttackInstance keeps homing toward the target
    // after Launch() (see AttackInstance.Update's travelSpeed-driven movement) — since a child's LOCAL
    // position stays fixed once set, positioning this only once at launch let the parent's own travel
    // drag it out of place by the time the flash was actually visible, bunching it up near the target
    // instead of spanning attacker->target. Only ever touches X (the stretch axis) and position/rotation
    // — Y is driven separately by the flash-in/fade-out intensity animation in Fade(), off the fixed
    // authoredScale baseline, so the two never fight over the same axis.
    private void StretchBetweenEndpoints()
    {
        if (ownerAttack == null)
            return;

        Vector3 from = ownerAttack.AttackerOrigin;
        Vector3 to = ownerAttack.TargetOrigin;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        transform.position = (from + to) / 2f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        float spriteWidth = spriteRenderer.sprite != null ? spriteRenderer.sprite.bounds.size.x : 0f;
        stretchedScaleX = spriteWidth > 0f ? distance / spriteWidth : 1f;
        transform.localScale = new Vector3(stretchedScaleX, transform.localScale.y, authoredScale.z);
    }

    private IEnumerator PlayFlash()
    {
        // Flash-in grows vertically (0 -> full thickness) as well as brightening — reads as a spark
        // igniting. Fade-out deliberately does NOT touch scale — pure color/alpha dim only, so it reads
        // as fading away rather than shrinking/squishing.
        yield return Fade(0f, 1f, flashInSeconds, animateScale: true);
        yield return Hold(holdSeconds);
        yield return Fade(1f, 0f, fadeOutSeconds, animateScale: false);
    }

    // Holds at full brightness/scale for `duration` — still re-tracks position every frame so the flash
    // doesn't drift out of place if the parent AttackInstance is still settling into its final position.
    private IEnumerator Hold(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            StretchBetweenEndpoints();
            yield return null;
        }
    }

    private IEnumerator Fade(float from, float to, float duration, bool animateScale)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            StretchBetweenEndpoints();
            float intensity = Mathf.Lerp(from, to, duration <= 0f ? 1f : t / duration);
            SetTint(intensity);
            if (animateScale)
                transform.localScale = new Vector3(stretchedScaleX, authoredScale.y * intensity, authoredScale.z);
            yield return null;
        }
        StretchBetweenEndpoints();
        SetTint(to);
        if (animateScale)
            transform.localScale = new Vector3(stretchedScaleX, authoredScale.y * to, authoredScale.z);
    }

    // Drives the shader's actual _BaseColor property directly via a MaterialPropertyBlock, rather than
    // relying on SpriteRenderer.color — that's really a per-vertex tint that only sprite-aware shaders
    // (Sprite-Unlit-Default, Sprite-Lit-Default) read automatically, and those don't expose an Additive
    // blend option in the Inspector. The general Unlit shader we need for Additive blending doesn't read
    // vertex color, but it does have a real _BaseColor uniform, which this reaches directly instead.
    private void SetTint(float intensity)
    {
        spriteRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor(BaseColorId, baseTint * intensity);
        spriteRenderer.SetPropertyBlock(propBlock);
    }
}
