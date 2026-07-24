using UnityEngine;

// Temporary debug trigger for exercising Foraging dispatch/return before ForagingDispatchUI/
// ForagingScreenUI are wired up in the Editor — same reasoning as WanderTestTrigger.cs/
// RoomDeletionTester.cs letting past systems be verified in Play mode pre-UI. Safe to delete once the
// real UI is wired and confirmed working.
public class ForagingTestTrigger : MonoBehaviour
{
    [SerializeField] private NPCBunny bunnyToTest;
    [SerializeField] private ForagingLocationDefinition locationToTest;
    [SerializeField] private int potionCount = 0;
    [SerializeField] private ForagingAccessoryDefinition accessoryToTest;

    [ContextMenu("Dispatch Foraging")]
    public void TriggerDispatch()
    {
        if (bunnyToTest == null || locationToTest == null)
        {
            Debug.LogWarning("ForagingTestTrigger: bunnyToTest or locationToTest not assigned.");
            return;
        }
        if (ForagingManager.Instance == null)
        {
            Debug.LogWarning("ForagingTestTrigger: no ForagingManager in the scene.");
            return;
        }

        bool dispatched = ForagingManager.Instance.TryDispatch(bunnyToTest, locationToTest, potionCount, accessoryToTest);
        Debug.Log($"ForagingTestTrigger: TryDispatch({bunnyToTest.name}, {locationToTest.displayName}) -> {dispatched}");
    }

    [ContextMenu("Recall")]
    public void TriggerRecall()
    {
        if (bunnyToTest == null || ForagingManager.Instance == null) return;
        ForagingManager.Instance.RecallBunny(bunnyToTest);
        Debug.Log($"ForagingTestTrigger: recall requested for {bunnyToTest.name}.");
    }

    [ContextMenu("Log State")]
    public void LogState()
    {
        if (bunnyToTest == null) return;

        ForagingTripState trip = ForagingManager.Instance != null ? ForagingManager.Instance.GetTripState(bunnyToTest) : null;
        string tripInfo = trip != null
            ? $"location={trip.location?.displayName}, elapsed={trip.elapsedTripTime:F1}s, carried={trip.CarriedItemCount}/{trip.carryCapacity}, gold={trip.carriedGold}, xp={trip.xpAccumulator:F1}, potionsRemaining={trip.potionsRemaining}, recallRequested={trip.manualRecallRequested}"
            : "no active trip";

        Debug.Log($"ForagingTestTrigger: {bunnyToTest.name} CurrentState={bunnyToTest.CurrentState}, HP={bunnyToTest.HPValue}/{bunnyToTest.Stats.HP}, Energy={bunnyToTest.EnergyValue:F1}, returnCountdownActive={bunnyToTest.IsForagingReturnCountdownActive}, trip=({tripInfo})");
    }
}
