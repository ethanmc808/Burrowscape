using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Physical egg placed in a Hatchery (see the Breeding System plan). A plain Component, not an NPCBunny —
// matches RoomSpot.TryClaim(Component)'s existing occupant-type flexibility, same reasoning EnemySpots
// already use to hold an EnemyInstance occupant instead of a bunny.
//
// type and litterMembers are both carried over verbatim from the mother's NPCBunny fields at lay time
// (NPCBunny.TryLayEgg) — everything about this litter was already fully resolved at conception
// (Bedroom.TriggerMatingSequence); nothing is rolled here or at hatch, only revealed (see decision #4 /
// point 4 in the plan — this is what makes hatching save-scum-proof).
public class Egg : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    public BunnyType Type { get; private set; }
    public List<LitterMemberData> LitterMembers { get; private set; } = new List<LitterMemberData>();
    public HatcheryRoom HatcheryRoom { get; private set; }
    public RoomSpot ClaimedSpot { get; private set; }

    private float incubationElapsed;
    private float incubationDuration;
    // Read by SaveManager.SaveEggs — mirrors NPCBunny's own read-only accessor pattern for its private
    // save-relevant fields.
    public float IncubationElapsed => incubationElapsed;
    public float IncubationDuration => incubationDuration;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    // elapsedSoFar defaults to 0 for a freshly-laid egg; SaveManager.LoadEggs (Phase 5) passes the saved
    // incubationElapsed instead when reconstructing an egg that was already partway through incubating.
    public void Initialize(BunnyType type, List<LitterMemberData> litterMembers, HatcheryRoom hatcheryRoom, RoomSpot claimedSpot, float duration, float elapsedSoFar = 0f)
    {
        Type = type;
        LitterMembers = litterMembers;
        HatcheryRoom = hatcheryRoom;
        ClaimedSpot = claimedSpot;
        incubationDuration = duration;
        incubationElapsed = elapsedSoFar;

        transform.position = claimedSpot.transform.position;
        ApplyTypeSprite();
    }

    private void Update()
    {
        // Eggs hatch on their own regardless of tending — HatchSpeedMultiplier is 1 (no bonus) whenever
        // no Fire/Light bunny is actively tending the room, it never gates progress, only speeds it up.
        // See HatcheryRoom.HatchSpeedMultiplier's own comment — read live every frame, not cached, so a
        // tender arriving/leaving mid-incubation takes effect immediately.
        float speedMultiplier = HatcheryRoom != null ? HatcheryRoom.HatchSpeedMultiplier : 1f;
        incubationElapsed += Time.deltaTime * speedMultiplier;

        if (incubationElapsed >= incubationDuration)
        {
            HatcheryRoom.HatchEgg(this);
            Destroy(gameObject);
        }
    }

    // Same "resolve BunnyTypeDefinition by type via WildBunnySpawner.BunnyTypes" lookup HatcheryRoom.
    // HatchEgg uses — kept independent (not shared) since Egg needs it immediately at lay time, well
    // before hatch. Leaves whatever sprite is already on the prefab's SpriteRenderer untouched if this
    // type has no eggSprite authored yet (not yet authored isn't an error state, same philosophy as
    // BunnyTypeDefinition.icon).
    private void ApplyTypeSprite()
    {
        if (spriteRenderer == null) return;

        WildBunnySpawner spawner = FindAnyObjectByType<WildBunnySpawner>();
        BunnyTypeDefinition typeDef = spawner != null
            ? spawner.BunnyTypes.FirstOrDefault(t => t != null && t.type == Type)
            : null;

        if (typeDef != null && typeDef.eggSprite != null)
            spriteRenderer.sprite = typeDef.eggSprite;
    }
}
