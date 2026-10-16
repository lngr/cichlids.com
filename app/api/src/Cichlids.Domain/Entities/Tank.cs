using Cichlids.Domain.Enums;

namespace Cichlids.Domain.Entities;

/// <summary>
/// A tank owned by a profile: setup, water values and stocking, published as a page of its own.
/// CareRemindersEnabled tells whether the member's devices remind them of the care due for this tank.
/// ProfileVisibility is the audience of the tank itself and StreamVisibility the audience of its care
/// stream, never wider than the profile. MediaPlacedPublic tells whether the stored media of the tank
/// were last placed under the public storage prefixes; MediaMovePending marks a tank whose media are
/// to be placed to match its profile level.
/// </summary>
public class Tank
{
    public long Id { get; set; }
    public int? LegacyId { get; set; }
    public long ProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TankRegion? Region { get; set; }
    public DateTimeOffset? AdoptedAt { get; set; }
    public TankCategory? Category { get; set; }
    public decimal? WidthValue { get; set; }
    public decimal? HeightValue { get; set; }
    public decimal? DepthValue { get; set; }
    public DimensionUnit? DimensionUnit { get; set; }
    public decimal? WidthInputValue { get; set; }
    public decimal? HeightInputValue { get; set; }
    public decimal? DepthInputValue { get; set; }
    public decimal? VolumeLiters { get; set; }
    public decimal? VolumeInputValue { get; set; }
    public VolumeUnit? VolumeInputUnit { get; set; }
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
    public bool CareRemindersEnabled { get; set; } = true;
    public TankVisibility ProfileVisibility { get; set; } = TankVisibility.Public;
    public TankVisibility StreamVisibility { get; set; } = TankVisibility.Public;
    public bool MediaPlacedPublic { get; set; } = true;
    public bool MediaMovePending { get; set; }

    /// <summary>
    /// The earliest time the media placement of the tank is attempted next. While a worker holds the
    /// tank, this is the end of its lease.
    /// </summary>
    public DateTimeOffset? MediaMoveNextAttemptAt { get; set; }

    /// <summary>
    /// Failed placement attempts since the last successful one.
    /// </summary>
    public int MediaMoveAttempts { get; set; }
    public long? MainMediaId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeleteReason { get; set; }
    public long? DeletedByProfileId { get; set; }

    public List<Inhabitant> Inhabitants { get; set; } = [];
    public List<TankMedia> Media { get; set; } = [];
}
