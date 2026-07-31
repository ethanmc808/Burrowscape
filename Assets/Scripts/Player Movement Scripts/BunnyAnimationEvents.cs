using UnityEngine;

public class BunnyAnimationEvents : MonoBehaviour
{
    private BunnyMovement bunnyMovement;
    private NPCBunny npcBunny;

    // TEMP — chasing the Eyes_Open/Eyes_Angry overlap-during-attack-transition bug. Logs whenever either
    // object's actual runtime state changes, so the Editor.log shows ground truth about whether both are
    // genuinely active at once (and for how long) instead of guessing from the Animator/clip data alone.
    // Remove once root-caused.
    private GameObject eyesOpen;
    private GameObject eyesAngry;
    private SpriteRenderer eyesAngryRenderer;
    private bool lastOpenActive;
    private bool lastAngryEnabled;

    private void Awake()
    {
        bunnyMovement = GetComponentInParent<BunnyMovement>();
        npcBunny = GetComponentInParent<NPCBunny>();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Eyes_Open") eyesOpen = t.gameObject;
            else if (t.name == "Eyes_Angry") { eyesAngry = t.gameObject; eyesAngryRenderer = t.GetComponent<SpriteRenderer>(); }
        }
    }

    private void Update()
    {
        if (eyesOpen == null || eyesAngry == null) return;

        bool openActive = eyesOpen.activeInHierarchy;
        bool angryEnabled = eyesAngryRenderer != null && eyesAngryRenderer.enabled && eyesAngry.activeInHierarchy;
        if (openActive != lastOpenActive || angryEnabled != lastAngryEnabled)
        {
            Debug.Log($"[VFXDEBUG] {name} t={Time.time:F3} EyesOpen.active={openActive} EyesAngry.active&enabled={angryEnabled}");
            lastOpenActive = openActive;
            lastAngryEnabled = angryEnabled;
        }
    }

    public void PerformJump()
    {
        if (bunnyMovement != null)
            bunnyMovement.PerformJump();
    }

    // Placed on each type's Attacking clip at the "release" frame — relays up to NPCBunny.
    // ReleasePendingAttack, same shared-rig pattern as PerformJump above (this component sits on the rig,
    // NPCBunny lives on the wrapper prefab that nests it).
    public void ReleaseAttack()
    {
        Debug.Log($"[VFXDEBUG] {name}.BunnyAnimationEvents.ReleaseAttack fired, npcBunny={(npcBunny != null ? npcBunny.name : "NULL")}");
        if (npcBunny != null)
            npcBunny.ReleasePendingAttack();
    }
}