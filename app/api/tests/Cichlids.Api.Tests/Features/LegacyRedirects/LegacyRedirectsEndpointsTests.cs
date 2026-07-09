using System.Net;

namespace Cichlids.Api.Tests.Features.LegacyRedirects;

[Collection(ApiCollection.Name)]
public class LegacyRedirectsEndpointsTests(ApiFixture fixture)
{
    private static void AssertPermanentRedirectTo(HttpResponseMessage response, string expectedRelativeLocation)
    {
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(expectedRelativeLocation, response.Headers.Location!.OriginalString);
    }

    private static void AssertGone(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

    [Theory]
    [InlineData("/pictures/pic/{0}.html")]
    [InlineData("/pictures/pic/{0}")]
    public async Task Picture_KnownAliasRedirectsToCanonicalSlug(string routeFormat)
    {
        var path = string.Format(routeFormat, fixture.Seed.PublishedPictureAAltSlug);
        var response = await fixture.NoRedirectClient.GetAsync(path);

        AssertPermanentRedirectTo(response, $"/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}");
    }

    [Fact]
    public async Task Picture_CaseInsensitiveFallbackStillResolves()
    {
        var response = await fixture.NoRedirectClient.GetAsync($"/pictures/pic/{fixture.Seed.PublishedPictureAAltSlug.ToUpperInvariant()}");

        AssertPermanentRedirectTo(response, $"/pictures/{fixture.Seed.PublishedPictureACanonicalSlug}");
    }

    [Fact]
    public async Task Picture_UnknownAliasReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/pictures/pic/does-not-exist");

        AssertGone(response);
    }

    [Fact]
    public async Task Tank_KnownLegacyIdRedirectsToTankRoute()
    {
        var response = await fixture.NoRedirectClient.GetAsync($"/tanks/details/{fixture.Seed.PublishedTank.LegacyId}");

        AssertPermanentRedirectTo(response, $"/tanks/{fixture.Seed.PublishedTank.Id}");
    }

    [Fact]
    public async Task Tank_UnknownLegacyIdReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/tanks/details/999999999");

        AssertGone(response);
    }

    [Fact]
    public async Task Tank_NonNumericLegacyIdReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/tanks/details/not-a-number");

        AssertGone(response);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/profile")]
    [InlineData("/profile/edit")]
    public async Task Member_KnownLegacyUidRedirectsToMemberRoute(string trailingRest)
    {
        var response = await fixture.NoRedirectClient.GetAsync($"/members/{fixture.Seed.Alice.LegacyId}{trailingRest}");

        AssertPermanentRedirectTo(response, $"/members/{fixture.Seed.Alice.Id}");
    }

    [Fact]
    public async Task Member_NonMemberKindLegacyUidReturnsGone()
    {
        // The archived placeholder profile never had a public member page of its own, even if it
        // happened to carry a legacy uid.
        var response = await fixture.NoRedirectClient.GetAsync("/members/999999999");

        AssertGone(response);
    }

    [Fact]
    public async Task Species_KnownAliasRedirectsCaseInsensitively()
    {
        var response = await fixture.NoRedirectClient.GetAsync($"/browse/species/{fixture.Seed.Tropheus.Slug!.ToUpperInvariant()}.html");

        AssertPermanentRedirectTo(response, $"/species/{fixture.Seed.Tropheus.Slug}");
    }

    [Fact]
    public async Task Species_UnknownAliasReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/browse/species/does-not-exist.html");

        AssertGone(response);
    }

    [Fact]
    public async Task DiscussionRead_KnownThreadLegacyIdRedirectsToCommunityThread()
    {
        var response = await fixture.NoRedirectClient.GetAsync(
            $"/disc/read.php?1,{fixture.Seed.CommunityCichlidsThreadA.LegacyId}");

        AssertPermanentRedirectTo(response, $"/community/{fixture.Seed.CommunityCichlidsThreadA.Id}");
    }

    [Fact]
    public async Task DiscussionRead_WithPostAnchorStillResolvesByThreadOnly()
    {
        var response = await fixture.NoRedirectClient.GetAsync(
            $"/disc/read.php?1,{fixture.Seed.CommunityCichlidsThreadA.LegacyId},42");

        AssertPermanentRedirectTo(response, $"/community/{fixture.Seed.CommunityCichlidsThreadA.Id}");
    }

    [Fact]
    public async Task DiscussionRead_UnknownThreadLegacyIdReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/disc/read.php?1,999999999");

        AssertGone(response);
    }

    [Fact]
    public async Task DiscussionRead_MalformedQueryStringReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/disc/read.php?not-a-valid-query");

        AssertGone(response);
    }

    [Theory]
    [InlineData(1, "cichlids")]
    [InlineData(2, "african")]
    [InlineData(3, "market_place")]
    public async Task DiscussionList_KnownForumIdRedirectsToCommunityCategory(int forumId, string expectedCategory)
    {
        var response = await fixture.NoRedirectClient.GetAsync($"/disc/list.php?{forumId}");

        AssertPermanentRedirectTo(response, $"/community?category={expectedCategory}");
    }

    [Fact]
    public async Task DiscussionList_UnknownForumIdReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/disc/list.php?9");

        AssertGone(response);
    }

    [Fact]
    public async Task Wiki_ExactDisplayNameMatchRedirectsToSpecies()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/wiki/index.php/Tropheus_duboisi");

        AssertPermanentRedirectTo(response, $"/species/{fixture.Seed.Tropheus.Slug}");
    }

    [Fact]
    public async Task Wiki_GenusAndNameFallbackRedirectsToSpecies()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/wiki/index.php/Cyphotilapia_frontosa");

        AssertPermanentRedirectTo(response, $"/species/{fixture.Seed.CyphotilapiaFrontosa.Slug}");
    }

    [Fact]
    public async Task Wiki_SlugFallbackRedirectsToSpecies()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/wiki/index.php/Neolamprologus_brichardi_daffodil");

        AssertPermanentRedirectTo(response, $"/species/{fixture.Seed.NeolamprologusBrichardiDaffodil.Slug}");
    }

    [Fact]
    public async Task Wiki_UnknownNameReturnsGone()
    {
        var response = await fixture.NoRedirectClient.GetAsync("/wiki/index.php/Definitely_not_a_real_species");

        AssertGone(response);
    }
}
