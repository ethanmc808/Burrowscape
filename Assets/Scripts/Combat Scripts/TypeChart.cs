using System.Collections.Generic;

// Full 20-type attacker/defender effectiveness chart, authored in "Burrowscape Type Chart.xlsx" and
// transcribed here verbatim. Only non-neutral matchups are listed per attacker; anything not listed
// (including an attacker vs. itself, unless stated) is Neutral (1x). Deliberately no true 0x immunities
// — the worst matchup is ResistedMax (0.25x) so no attack is ever fully wasted in auto-combat.
public static class TypeChart
{
    public const float SuperEffective = 2f;
    public const float NotVeryEffective = 0.5f;
    public const float ResistedMax = 0.25f;
    public const float Neutral = 1f;

    private static readonly Dictionary<BunnyType, Dictionary<BunnyType, float>> Chart =
        new Dictionary<BunnyType, Dictionary<BunnyType, float>>
    {
        [BunnyType.Neutral] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Ghost] = ResistedMax,
        },
        [BunnyType.Fire] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Water] = NotVeryEffective,
            [BunnyType.Plant] = SuperEffective,
            [BunnyType.Ice] = SuperEffective,
            [BunnyType.Insect] = SuperEffective,
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Metal] = SuperEffective,
            [BunnyType.Light] = NotVeryEffective,
            [BunnyType.Draco] = NotVeryEffective,
        },
        [BunnyType.Water] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = SuperEffective,
            [BunnyType.Water] = NotVeryEffective,
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Stone] = SuperEffective,
            [BunnyType.Earth] = SuperEffective,
            [BunnyType.Draco] = NotVeryEffective,
        },
        [BunnyType.Plant] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Water] = SuperEffective,
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Toxic] = NotVeryEffective,
            [BunnyType.Insect] = NotVeryEffective,
            [BunnyType.Stone] = SuperEffective,
            [BunnyType.Earth] = SuperEffective,
            [BunnyType.Air] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Light] = SuperEffective,
            [BunnyType.Draco] = NotVeryEffective,
        },
        [BunnyType.Shock] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Water] = SuperEffective,
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Shock] = NotVeryEffective,
            [BunnyType.Earth] = ResistedMax,
            [BunnyType.Air] = SuperEffective,
            [BunnyType.Metal] = SuperEffective,
            [BunnyType.Light] = NotVeryEffective,
            [BunnyType.Draco] = NotVeryEffective,
        },
        [BunnyType.Ice] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Water] = SuperEffective,
            [BunnyType.Plant] = SuperEffective,
            [BunnyType.Ice] = NotVeryEffective,
            [BunnyType.Earth] = SuperEffective,
            [BunnyType.Air] = SuperEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Ghost] = NotVeryEffective,
            [BunnyType.Draco] = SuperEffective,
        },
        [BunnyType.Mind] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Mind] = NotVeryEffective,
            [BunnyType.Toxic] = SuperEffective,
            [BunnyType.Insect] = NotVeryEffective,
            [BunnyType.Melee] = SuperEffective,
            [BunnyType.Metal] = SuperEffective,
            [BunnyType.Dark] = ResistedMax,
        },
        [BunnyType.Toxic] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Water] = SuperEffective,
            [BunnyType.Plant] = SuperEffective,
            [BunnyType.Toxic] = NotVeryEffective,
            [BunnyType.Insect] = SuperEffective,
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Earth] = NotVeryEffective,
            [BunnyType.Air] = SuperEffective,
            [BunnyType.Metal] = ResistedMax,
            [BunnyType.Pixie] = SuperEffective,
            [BunnyType.Ghost] = NotVeryEffective,
            [BunnyType.Draco] = NotVeryEffective,
        },
        [BunnyType.Sound] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Ice] = SuperEffective,
            [BunnyType.Mind] = SuperEffective,
            [BunnyType.Sound] = NotVeryEffective,
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Earth] = NotVeryEffective,
            [BunnyType.Air] = SuperEffective,
            [BunnyType.Metal] = NotVeryEffective,
        },
        [BunnyType.Insect] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Plant] = SuperEffective,
            [BunnyType.Ice] = NotVeryEffective,
            [BunnyType.Mind] = SuperEffective,
            [BunnyType.Toxic] = NotVeryEffective,
            [BunnyType.Sound] = SuperEffective,
            [BunnyType.Melee] = NotVeryEffective,
            [BunnyType.Earth] = SuperEffective,
            [BunnyType.Air] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Pixie] = SuperEffective,
            [BunnyType.Ghost] = NotVeryEffective,
        },
        [BunnyType.Melee] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Neutral] = SuperEffective,
            [BunnyType.Ice] = SuperEffective,
            [BunnyType.Mind] = NotVeryEffective,
            [BunnyType.Toxic] = NotVeryEffective,
            [BunnyType.Stone] = SuperEffective,
            [BunnyType.Air] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Pixie] = NotVeryEffective,
            [BunnyType.Ghost] = ResistedMax,
            [BunnyType.Dark] = SuperEffective,
        },
        [BunnyType.Stone] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = SuperEffective,
            [BunnyType.Ice] = SuperEffective,
            [BunnyType.Insect] = SuperEffective,
            [BunnyType.Melee] = NotVeryEffective,
            [BunnyType.Earth] = NotVeryEffective,
            [BunnyType.Air] = SuperEffective,
            [BunnyType.Metal] = NotVeryEffective,
        },
        [BunnyType.Earth] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = SuperEffective,
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Shock] = SuperEffective,
            [BunnyType.Toxic] = SuperEffective,
            [BunnyType.Insect] = NotVeryEffective,
            [BunnyType.Stone] = SuperEffective,
            [BunnyType.Air] = ResistedMax,
            [BunnyType.Metal] = SuperEffective,
        },
        [BunnyType.Air] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Plant] = SuperEffective,
            [BunnyType.Shock] = NotVeryEffective,
            [BunnyType.Insect] = SuperEffective,
            [BunnyType.Melee] = SuperEffective,
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
        },
        [BunnyType.Metal] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Water] = NotVeryEffective,
            [BunnyType.Shock] = NotVeryEffective,
            [BunnyType.Ice] = SuperEffective,
            [BunnyType.Melee] = NotVeryEffective,
            [BunnyType.Stone] = SuperEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Pixie] = SuperEffective,
        },
        [BunnyType.Pixie] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Plant] = NotVeryEffective,
            [BunnyType.Toxic] = NotVeryEffective,
            [BunnyType.Melee] = SuperEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Light] = NotVeryEffective,
            [BunnyType.Dark] = SuperEffective,
            [BunnyType.Draco] = SuperEffective,
        },
        [BunnyType.Light] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Plant] = ResistedMax,
            [BunnyType.Toxic] = SuperEffective,
            [BunnyType.Insect] = SuperEffective,
            [BunnyType.Stone] = NotVeryEffective,
            [BunnyType.Earth] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Ghost] = SuperEffective,
            [BunnyType.Dark] = SuperEffective,
        },
        [BunnyType.Ghost] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Neutral] = ResistedMax,
            [BunnyType.Fire] = NotVeryEffective,
            [BunnyType.Shock] = SuperEffective,
            [BunnyType.Mind] = SuperEffective,
            [BunnyType.Sound] = SuperEffective,
            [BunnyType.Insect] = NotVeryEffective,
            [BunnyType.Light] = ResistedMax,
            [BunnyType.Ghost] = SuperEffective,
            [BunnyType.Dark] = NotVeryEffective,
        },
        [BunnyType.Dark] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Mind] = SuperEffective,
            [BunnyType.Insect] = NotVeryEffective,
            [BunnyType.Melee] = NotVeryEffective,
            [BunnyType.Pixie] = NotVeryEffective,
            [BunnyType.Light] = SuperEffective,
            [BunnyType.Ghost] = SuperEffective,
            [BunnyType.Dark] = NotVeryEffective,
        },
        [BunnyType.Draco] = new Dictionary<BunnyType, float>
        {
            [BunnyType.Ice] = NotVeryEffective,
            [BunnyType.Metal] = NotVeryEffective,
            [BunnyType.Pixie] = NotVeryEffective,
            [BunnyType.Draco] = SuperEffective,
        },
    };

    // Returns the damage multiplier for attacker's hit landing on defender. Unlisted matchups are Neutral.
    public static float GetMultiplier(BunnyType attacker, BunnyType defender)
    {
        if (Chart.TryGetValue(attacker, out var defenders) && defenders.TryGetValue(defender, out var multiplier))
        {
            return multiplier;
        }
        return Neutral;
    }
}
