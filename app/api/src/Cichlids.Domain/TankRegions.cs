using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

public static class TankRegions
{
    public static TankRegion? FromLegacyCategory(TankCategory? category) => category switch
    {
        TankCategory.Malawi => TankRegion.LakeMalawi,
        TankCategory.Tanganyika => TankRegion.LakeTanganyika,
        TankCategory.African => TankRegion.AfricaOther,
        TankCategory.Community => TankRegion.Mixed,
        TankCategory.CentralAmerican => TankRegion.CentralAmerica,
        TankCategory.SouthAmerican => TankRegion.SouthAmerica,
        TankCategory.American => TankRegion.AmericasUnassigned,
        _ => null,
    };

    public static bool IsSelectable(TankRegion region) => Enum.IsDefined(region) && region != TankRegion.AmericasUnassigned;

    public static string Key(TankRegion region) => region switch
    {
        TankRegion.LakeMalawi => "lake_malawi",
        TankRegion.LakeTanganyika => "lake_tanganyika",
        TankRegion.LakeVictoria => "lake_victoria",
        TankRegion.AfricaOther => "africa_other",
        TankRegion.CentralAmerica => "central_america",
        TankRegion.SouthAmerica => "south_america",
        TankRegion.Mixed => "mixed",
        TankRegion.AmericasUnassigned => "americas_unassigned",
        _ => throw new ArgumentOutOfRangeException(nameof(region)),
    };

    public static bool IsInterest(string value) => Enum.GetValues<TankRegion>()
        .Any(region => IsSelectable(region) && region != TankRegion.Mixed && Key(region) == value);
}
