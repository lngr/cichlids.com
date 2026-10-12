// Story: task-3.30
using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

public sealed class ResetPolicyTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("development")]
    [InlineData("STAGING")]
    public void AllowsDevelopmentAndStaging(string environmentName)
    {
        Assert.True(ResetPolicy.Allows(environmentName));
        ResetPolicy.EnsureAllowed(environmentName);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void RefusesEveryOtherEnvironment(string? environmentName)
    {
        Assert.False(ResetPolicy.Allows(environmentName));
        var error = Assert.Throws<ResetRefusedException>(() => ResetPolicy.EnsureAllowed(environmentName));
        Assert.Contains("refuses", error.Message);
    }
}
