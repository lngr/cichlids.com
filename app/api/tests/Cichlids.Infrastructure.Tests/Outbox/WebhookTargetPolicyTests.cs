using Cichlids.Infrastructure.Outbox;

namespace Cichlids.Infrastructure.Tests.Outbox;

public class WebhookTargetPolicyTests
{
    private static readonly WebhookTargetPolicy Policy = new(["example.test", "127.0.0.1", "xn--bcher-kva.example"]);

    [Theory]
    [InlineData("https://example.test/hook")]
    [InlineData("http://example.test:8080/hook")]
    [InlineData("http://127.0.0.1:5000/")]
    public void IsAllowed_AcceptsHttpAndHttpsOnAListedHost(string url)
    {
        Assert.True(Policy.IsAllowed(url, out _));
    }

    [Theory]
    [InlineData("ftp://example.test/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.test/")]
    public void IsAllowed_RejectsOtherSchemes(string url)
    {
        Assert.False(Policy.IsAllowed(url, out var reason));
        Assert.Contains("http", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/hook")]
    [InlineData("example.test/hook")]
    public void IsAllowed_RejectsMissingOrRelativeUrls(string? url)
    {
        Assert.False(Policy.IsAllowed(url, out _));
    }

    [Theory]
    [InlineData("https://user:pass@example.test/hook")]
    [InlineData("http://example.test@evil.test/hook")]
    public void IsAllowed_RejectsUserInfo(string url)
    {
        Assert.False(Policy.IsAllowed(url, out _));
    }

    [Theory]
    [InlineData("https://not-listed.test/hook")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("https://sub.example.test/hook")]
    [InlineData("https://example.test.evil.test/hook")]
    public void IsAllowed_RejectsHostsOutsideTheAllowlist(string url)
    {
        Assert.False(Policy.IsAllowed(url, out var reason));
        Assert.Contains("allowlist", reason);
    }

    [Fact]
    public void IsAllowed_MatchesHostsCaseInsensitively()
    {
        Assert.True(Policy.IsAllowed("https://EXAMPLE.Test/hook", out _));
        Assert.True(new WebhookTargetPolicy(["Example.TEST"]).IsAllowed("https://example.test/hook", out _));
    }

    [Fact]
    public void IsAllowed_ComparesInternationalizedHostsInTheirAsciiForm()
    {
        Assert.True(Policy.IsAllowed("https://bücher.example/hook", out _));
        Assert.True(new WebhookTargetPolicy(["bücher.example"]).IsAllowed("https://xn--bcher-kva.example/hook", out _));
    }

    [Fact]
    public void IsAllowed_MatchesIpv6AddressesInAnyNotation()
    {
        var policy = new WebhookTargetPolicy(["0:0:0:0:0:0:0:1"]);

        Assert.True(policy.IsAllowed("http://[::1]:8080/hook", out _));
    }

    [Fact]
    public void IsAllowed_EmptyAllowlistRejectsEverything()
    {
        var policy = new WebhookTargetPolicy([]);

        Assert.False(policy.IsAllowed("https://example.test/hook", out _));
        Assert.False(policy.IsAllowed("http://127.0.0.1/", out _));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("subscriber.cichlids.svc.cluster.local", true)]
    [InlineData("bücher.example", true)]
    [InlineData("https://x", false)]
    [InlineData("x:8080", false)]
    [InlineData("x/path", false)]
    [InlineData("*.example.test", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void IsBareHostName_AcceptsOnlyHostNamesAndAddresses(string entry, bool expected)
    {
        Assert.Equal(expected, WebhookTargetPolicy.IsBareHostName(entry));
    }
}
