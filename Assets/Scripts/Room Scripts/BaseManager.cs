using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BaseManager : MonoBehaviour
{
    public static BaseManager Instance { get; private set; }

    private List<CafeteriaRoom> cafeterias = new List<CafeteriaRoom>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RegisterCafeteria(CafeteriaRoom cafeteria)
    {
        if (!cafeterias.Contains(cafeteria))
            cafeterias.Add(cafeteria);
    }

    public void UnregisterCafeteria(CafeteriaRoom cafeteria)
    {
        cafeterias.Remove(cafeteria);
    }

    public CafeteriaRoom FindNearestCafeteria(Vector3 fromPosition)
    {
        CafeteriaRoom nearest = null;
        float nearestDist = float.MaxValue;

        foreach (CafeteriaRoom cafeteria in cafeterias)
        {
            if (cafeteria == null) continue; // safety check for destroyed rooms

            float dist = Vector3.Distance(fromPosition, cafeteria.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = cafeteria;
            }
        }

        return nearest;
    }
}