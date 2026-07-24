// The Zodiac "Nature" layer of the Bunny Stat System Redesign (see design doc). Every bunny is
// assigned exactly one of these 12 signs at spawn (random, uniform — see NPCBunny.RollIndividuality).
// There is no neutral/13th sign; a bunny with the Stoic trait still has a real sign underneath, it's
// just suppressed at the calculation level (see NPCBunny.ApplyNatureEffects).
public enum BunnyNature
{
    Aries, Taurus, Gemini, Cancer, Leo, Virgo, Libra, Scorpio, Sagittarius, Capricorn, Aquarius, Pisces
}

// The four stats Nature can affect. HP is deliberately excluded — Nature never touches it.
public enum NatureStat
{
    Attack, Defense, Speed, Luck
}

// Static lookup from sign to its (boosted, lowered) stat pair. Balanced so each NatureStat appears
// exactly 3 times as a boost and 3 times as a penalty across all 12 signs.
public static class BunnyNatureData
{
    public static void GetModifiers(BunnyNature nature, out NatureStat boosted, out NatureStat lowered)
    {
        switch (nature)
        {
            case BunnyNature.Aries: boosted = NatureStat.Attack; lowered = NatureStat.Defense; break;
            case BunnyNature.Taurus: boosted = NatureStat.Attack; lowered = NatureStat.Speed; break;
            case BunnyNature.Scorpio: boosted = NatureStat.Attack; lowered = NatureStat.Luck; break;
            case BunnyNature.Cancer: boosted = NatureStat.Defense; lowered = NatureStat.Attack; break;
            case BunnyNature.Capricorn: boosted = NatureStat.Defense; lowered = NatureStat.Speed; break;
            case BunnyNature.Virgo: boosted = NatureStat.Defense; lowered = NatureStat.Luck; break;
            case BunnyNature.Gemini: boosted = NatureStat.Speed; lowered = NatureStat.Attack; break;
            case BunnyNature.Sagittarius: boosted = NatureStat.Speed; lowered = NatureStat.Defense; break;
            case BunnyNature.Aquarius: boosted = NatureStat.Speed; lowered = NatureStat.Luck; break;
            case BunnyNature.Libra: boosted = NatureStat.Luck; lowered = NatureStat.Attack; break;
            case BunnyNature.Leo: boosted = NatureStat.Luck; lowered = NatureStat.Defense; break;
            case BunnyNature.Pisces: boosted = NatureStat.Luck; lowered = NatureStat.Speed; break;
            default:
                // Unreachable — every BunnyNature value is handled above.
                boosted = NatureStat.Attack;
                lowered = NatureStat.Attack;
                break;
        }
    }
}
