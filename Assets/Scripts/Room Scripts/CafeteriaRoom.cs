using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class CafeteriaRoom : RoomBase   // CHANGED from : MonoBehaviour
{
    [SpotNamePrefix("EatingSpot")]
    [SerializeField] private List<RoomSpot> eatingSpots;
    // REMOVE: paths list (inherited)
    // REMOVE: GetPathToSpot method

    [SerializeField] private float eatingTickInterval = 2f;

    // If the carrot stockpile is completely empty, TryConsumeCarrot() keeps failing forever — without a
    // give-up point, EatingRoutine's while(!IsFullyFed()) loop never exits, permanently freezing the
    // bunny in Eating and never releasing its spot. Worse if the stuck bunny is a Garden worker: it could
    // never return to producing, guaranteeing the shortage never recovers. This many CONSECUTIVE failed
    // ticks (reset on any success) gives a brief production hiccup room to resolve itself before bailing.
    [SerializeField] private int maxConsecutiveFailedAttempts = 5;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterCafeteria(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterCafeteria(this);
    }

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in eatingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in eatingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    public void NotifyBunnyReadyToEat(NPCBunny bunny)
    {
        StartCoroutine(EatingRoutine(bunny));
    }

    private IEnumerator EatingRoutine(NPCBunny bunny)
    {
        int consecutiveFailedAttempts = 0;

        while (!bunny.IsFullyFed())
        {
            yield return new WaitForSeconds(eatingTickInterval);

            if (CarrotManager.Instance.TryConsumeCarrot())
            {
                CarrotManager.Instance.RecordConsumption(1);
                bunny.ReceiveCarrotNutrition();
                consecutiveFailedAttempts = 0;
            }
            else if (++consecutiveFailedAttempts >= maxConsecutiveFailedAttempts)
            {
                break; // stockpile's empty and staying empty — stop occupying the spot
            }
        }

        bunny.FinishEatingAndReturnToWork();
    }
}