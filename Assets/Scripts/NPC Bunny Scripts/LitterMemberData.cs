// One sibling's fully-resolved data within a breeding litter (see the Breeding System plan doc). Every
// field here is rolled exactly once, at the moment of conception (Bedroom.TriggerMatingSequence), and
// never re-rolled later — hatching only reveals what's already been decided, which is what makes the
// eventual Kid Bunny save-scum-proof (reloading before a hatch can't change the outcome, since there's
// nothing left to re-roll).
//
// Litter-wide data (the shared Type, litter size) lives on whatever's carrying the litter — the pregnant
// NPCBunny during gestation, then the Egg after she lays — NOT duplicated per-member here, since every
// sibling in one litter shares the same Type by design (see decision #6 in the plan).
//
// [System.Serializable] (not a ScriptableObject) so JsonUtility can round-trip a List<LitterMemberData>
// directly as a field of BunnySaveData/EggSaveData with no extra conversion step, same shape as every
// other plain save-data class in Assets/Scripts/Save System/SaveData.cs.
[System.Serializable]
public class LitterMemberData
{
    // IVs — the "touched" stat(s) inherited from whichever parent had the higher value there are copied
    // in verbatim by whoever builds this struct; every other stat is freshly randomized independently per
    // sibling (see decision #4 in the plan) — this class doesn't know or care which is which, it just
    // holds the final resolved values.
    public int ivHP;
    public int ivAttack;
    public int ivDefense;
    public int ivSpeed;
    public int ivLuck;

    // Resolved against BunnyTraitCatalog.Instance.AllTraits by id on hatch, same pattern SaveManager
    // already uses for a wild bunny's saved traitIds.
    public string traitId;

    public BunnyGender gender;

    // Drawn from WildBunnyNames at conception time (not hatch), so the no-repeat-until-exhausted pool is
    // correctly consumed immediately even though the name isn't shown to the player until the hatch
    // notification fires.
    public string name;
}
