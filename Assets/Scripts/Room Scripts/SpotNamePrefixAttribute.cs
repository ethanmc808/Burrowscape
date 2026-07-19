using System;

// Marks a `List<RoomSpot>` field with the child-GameObject name prefix the Editor-only auto-populate
// tool (RoomSpotPathAutoPopulator, Assets/Scripts/Editor/) should use to find its members — e.g.
// `farmingSpots` is populated from children named "FarmingSpot_01", "FarmingSpot_02", etc. Explicit
// rather than derived from the field name because that guess isn't always right (WaterRoom's
// `productionSpots` field is populated by children named "PottingSpot_XX", not "ProductionSpot_XX"),
// and WaterRoom has two separate spot lists that need disambiguating by name anyway.
[AttributeUsage(AttributeTargets.Field)]
public class SpotNamePrefixAttribute : Attribute
{
    public readonly string Prefix;
    public SpotNamePrefixAttribute(string prefix)
    {
        Prefix = prefix;
    }
}
