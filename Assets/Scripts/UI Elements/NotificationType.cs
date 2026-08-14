// Every player-facing notification funnels through NotificationManager tagged with one of these — each
// value maps to exactly one Inspector-authored NotificationDefinition (see NotificationManager.cs),
// which owns that notification's tier, exact wording, icon, sound, and duration. One enum value = one
// distinct message, deliberately granular — e.g. RoomFull/NoFreeBeds/NotEnoughGold are separate values
// rather than sharing a generic "Error" bucket, precisely so each can have its own tier and wording
// authored independently instead of fighting over one shared definition.
//
// EggHatched is now wired (see HatcheryRoom.HatchEgg — Breeding System plan, Phase 4); KidBunnyGrewUp is
// also now wired (see NPCBunny.GrowUp — Kid Bunny Growth follow-up to the same plan). SpecialVisitor,
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
//
// EVERY MEMBER BELOW HAS AN EXPLICIT INTEGER VALUE — DO NOT REMOVE THEM, AND NEVER INSERT A NEW MEMBER IN
// THE MIDDLE OF THIS LIST. Unity serializes an enum field (like NotificationDefinition.type in
// NotificationManager's Inspector list) by its raw underlying int, not its name. Without explicit values,
// C# assigns them by declaration order — so inserting a new member anywhere but the end silently reassigns
// every int after it, which desyncs every already-authored NotificationDefinition from the type it was
// actually meant to point at (confirmed 2026-08-05: this is exactly what broke every notification after
// KidBunnyGrewUp got inserted mid-list). To add a new notification: append a new line with the next unused
// number (currently 43+) anywhere convenient in the file — the numeric value is what matters for save-
// safety, not its position in the source. Never reuse a retired number, and never renumber an existing one.
public enum NotificationType
{
    // Special/Reveal
    EggHatched = 0,
    ForagingLocationUnlocked = 1,
    RoomTypeUnlocked = 2,
    NewBunnyType = 3,
    SpecialVisitor = 4,
    RecipeDiscovered = 5,

    // Standard
    LevelUp = 6,
    WildBunnyArrived = 7,
    QuestArrived = 8,
    QuestReturned = 9,
    QuestRecalled = 10,
    EnemyDefeated = 11,
    BunnyFainted = 12,
    BunnyDeath = 13,
    BunnyBanished = 14,
    BrewComplete = 15,

    // Alert/Warning
    InvasionSiege = 16,
    InvasionSpawn = 17,
    LowPower = 18,
    LowWater = 19,
    StorageFull = 20,
    RoomFull = 21,
    GuardRoomFull = 22,
    NoFreeBeds = 23,
    HatcheryFull = 24,
    PopulationCapFull = 25,
    NotEnoughGold = 26,
    NotEnoughPotions = 27,
    NotEnoughHerbs = 28,
    NoRouteToGate = 29,
    CantDeleteRoom = 30,
    CantDepartForaging = 31,
    LocationNotUnlocked = 32,
    NoRelaxSpot = 33,
    NoEatSpot = 34,
    NoDrinkSpot = 35,
    NoSleepSpot = 36,
    GettingHungry = 37,
    GettingThirsty = 38,
    GettingTired = 39,
    SaveFailedInvasionActive = 40,
    GameSaved = 41,

    // Appended after the fact — see the file header comment on why this MUST stay appended, never inserted.
    KidBunnyGrewUp = 42
}

// Same explicit-value reasoning as NotificationType above (NotificationDefinition.tier is serialized the
// same way) — pinned now as cheap insurance even though this list rarely changes.
public enum NotificationTier
{
    Special = 0,
    Standard = 1,
    Alert = 2
}
