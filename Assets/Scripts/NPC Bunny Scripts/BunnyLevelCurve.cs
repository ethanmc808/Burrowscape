using UnityEngine;

// Pokemon's "Fast" growth group, single shared curve for every bunny regardless of type — see
// Foraging_DesignDoc.md's "Level curve" section. Feeds NPCBunny.AddExperience/LevelUp, the previously
// unwired scaffolding from the Bunny Stat System Redesign.
public static class BunnyLevelCurve
{
    public static int CumulativeXPForLevel(int level)
    {
        return Mathf.RoundToInt(0.8f * level * level * level);
    }
}
