// 5 status effects, each tied to exactly one type's signature attack — see Combat_DesignDoc.md's
// "Status effects" section for what each one actually does (durations/fractions/debuffs all live in
// CombatBalanceConfig, not here).
public enum StatusEffectType
{
    Burn,
    Poison,
    Chill,
    Paralyze,
    Sleep
}

public static class StatusEffectLink
{
    // Fire->Burn, Toxic->Poison, Ice->Chill, Shock->Paralyze, Mind->Sleep. Every other type has no
    // linked status (yet) — returns null, meaning that type's attack can never inflict a status.
    public static StatusEffectType? GetLinkedStatus(BunnyType attackerType)
    {
        switch (attackerType)
        {
            case BunnyType.Fire: return StatusEffectType.Burn;
            case BunnyType.Toxic: return StatusEffectType.Poison;
            case BunnyType.Ice: return StatusEffectType.Chill;
            case BunnyType.Shock: return StatusEffectType.Paralyze;
            case BunnyType.Mind: return StatusEffectType.Sleep;
            default: return null;
        }
    }
}
