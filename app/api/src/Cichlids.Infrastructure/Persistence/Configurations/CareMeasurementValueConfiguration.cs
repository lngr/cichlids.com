using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CareMeasurementValueConfiguration : IEntityTypeConfiguration<CareMeasurementValue>
{
    public void Configure(EntityTypeBuilder<CareMeasurementValue> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Parameter).HasConversion<SnakeCaseEnumConverter<CareParameter>>();
        builder.Property(x => x.InputUnit).HasConversion<SnakeCaseEnumConverter<MeasurementUnit>>();
        builder.Property(x => x.Origin).HasConversion<SnakeCaseEnumConverter<MeasurementOrigin>>();
        builder.HasIndex(x => new { x.CareEntryId, x.Parameter }).IsUnique();
        builder.ToTable("care_measurement_value", table =>
        {
            table.HasCheckConstraint("ck_care_measurement_origin", "origin IS NULL OR origin IN ('test_kit', 'meter', 'probe')");
            table.HasCheckConstraint("ck_care_measurement_unit", """
                (parameter = 'temperature' AND input_unit IN ('celsius', 'fahrenheit'))
                OR (parameter = 'ph' AND input_unit = 'none')
                OR (parameter = 'kh' AND input_unit IN ('dkh', 'ppm'))
                OR (parameter = 'gh' AND input_unit IN ('dgh', 'ppm'))
                OR (parameter IN ('no2', 'no3', 'nh4', 'po4') AND input_unit = 'mg_per_liter')
                OR (parameter = 'conductivity' AND input_unit = 'microsiemens_per_cm')
                OR (parameter = 'tds' AND input_unit = 'ppm')
                """);
            table.HasCheckConstraint("ck_care_measurement_range", """
                (parameter = 'temperature' AND normalized_value BETWEEN 0 AND 45)
                OR (parameter = 'ph' AND normalized_value BETWEEN 0 AND 14)
                OR (parameter = 'kh' AND normalized_value BETWEEN 0 AND 50)
                OR (parameter = 'gh' AND normalized_value BETWEEN 0 AND 100)
                OR (parameter IN ('no2', 'nh4', 'po4') AND normalized_value BETWEEN 0 AND 100)
                OR (parameter = 'no3' AND normalized_value BETWEEN 0 AND 1000)
                OR (parameter IN ('conductivity', 'tds') AND normalized_value BETWEEN 0 AND 100000)
                """);
        });
    }
}
