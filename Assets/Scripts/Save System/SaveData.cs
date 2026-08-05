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

    // Position — floorIndex/facing aren't saved separately: RestoreRoomAssignment derives floor from the
    // resolved room, and facing is purely cosmetic sprite-flip state that self-corrects on the next move.
    public float posX, posY, posZ;

    // Room/job assignment — see SavedBunnyRole. roomInstanceId is empty when savedState == Idle
    // (unassigned). Re-resolved against whatever RoomBase.InstanceId got reconstructed with this same
    // value during the room-reconstruction pass, which always runs before bunnies on load.
    public SavedBunnyRole savedState;
    public string roomInstanceId;
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
}
