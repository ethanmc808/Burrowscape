using UnityEngine;

// Relay component for enemy rigs whose Animator lives on a child GameObject, not the same object as
// EnemyInstance itself (confirmed true for Slime) — Animation Events call SendMessage on the Animator's
// OWN GameObject, not its parent, so EnemyInstance.ReleasePendingAttack can't be wired directly as an
// event target. Same shared-rig relay pattern as BunnyAnimationEvents/NPCBunny. Add this component to
// whichever GameObject actually carries the Animator on an enemy prefab, then wire its ReleaseAttack
// method as the Animation Event on that enemy's Attacking clip.
public class EnemyAnimationEvents : MonoBehaviour
{
    private EnemyInstance enemyInstance;

    private void Awake()
    {
        enemyInstance = GetComponentInParent<EnemyInstance>();
    }

    public void ReleaseAttack()
    {
        if (enemyInstance != null)
            enemyInstance.ReleasePendingAttack();
    }
}
