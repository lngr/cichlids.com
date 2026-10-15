using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Domain;

public static class TankMeasurements
{
    // NIST SP 811 Appendix B: 1 inch = 2.54 cm exactly; a US liquid gallon is
    // 231 cubic inches, hence exactly 3.785411784 liters.
    // https://www.nist.gov/pml/special-publication-811/nist-guide-si-appendix-b-conversion-factors
    public const decimal CentimetersPerInch = 2.54m;
    public const decimal LitersPerUsGallon = 3.785411784m;

    public static void SetDimensions(Tank tank, decimal? width, decimal? height, decimal? depth, DimensionUnit unit)
    {
        var factor = unit == DimensionUnit.Inch ? CentimetersPerInch : 1m;
        tank.WidthInputValue = width;
        tank.HeightInputValue = height;
        tank.DepthInputValue = depth;
        tank.DimensionUnit = unit;
        tank.WidthValue = width * factor;
        tank.HeightValue = height * factor;
        tank.DepthValue = depth * factor;
    }

    public static void SetVolume(Tank tank, decimal value, VolumeUnit unit)
    {
        tank.VolumeInputValue = value;
        tank.VolumeInputUnit = unit;
        tank.VolumeLiters = unit == VolumeUnit.UsGallon ? value * LitersPerUsGallon : value;
    }
}
