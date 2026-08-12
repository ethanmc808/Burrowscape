// Every player-facing notification funnels through NotificationManager tagged with one of these — each
// value maps to exactly one Inspector-authored NotificationDefinition (see NotificationManager.cs),
// which owns that notification's tier, exact wording, icon, sound, and duration. One enum value = one
// distinct message, deliberately granular — e.g. RoomFull/NoFreeBeds/NotEnoughGold are separate values
// rather than sharing a generic "Error" bucket, precisely so each can have its own tier and wording
// authored independently instead of fighting over one shared definition.
//
// EggHatched is now wired (see HatcheryRoom.HatchEgg — Breeding System plan, Phase 4); SpecialVisitor,
// BunnyDeath, and BunnyBanished still have no underlying game system yet (no merchant/visitor or
// death/banish mechanic exists) — stubbed here so NotificationManager is ready the moment those features
// land, but nothing currently fires them. HatcheryFull/PopulationCapFull are new (Phase 1/2 of the same
// plan) — like every value here, each needs its own Inspector-authored NotificationDefinition on
// NotificationManager before it actually shows anything; the enum value alone is just the tag.
//
// RecipeDiscovered/BrewComplete/NotEnoughHerbs are new for the Laboratory room (see LaboratoryRoom,
// LaboratoryRecipeUnlockTracker). RecipeDiscovered currently only ever fires from
// LaboratoryRecipeUnlockTracker.DiscoverRecipe, which nothing calls yet (scaffolding for future foraging/
// quest/special-visitor recipe rewards) — like the others above, wiring an Inspector NotificationDefinition
// for it is a required manual step before it will show anything.
public enum NotificationType
{
    // Special/Reveal
    EggHatched,
    ForagingLocationUnlocked,
    RoomTypeUnlocked,
    NewBunnyType,
    SpecialVisitor,
    RecipeDiscovered,

    // Standard
    LevelUp,
    WildBunnyArrived,
    QuestArrived,
    QuestReturned,
    QuestRecalled,
    EnemyDefeated,
    BunnyFainted,
    BunnyDeath,
    BunnyBanished,
    BrewComplete,

    // Alert/Warning
    InvasionSiege,
    InvasionSpawn,
    LowPower,
    LowWater,
    StorageFull,
    RoomFull,
    GuardRoomFull,
    NoFreeBeds,
    HatcheryFull,
    PopulationCapFull,
    NotEnoughGold,
    NotEnoughPotions,
    NotEnoughHerbs,
    NoRouteToGate,
    CantDeleteRoom,
    CantDepartForaging,
    LocationNotUnlocked,
    NoRelaxSpot,
    NoEatSpot,
    NoDrinkSpot,
    NoSleepSpot,
    GettingHungry,
    GettingThirsty,
    GettingTired,
    SaveFailedInvasionActive,
    GameSaved
}

public enum NotificationTier
{
    Special,
    Standard,
    Alert
}
