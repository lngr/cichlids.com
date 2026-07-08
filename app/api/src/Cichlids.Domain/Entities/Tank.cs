using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A tank owned by a profile: setup, water values and stocking, published as a page of its own.
/// </summary>
public class Tank
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long ProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TankCategory? Category { get; set; }
    public int? WidthValue { get; set; }
    public int? HeightValue { get; set; }
    public int? DepthValue { get; set; }
    public DimensionUnit? DimensionUnit { get; set; }
    public string? Gravel { get; set; }
    public string? Plants { get; set; }
    public string? Decoration { get; set; }
    public string? Light { get; set; }
    public string? LightDuration { get; set; }
    public string? Filtration { get; set; }
    public string? Technic { get; set; }
    public string? WaterPh { get; set; }
    public string? WaterKh { get; set; }
    public string? WaterGh { get; set; }
    public string? WaterNo2 { get; set; }
    public string? WaterNo3 { get; set; }
    public string? WaterPo4 { get; set; }
    public string? WaterNotes { get; set; }
    public string? Food { get; set; }
    public string? Notes { get; set; }
    public TankState State { get; set; } = TankState.Draft;
    public long? MainMediaId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
    public long? DeletedByProfileId { get; set; }

    public List<Inhabitant> Inhabitants { get; set; } = [];
    public List<TankMedia> Media { get; set; } = [];
}
