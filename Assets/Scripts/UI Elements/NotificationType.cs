// Every player-facing notification funnels through NotificationManager tagged with one of these — each
// value maps to exactly one Inspector-authored NotificationDefinition (see NotificationManager.cs),
// which owns that notification's tier, exact wording, icon, sound, and duration. One enum value = one
// distinct message, deliberately granular — e.g. RoomFull/NoFreeBeds/NotEnoughGold are separate values
// rather than sharing a generic "Error" bucket, precisely so each can have its own tier and wording
// authored independently instead of fighting over one shared definition.
//
// EggHatched, SpecialVisitor, BunnyDeath, and BunnyBanished have no underlying game system yet (no
// egg/breeding, merchant/visitor, or death/banish mechanic exists) — stubbed here so NotificationManager
// is ready the moment those features land, but nothing currently fires them.
public enum NotificationType
{
    // Special/Reveal
    EggHatched,
    ForagingLocationUnlocked,
    RoomTypeUnlocked,
    NewBunnyType,
    SpecialVisitor,

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

    // Alert/Warning
    InvasionSiege,
    InvasionSpawn,
    LowPower,
    LowWater,
    StorageFull,
    RoomFull,
    GuardRoomFull,
    NoFreeBeds,
    NotEnoughGold,
    NotEnoughPotions,
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
    GettingTired
}

public enum NotificationTier
{
    Special,
    Standard,
    Alert
}
