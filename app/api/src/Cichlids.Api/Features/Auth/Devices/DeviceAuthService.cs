using System.Security.Cryptography;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cichlids.Api.Features.Auth.Devices;

public enum DeviceRegistrationOutcome
{
    Created,
    AlreadyRegistered,
    InvalidKey,
    KeyRetired,
}

public sealed record DeviceRegistration(DeviceRegistrationOutcome Outcome, DeviceCredentialDto? Credential);

/// <summary>
/// Registers the public keys of app installations and exchanges signed challenges for device
/// tokens. A registration creates the provisional profile with a generated name and the device
/// credential of the key in one save. A challenge is 32 random bytes of which only the hash is
/// stored; once the shape of the request and its timestamp pass, the exchange deletes the challenge
/// in a single statement, so a challenge is used once on any API instance, and the credential and
/// the signature follow. Every refusal of an exchange looks the same to the caller.
/// </summary>
public sealed class DeviceAuthService(
    CichlidsDbContext context,
    DeviceTokenIssuer issuer,
    Lazy<GeneratedNames> generatedNames,
    IOptions<DeviceAuthOptions> options,
    TimeProvider clock,
    ILogger<DeviceAuthService> logger)
{
    public const string Algorithm = "ES256";

    private const int ChallengeBytes = 32;
    private const int MaxPublicKeyBytes = 200;
    private const int SignatureBytes = 64;
    private const int MaxCreateAttempts = 3;
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Registers the public key. A key that is already registered and active returns its existing
    /// credential, so an installation that lost the answer can send the registration twice without getting a second
    /// profile. A retired key is refused.
    /// </summary>
    public async Task<DeviceRegistration> RegisterAsync(string? publicKey, CancellationToken cancellationToken)
    {
        if (!TryReadP256PublicKey(publicKey, out var spki))
        {
            return new DeviceRegistration(DeviceRegistrationOutcome.InvalidKey, null);
        }

        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            if (await FindByKeyAsync(spki, cancellationToken) is { } existing)
            {
                return existing;
            }

            var credentialId = Guid.NewGuid();
            var now = clock.GetUtcNow();
            var displayName = await FirstFreeNameAsync(credentialId, cancellationToken);
            var profile = new Profile
            {
                Username = $"provisional-{credentialId:N}",
                DisplayName = displayName,
                Kind = ProfileKind.Provisional,
                CreatedAt = now,
            };
            var credential = new DeviceCredential
            {
                Id = credentialId,
                PublicKey = spki,
                Algorithm = Algorithm,
                State = DeviceCredentialState.Active,
                CreatedAt = now,
                LastSignedRequestAt = now,
            };
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                context.Profiles.Add(profile);
                await context.SaveChangesAsync(cancellationToken);
                credential.ProfileId = profile.Id;
                context.DeviceCredentials.Add(credential);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new DeviceRegistration(
                    DeviceRegistrationOutcome.Created, new DeviceCredentialDto(credentialId, profile.Id, displayName));
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A parallel registration of the same key or name won; the next pass finds its credential or picks another name.
                context.ChangeTracker.Clear();
            }
        }

        return await FindByKeyAsync(spki, cancellationToken)
            ?? throw new InvalidOperationException($"The device key could not be registered after {MaxCreateAttempts} attempts.");
    }

    /// <summary>Issues a challenge and removes the challenges that have expired.</summary>
    public async Task<DeviceChallengeDto> IssueChallengeAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var challenge = RandomNumberGenerator.GetBytes(ChallengeBytes);
        var expiresAt = now + options.Value.ChallengeLifetime;

        await context.DeviceChallenges.Where(c => c.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
        context.DeviceChallenges.Add(new DeviceChallenge
        {
            ChallengeHash = SHA256.HashData(challenge),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await context.SaveChangesAsync(cancellationToken);

        return new DeviceChallengeDto(WebEncoders.Base64UrlEncode(challenge), expiresAt);
    }

    /// <summary>
    /// Exchanges a signed challenge for a device token, or returns null when the exchange is
    /// refused. A successful exchange records the time as the last signed request of the credential.
    /// </summary>
    public async Task<IssuedDeviceToken?> ExchangeAsync(DeviceTokenRequest request, CancellationToken cancellationToken)
    {
        if (request.Challenge is not { Length: > 0 } challenge || request.CredentialId is not { } credentialId
            || request.Timestamp is not { } timestamp || request.Signature is not { Length: > 0 } signatureText
            || !TryDecodeCanonical(challenge, out var challengeBytes) || challengeBytes.Length != ChallengeBytes
            || !TryDecode(signatureText, out var signature) || signature.Length != SignatureBytes)
        {
            return Refuse("malformed");
        }

        var now = clock.GetUtcNow();
        if (Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(timestamp, 0, 253_402_300_799))).TotalSeconds)
            > options.Value.TimestampWindow.TotalSeconds)
        {
            return Refuse("timestamp");
        }

        // Deleting the row is the use of the challenge: of parallel requests with the same
        // challenge, exactly one deletes a row.
        var hash = SHA256.HashData(challengeBytes);
        var consumed = await context.DeviceChallenges
            .Where(c => c.ChallengeHash == hash && c.ExpiresAt > now)
            .ExecuteDeleteAsync(cancellationToken);
        if (consumed != 1)
        {
            return Refuse("challenge");
        }

        var credential = await context.DeviceCredentials.AsNoTracking()
            .Where(c => c.Id == credentialId && c.State == DeviceCredentialState.Active
                && context.Profiles.Any(p => p.Id == c.ProfileId && p.Kind == ProfileKind.Provisional))
            .Select(c => new { c.ProfileId, c.PublicKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (credential is null)
        {
            return Refuse("credential");
        }

        var message = DeviceSignatureMessage.Build(challenge, credentialId, timestamp);
        if (!Verify(credential.PublicKey, message, signature))
        {
            return Refuse("signature");
        }

        // A credential removed together with its abandoned profile since the check above updates no row.
        var recorded = await context.DeviceCredentials.Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSignedRequestAt, now), cancellationToken);
        if (recorded != 1)
        {
            return Refuse("credential");
        }

        return issuer.Issue(credentialId, credential.ProfileId, now);
    }

    private IssuedDeviceToken? Refuse(string reason)
    {
        logger.LogInformation("A device token request was refused: {Reason}.", reason);
        return null;
    }

    private static bool Verify(byte[] spki, byte[] message, byte[] signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out _);
            return key.VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private async Task<DeviceRegistration?> FindByKeyAsync(byte[] spki, CancellationToken cancellationToken)
    {
        var found = await context.DeviceCredentials.AsNoTracking()
            .Where(c => c.PublicKey == spki)
            .Join(context.Profiles, c => c.ProfileId, p => p.Id, (c, p) => new { c.Id, c.State, p.DisplayName, ProfileId = p.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        return found.State == DeviceCredentialState.Active
            ? new DeviceRegistration(
                DeviceRegistrationOutcome.AlreadyRegistered, new DeviceCredentialDto(found.Id, found.ProfileId, found.DisplayName ?? string.Empty))
            : new DeviceRegistration(DeviceRegistrationOutcome.KeyRetired, null);
    }

    private async Task<string> FirstFreeNameAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        foreach (var candidate in generatedNames.Value.Candidates(GeneratedNames.ProvisionalInput(credentialId.ToString("N"))))
        {
            if (!await context.Profiles.AnyAsync(p => p.Username == candidate || p.DisplayName == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The candidate sequence ended without a free name.");
    }

    /// <summary>
    /// Reads a base64url SubjectPublicKeyInfo and accepts it only when it is the canonical encoding
    /// of a point on the curve P-256.
    /// </summary>
    private static bool TryReadP256PublicKey(string? text, out byte[] spki)
    {
        spki = [];
        if (string.IsNullOrEmpty(text) || text.Length > MaxPublicKeyBytes * 2 || !TryDecode(text, out var bytes)
            || bytes.Length is 0 or > MaxPublicKeyBytes)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length || key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value != P256Oid
                || !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(bytes))
            {
                return false;
            }

            spki = bytes;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDecode(string text, out byte[] bytes)
    {
        try
        {
            bytes = WebEncoders.Base64UrlDecode(text);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>Decodes base64url and accepts only the exact text that encoding the bytes yields.</summary>
    private static bool TryDecodeCanonical(string text, out byte[] bytes) =>
        TryDecode(text, out bytes) && string.Equals(WebEncoders.Base64UrlEncode(bytes), text, StringComparison.Ordinal);
}
