using UnityEngine;

// Attached to the shared Attacking state in NPC_Rabbit_Neutral_Controller (applies to all 4 bunny
// types, since only the state's Motion clip is overridden per type, not the state itself). Replaces the
// Eyes_Open/Eyes_Angry curves that used to live on the Idle/Attacking clips directly — Unity doesn't
// blend GameObject.active/Renderer.enabled toggles smoothly across an Animator crossfade, so both stayed
// simultaneously "on" for the full 0.25s transition in both directions. Driving the swap here instead,
// with the curves removed from the clips entirely, means nothing else ever writes to these two objects,
// so there's nothing left for this to race against — the swap always lands cleanly on OnStateEnter/Exit.
public class AttackFaceOverride : StateMachineBehaviour
{
    private GameObject eyesOpen;
    private GameObject eyesAngry;
    private SpriteRenderer eyesAngryRenderer;
    private bool cached;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        CacheReferences(animator);
        if (eyesOpen != null) eyesOpen.SetActive(false);
        if (eyesAngryRenderer != null) eyesAngryRenderer.enabled = true;
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        CacheReferences(animator);
        if (eyesOpen != null) eyesOpen.SetActive(true);
        if (eyesAngryRenderer != null) eyesAngryRenderer.enabled = false;
    }

    // Unity clones this behaviour once per Animator it's attached to, so instance fields are safe to use
    // as this-animator's own cache — searched once on first use rather than every enter/exit.
    private void CacheReferences(Animator animator)
    {
        if (cached) return;
        cached = true;

        foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Eyes_Open") eyesOpen = t.gameObject;
            else if (t.name == "Eyes_Angry") { eyesAngry = t.gameObject; eyesAngryRenderer = t.GetComponent<SpriteRenderer>(); }
        }
    }
}
