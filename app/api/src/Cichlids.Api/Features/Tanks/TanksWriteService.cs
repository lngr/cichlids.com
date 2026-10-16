using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Creates tanks with metric measurements, original inputs, the visibility answer of the owner and a
/// tank.created outbox event in one transaction. A tank is created only with an explicit answer to
/// the question whether it should be public: yes makes profile and stream public, no makes both
/// private, and either answer is stored with its time.
/// </summary>
public sealed class TanksWriteService(CichlidsDbContext context)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public const string AnswerRequiredCode = "tank_visibility_answer_required";

    public static (string Code, string Detail)? Validate(CreateTankRequest request)
    {
        if (request.IsPublic is null)
        {
            return (AnswerRequiredCode, "Answer whether your tank should be public.");
        }
        if (request.Region is { } region && !TankRegions.IsSelectable(region))
        {
            return ("tank_region_invalid", "Choose a region for a new tank.");
        }
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return ("tank_title_invalid", "A title of at most 200 characters is required.");
        }
        if (request.Dimensions is { } dimensions &&
            (!Enum.IsDefined(dimensions.Unit) ||
             !ValidMeasurement(dimensions.Width) || !ValidMeasurement(dimensions.Height) || !ValidMeasurement(dimensions.Depth) ||
             (dimensions.Width is null && dimensions.Height is null && dimensions.Depth is null)))
        {
            return ("tank_dimensions_invalid", "Dimensions require positive values of at most 1000000 and cm or inch.");
        }
        if (request.Volume is { } volume && (!Enum.IsDefined(volume.Unit) || !ValidMeasurement(volume.Value)))
        {
            return ("tank_volume_invalid", "Volume requires a positive value of at most 1000000 and liter or us_gallon.");
        }
        return null;
    }

    public async Task<Tank> CreateAsync(CreateTankRequest request, long profileId, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            throw new ArgumentException(error.Detail, nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var level = request.IsPublic == true ? TankVisibility.Public : TankVisibility.Private;
        var tank = new Tank
        {
            ProfileId = profileId,
            Region = request.Region,
            Title = request.Title.Trim(),
            State = TankState.Published,
            CreatedAt = now,
            PublishedAt = now,
            ProfileVisibility = level,
            StreamVisibility = level,
        };
        if (request.Dimensions is { } dimensions)
        {
            TankMeasurements.SetDimensions(tank, dimensions.Width, dimensions.Height, dimensions.Depth, dimensions.Unit);
        }
        if (request.Volume is { } volume)
        {
            TankMeasurements.SetVolume(tank, volume.Value, volume.Unit);
        }

        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        context.Tanks.Add(tank);
        await context.SaveChangesAsync(cancellationToken);
        context.TankVisibilityConsents.Add(new TankVisibilityConsent
        {
            TankId = tank.Id, Answer = request.IsPublic == true, AnsweredAt = now, Context = TankVisibilityConsent.CreationContext,
        });
        context.OutboxEvents.Add(OutboxEvent.Create(
            "tank.created", "tank", tank.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                tankId = tank.Id, profileId,
                profileVisibility = SnakeCaseEnum.ToSnakeCase(tank.ProfileVisibility),
                streamVisibility = SnakeCaseEnum.ToSnakeCase(tank.StreamVisibility),
            }, PayloadOptions), now));
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return tank;
    }

    public const string VisibilityInvalidCode = "tank_visibility_invalid";
    public const string StreamWiderCode = "tank_stream_wider_than_profile";
    public const string NotFoundCode = "tank_not_found";

    /// <summary>
    /// Sets the profile and stream levels of the owner's tank. The error is a problem code:
    /// tank_visibility_invalid for a missing or unknown level, tank_not_found when the tank is not the
    /// caller's, and tank_stream_wider_than_profile for a stream wider than the profile. A change of
    /// the profile level between public and not public is stored as a consent answer. A change of either
    /// level changes the visibility generation of the media items of the tank and marks them for placement
    /// in the same transaction, and every change commits with its tank.visibility_changed event.
    /// </summary>
    public async Task<(TankVisibilityDto? Visibility, string? Error)> SetVisibilityAsync(
        long tankId, long profileId, UpdateTankVisibilityRequest request, CancellationToken cancellationToken)
    {
        if (!SnakeCaseEnum.TryParse<TankVisibility>(request.Profile, out var profile)
            || !SnakeCaseEnum.TryParse<TankVisibility>(request.Stream, out var stream))
        {
            return (null, VisibilityInvalidCode);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (tank is null || tank.ProfileId != profileId || tank.DeletedAt is not null)
        {
            return (null, NotFoundCode);
        }

        if (!TankVisibilityRules.IsStreamWithinProfile(profile, stream))
        {
            return (null, StreamWiderCode);
        }

        if (tank.ProfileVisibility == profile && tank.StreamVisibility == stream)
        {
            return (new TankVisibilityDto(profile, stream), null);
        }

        var now = DateTimeOffset.UtcNow;
        var previous = new TankVisibilityDto(tank.ProfileVisibility, tank.StreamVisibility);
        if (tank.ProfileVisibility != profile)
        {
            context.TankVisibilityConsents.Add(new TankVisibilityConsent
            {
                TankId = tank.Id, Answer = profile == TankVisibility.Public, AnsweredAt = now,
                Context = TankVisibilityConsent.SettingsContext,
            });
        }

        tank.ProfileVisibility = profile;
        tank.StreamVisibility = stream;
        context.OutboxEvents.Add(OutboxEvent.Create(
            "tank.visibility_changed", "tank", tank.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                tankId = tank.Id, profileId,
                profileVisibility = SnakeCaseEnum.ToSnakeCase(profile),
                streamVisibility = SnakeCaseEnum.ToSnakeCase(stream),
                previousProfileVisibility = SnakeCaseEnum.ToSnakeCase(previous.Profile),
                previousStreamVisibility = SnakeCaseEnum.ToSnakeCase(previous.Stream),
            }, PayloadOptions), now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new TankVisibilityDto(profile, stream), null);
    }

    private static bool ValidMeasurement(decimal? value) => value is null || value is > 0 and <= 1000000m;
}
