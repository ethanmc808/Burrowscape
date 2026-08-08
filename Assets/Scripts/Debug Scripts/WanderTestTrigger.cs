using UnityEngine;

public class WanderTestTrigger : MonoBehaviour
{
    [SerializeField] private NPCBunny bunnyToTest;
    [SerializeField] private bool triggerOnStart = true;

    private void Start()
    {
        if (triggerOnStart)
        {
            TriggerWander();
        }
    }

    [ContextMenu("Trigger Wander")]
    public void TriggerWander()
    {
        if (bunnyToTest == null)
        {
            Debug.LogWarning("WanderTestTrigger: no bunny assigned.");
            return;
        }

        bunnyToTest.EnterBaseAndWander();
        Debug.Log($"{bunnyToTest.name} is now wandering.");
    }
}