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
        while (!bunny.IsFullyFed())
        {
            yield return new WaitForSeconds(eatingTickInterval);

            if (CarrotManager.Instance.TryConsumeCarrot())
            {
                CarrotManager.Instance.RecordConsumption(1);
                bunny.ReceiveCarrotNutrition();
            }
        }

        bunny.FinishEatingAndReturnToWork();
    }
}