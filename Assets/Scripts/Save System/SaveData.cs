using System.Collections.Generic;
using UnityEngine;

// Every serializable data shape SaveManager reads/writes. Plain data only — no MonoBehaviours, no live
// object references (rooms/bunnies are cross-referenced by string id, never by reference, since Unity
// object references can't survive a JSON round-trip anyway). JsonUtility (Unity's built-in serializer,
// used throughout this file) does not support Dictionary<TKey,TValue> at all — every place the live game
// state uses a Dictionary (PopulationManager's per-category counts, ForagingTripState's per-item loot
// counters) is flattened here into either fixed fields or a List of small key+count entry structs
// instead. See SaveManager.cs for how each section is actually populated/applied.

// ---------- Small reusable entry types (Dictionary replacements — see file header) ----------

[System.Serializable]
public class NamedCountEntry
{
    public string name;
    public int count;
}

[System.Serializable]
public class RarityCountEntry
{
    public ForagingLootRarity rarity;
    public int count;
}

// ---------- Resources ----------

[System.Serializable]
public class ResourceSaveData
{
    public int currentCarrots;
    public int currentGold;
    public int currentWater;
    // *Max for every resource is recomputed on load from whatever rooms get rebuilt (RegisterProducer/
    // RegisterCapacityContributor already do this automatically via RoomBase.OnEnable) — never saved.
    public float powerRationingPoolCurrent;
    public float waterRationingPoolCurrent;
}

// ---------- Population ----------

// Mirrors PopulationManager's 4 fixed ResidentCategory counts directly (no Dictionary — see file header)
// since that ledger is independent, explicitly-maintained state, not derivable by counting live bunnies.
[System.Serializable]
public class PopulationSaveData
{
    public int inBase;
    public int questing;
    public int foraging;
    public int egg;
}

// ---------- Rooms ----------

// Enough to re-resolve the exact prefab variant via RoomCatalogRegistry.FindVariant(roomTypeId,
// footprintWidth, grade) and place it at the same grid position — see RoomBase.cs's own comment on why
// roomTypeId+footprintWidth+grade (not RoomDefinition.Id) is the correct lookup key; nothing in the game
// stamps RoomDefinition.Id onto a built instance today.
[System.Serializable]
public class RoomSaveData
{
    public string instanceId;
    public string roomTypeId;
    public float footprintWidth;
    public int grade;
    public int gridX;
    public int floorIndex;

    // The room's TRUE world X, distinct from gridX above (which is gridX's own ROUNDED int — see
    // RoomBase.GridX). Round-tripping only gridX silently shifts any odd-footprint room by 0.5 units,
    // since its true center deliberately sits exactly halfway between two integer grid lines (see
    // BuildGridUtility.SnapCenterX's own comment) — every other room type has an even footprint width,
    // so this was never noticeable until LiftRoom (footprint 1). NaN sentinel (not a field JsonUtility
    // will ever overwrite from an older save that lacks this field) lets that older save fall back to
    // the previous gridX-only reconstruction instead of every one of its rooms snapping to X=0.
    public float worldX = float.NaN;
}

// ---------- Bunnies ----------

// A bunny's job/relax/sleep assignment at save time, snapped down to one of these 4 stable states —
// Defending/Fainted (combat) and every mid-transit state (MovingToSpot, PassingGate, WaitingForLift,
// etc.) are deliberately never saved; see BunnySaveData.savedState's own comment.
public enum SavedBunnyRole
{
    Idle,
    Working,
    Sleeping,
    Relaxing
}

[System.Serializable]
public class BunnySaveData
{
    // Identity
    public string bunnyName;
    public BunnyGender gender;
    public BunnyType bunnyType;

    // Stats/progression — Stats itself (HP/Attack/Defense/Speed/Luck) is re-derived via
    // BunnyStatCalculator.Resolve from the fields below on load, never saved directly.
    public int level;
    public float experience;
    public BunnyNature nature;
    public int ivHP, ivAttack, ivDefense, ivSpeed, ivLuck;
    public int evHP, evAttack, evDefense, evSpeed, evLuck;

    // Traits/passives — both already carry a stable string id designed for exactly this (see
    // BunnyTraitDefinition.id / BunnyPassiveDefinition.id); re-resolved on load against
    // BunnyTraitCatalog.Instance and bunnyType's own BunnyTypeDefinition.passives respectively. Traits
    // get re-APPLIED (ApplyTraitEffects) fresh from prefab defaults on load rather than saving the
    // already-mutated decay rates directly — that mutation isn't idempotent-safe to reapply blindly.
    public List<string> traitIds = new List<string>();
    public List<string> passiveIds = new List<string>();

    // Empty string = no accessory equipped. Resolved on load against whatever catalog
    // ForagingInventoryManager/ForagingAccessoryDefinition uses (see SaveManager's bunny reconstruction).
    public string equippedAccessoryName;

    // Needs (current values, not decay rates — those are prefab defaults re-applied via traits above)
    public float hunger;
    public float thirst;
    public float energy;
    public float mood;
    public int currentHP;

    // Position — floorIndex isn't saved separately: RestoreRoomAssignment derives it from the resolved
    // room. Facing WAS assumed cosmetic/self-correcting and left unsaved — true for Working/Relaxing
    // (the next work-wander/movement tick recomputes it almost immediately) but false for Sleeping, where
    // a reloaded bunny doesn't move again until she wakes, so the wrong facing stayed visibly wrong for
    // her whole remaining nap. Fixed not by saving facing but by having RestoreRoomAssignment explicitly
    // call SetFacing(spot.FacesRight) on every reclaim branch, same as a live arrival already does.
    public float posX, posY, posZ;

    // Room/job assignment — see SavedBunnyRole. roomInstanceId is empty when savedState == Idle
    // (unassigned). Re-resolved against whatever RoomBase.InstanceId got reconstructed with this same
    // value during the room-reconstruction pass, which always runs before bunnies on load.
    public SavedBunnyRole savedState;
    public string roomInstanceId;

    // Separate from roomInstanceId/savedState above — a job-assigned bunny who happens to be
    // Sleeping/Eating/Drinking/etc. at save time keeps her AssignedJobRoom live the whole time (see
    // NPCBunny.RestoreUnderlyingJobAssignment's own comment for why), but savedState/roomInstanceId can
    // only capture ONE role+room pair, whichever CurrentState currently is. This second field independently
    // records the job so it survives a reload that happens to land mid-interruption. Empty when the bunny
    // has no job assigned, or (harmlessly) duplicates roomInstanceId when savedState is already Working.
    public string underlyingJobRoomInstanceId;

    // ---------- Breeding (see the Breeding System plan) ----------
    public bool isPregnant;
    public float pregnancyElapsed;
    public float pregnancyDuration;
    // The whole litter, already fully resolved at conception — see LitterMemberData's own comment for why
    // nothing here is ever re-rolled on load (that's what keeps a hatch save-scum-proof). Empty/default
    // when isPregnant is false.
    public BunnyType litterType;
    public List<LitterMemberData> litterMembers = new List<LitterMemberData>();
    // Room only — no spot index, since nothing is reserved at a specific spot at conception, only counted
    // capacity (see HatcheryRoom.ReserveCapacity's own comment). Empty when isPregnant is false.
    public string claimedHatcheryRoomInstanceId;

    public bool isKidBunny;
}

// ---------- Eggs (see the Breeding System plan) ----------

// One entry per live, unhatched Egg across every registered HatcheryRoom (see SaveManager.SaveEggs/
// LoadEggs). Unlike a pregnant bunny above, an egg IS physically claimed to a specific RoomSpot — spotIndex
// here is real, resolved against the reconstructed HatcheryRoom's own spot list on load, same convention
// every other physically-claimed occupant in this file would use if any currently needed one.
[System.Serializable]
public class EggSaveData
{
    public string hatcheryRoomInstanceId;
    public int spotIndex;
    public BunnyType type;
    public List<LitterMemberData> litterMembers = new List<LitterMemberData>();
    public float incubationElapsed;
    public float incubationDuration;
}

// ---------- Unlocks ----------

[System.Serializable]
public class UnlockSaveData
{
    // RoomUnlockTracker's PermanentFlag ledger — currently unused by any live system (no
    // blueprint/discovery mechanic calls UnlockPermanently yet) but saved for forward-compatibility.
    public List<string> permanentlyUnlockedRoomIds = new List<string>();
    // BunnyTypeUnlockTracker — enum values serialize fine directly via JsonUtility's List<T> support.
    public List<BunnyType> unlockedBunnyTypes = new List<BunnyType>();
    // ForagingLocationUnlockTracker — saved by ForagingLocationDefinition asset name (its HashSet holds
    // direct asset references, which can't round-trip through JSON), re-resolved against
    // ForagingManager.Instance.Locations on load.
    public List<string> unlockedForagingLocationNames = new List<string>();
    // RoomTypeUnlockAnnouncer's own dedupe set is deliberately NOT here — it's pure notification
    // bookkeeping and already correctly re-seeds itself from live unlock state in its own Start().
}

// ---------- Active foraging trips ----------

// One entry per in-progress ForagingTripState at save time. bunnyIndex is this trip's owning bunny's
// position in SaveData.bunnies — trips don't need their own bunny-id scheme since both lists are
// reconstructed together in the same pass (see SaveManager.LoadGame). Resumed via a new
// ForagingManager.ResumeTrip(bunny, tripData) that re-enters RunActiveTripPhase at elapsedTripTime
// rather than replaying TryDispatch's gate-departure logic.
[System.Serializable]
public class ForagingTripSaveData
{
    public int bunnyIndex;
    public string locationName;
    public string equippedAccessoryName; // empty = none; independent of the bunny's OWN equippedAccessoryName in case it ever diverges
    public int potionsRemaining;
    public int carryCapacity;

    public int carriedCarrots;
    public int carriedGold;
    public int foundPotionCount;
    public int carriedCrystalCarrots;
    public List<string> foundAccessoryNames = new List<string>();
    public List<NamedCountEntry> foundFruits = new List<NamedCountEntry>();
    public List<NamedCountEntry> foundTrinkets = new List<NamedCountEntry>();
    public List<RarityCountEntry> foundHerbs = new List<RarityCountEntry>();

    public int enemiesSlain;
    public float elapsedTripTime;
    public float xpAccumulator;
    // recentLog (the trip detail panel's event log) is deliberately NOT saved — cosmetic history only,
    // safe to just start fresh from the resume point.
}

// ---------- Game speed ----------

[System.Serializable]
public class GameSpeedSaveData
{
    public int speedIndex;
}

// ---------- Bunny name pools ----------

// Delegates straight to WildBunnyNames' own existing UsedNameEntry shape (see WildBunnyNames.cs's
// ExportUsedNames/ImportUsedNames — this save system is exactly what that stub was written for).
[System.Serializable]
public class WildBunnyNamesSaveData
{
    public List<WildBunnyNames.UsedNameEntry> usedNames = new List<WildBunnyNames.UsedNameEntry>();
}

// ---------- Top level ----------

[System.Serializable]
public class SaveData
{
    public ResourceSaveData resources = new ResourceSaveData();
    public PopulationSaveData population = new PopulationSaveData();
    public List<RoomSaveData> rooms = new List<RoomSaveData>();
    public List<BunnySaveData> bunnies = new List<BunnySaveData>();
    public UnlockSaveData unlocks = new UnlockSaveData();
    public List<ForagingTripSaveData> activeTrips = new List<ForagingTripSaveData>();
    public GameSpeedSaveData gameSpeed = new GameSpeedSaveData();
    public WildBunnyNamesSaveData bunnyNames = new WildBunnyNamesSaveData();
    public List<EggSaveData> eggs = new List<EggSaveData>();
}
