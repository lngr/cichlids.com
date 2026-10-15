using System.Collections.ObjectModel;

namespace Cichlids.Api.Features.FeatureFlags;

public sealed record FeatureFlagSettings(IReadOnlyDictionary<string, bool> Values)
{
    public static FeatureFlagSettings Resolve(IConfiguration configuration)
    {
        var values = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["discover-activity"] = false,
        };
        foreach (var setting in configuration.GetSection("FeatureFlags").GetChildren())
        {
            if (!values.ContainsKey(setting.Key))
                throw new InvalidOperationException($"Unknown FeatureFlags name: {setting.Key}.");
            if (setting.GetChildren().Any() || !bool.TryParse(setting.Value, out var enabled))
                throw new InvalidOperationException($"FeatureFlags:{setting.Key} must be a boolean.");
            values[setting.Key] = enabled;
        }
        return new FeatureFlagSettings(new ReadOnlyDictionary<string, bool>(values));
    }
}
