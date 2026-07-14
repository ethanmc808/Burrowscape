using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DwellerRoster : MonoBehaviour
{
    public static DwellerRoster Instance { get; private set; }

    private List<NPCBunny> allBunnies = new List<NPCBunny>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(NPCBunny bunny)
    {
        if (!allBunnies.Contains(bunny))
            allBunnies.Add(bunny);
    }

    public void Unregister(NPCBunny bunny)
    {
        allBunnies.Remove(bunny);
    }

    public List<NPCBunny> GetUnassignedBunnies()
    {
        return allBunnies.Where(b => !b.IsAssignedToJob).ToList();
    }
}