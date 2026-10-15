using System.Text.Json;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Tanks;

/// <summary>
/// Creates tanks with metric measurements, original inputs and a tank.created outbox event in one transaction.
/// </summary>
public sealed class TanksWriteService(CichlidsDbContext context)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public static (string Code, string Detail)? Validate(CreateTankRequest request)
    {
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
        var tank = new Tank
        {
            ProfileId = profileId,
            Title = request.Title.Trim(),
            State = TankState.Published,
            CreatedAt = now,
            PublishedAt = now,
        };
        if (request.Dimensions is { } dimensions)
        {
            TankMeasurements.SetDimensions(tank, dimensions.Width, dimensions.Height, dimensions.Depth, dimensions.Unit);
        }
        if (request.Volume is { } volume)
        {
            TankMeasurements.SetVolume(tank, volume.Value, volume.Unit);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.Tanks.Add(tank);
        await context.SaveChangesAsync(cancellationToken);
        context.OutboxEvents.Add(OutboxEvent.Create(
            "tank.created", "tank", tank.Id.ToString(),
            JsonSerializer.Serialize(new { tankId = tank.Id, profileId }, PayloadOptions), now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tank;
    }

    private static bool ValidMeasurement(decimal? value) => value is null || value is > 0 and <= 1000000m;
}
