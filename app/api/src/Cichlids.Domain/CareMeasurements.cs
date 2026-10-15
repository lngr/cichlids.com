using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

/// <summary>
/// Describes how a water parameter is measured: the canonical unit its values are normalized to,
/// the units a value can be entered in (the first one is the default) and the plausible range of
/// the normalized value.
/// </summary>
public sealed record MeasurementRule(MeasurementUnit CanonicalUnit, IReadOnlyList<MeasurementUnit> InputUnits, decimal Minimum, decimal Maximum);

public static class CareMeasurements
{
    // Aquarium hobby convention: 1 German degree is 17.848 ppm as CaCO3, for general and carbonate hardness.
    // https://www.horiba.com/sgp/water-quality/applications/aquarium/aquarium-water-testing/
    public const decimal PartsPerMillionPerGermanDegree = 17.848m;

    private const int NormalizedScale = 6;

    private static readonly IReadOnlyDictionary<CareParameter, MeasurementRule> Rules = new Dictionary<CareParameter, MeasurementRule>
    {
        [CareParameter.Temperature] = new(MeasurementUnit.Celsius, [MeasurementUnit.Celsius, MeasurementUnit.Fahrenheit], 0m, 45m),
        [CareParameter.Ph] = new(MeasurementUnit.None, [MeasurementUnit.None], 0m, 14m),
        [CareParameter.Kh] = new(MeasurementUnit.Dkh, [MeasurementUnit.Dkh, MeasurementUnit.Ppm], 0m, 50m),
        [CareParameter.Gh] = new(MeasurementUnit.Dgh, [MeasurementUnit.Dgh, MeasurementUnit.Ppm], 0m, 100m),
        [CareParameter.No2] = new(MeasurementUnit.MgPerLiter, [MeasurementUnit.MgPerLiter], 0m, 100m),
        [CareParameter.No3] = new(MeasurementUnit.MgPerLiter, [MeasurementUnit.MgPerLiter], 0m, 1000m),
        [CareParameter.Nh4] = new(MeasurementUnit.MgPerLiter, [MeasurementUnit.MgPerLiter], 0m, 100m),
        [CareParameter.Po4] = new(MeasurementUnit.MgPerLiter, [MeasurementUnit.MgPerLiter], 0m, 100m),
        [CareParameter.Conductivity] = new(MeasurementUnit.MicrosiemensPerCm, [MeasurementUnit.MicrosiemensPerCm], 0m, 100000m),
        [CareParameter.Tds] = new(MeasurementUnit.Ppm, [MeasurementUnit.Ppm], 0m, 100000m),
    };

    public static MeasurementRule RuleFor(CareParameter parameter) => Rules[parameter];

    /// <summary>
    /// Converts a value entered in the given unit to the canonical unit of the parameter, rounded
    /// to six decimal places.
    /// </summary>
    public static decimal Normalize(CareParameter parameter, decimal value, MeasurementUnit unit)
    {
        var normalized = unit switch
        {
            MeasurementUnit.Fahrenheit => (value - 32m) * 5m / 9m,
            MeasurementUnit.Ppm when parameter is CareParameter.Kh or CareParameter.Gh => value / PartsPerMillionPerGermanDegree,
            _ => value,
        };
        return Math.Round(normalized, NormalizedScale, MidpointRounding.AwayFromZero);
    }

    public static bool IsPlausible(CareParameter parameter, decimal normalized) =>
        normalized >= Rules[parameter].Minimum && normalized <= Rules[parameter].Maximum;
}
