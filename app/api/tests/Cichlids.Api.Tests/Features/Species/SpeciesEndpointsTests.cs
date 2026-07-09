using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Species;

namespace Cichlids.Api.Tests.Features.Species;

[Collection(ApiCollection.Name)]
public class SpeciesEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task List_OrdersByGenusThenName()
    {
        var response = await fixture.Client.GetAsync("/api/species");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<SpeciesListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(2, body.Total);
        Assert.Equal("Neolamprologus", body.Items[0].Genus);
        Assert.Equal("Tropheus", body.Items[1].Genus);
    }

    [Fact]
    public async Task List_FiltersByQueryAcrossGenusNameAndDisplayName()
    {
        var response = await fixture.Client.GetAsync("/api/species?query=duboisi");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<SpeciesListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(1, body.Total);
        Assert.Equal("tropheus-duboisi", body.Items[0].Slug);
    }

    [Fact]
    public async Task List_QueryIsCaseInsensitive()
    {
        var response = await fixture.Client.GetAsync("/api/species?query=TROPHEUS");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<SpeciesListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(1, body.Total);
    }

    [Fact]
    public async Task List_CapsLimit()
    {
        var response = await fixture.Client.GetAsync("/api/species?limit=1");
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<PagedResponse<SpeciesListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(2, body.Total);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task GetBySlug_ReturnsFullCareData()
    {
        var response = await fixture.Client.GetAsync("/api/species/tropheus-duboisi");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<SpeciesDetailDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal(fixture.Seed.Tropheus.Id, detail.Id);
        Assert.Equal("Tropheus", detail.Genus);
        Assert.Equal("duboisi", detail.Name);
        Assert.Equal("Tropheus duboisi", detail.DisplayName);
        Assert.Equal("24-27C", detail.TemperatureRange);
        Assert.Equal(Domain.Enums.SpeciesBreeding.Mouthbreeder, detail.Breeding);
        Assert.Equal(Domain.Enums.AggressionLevel.Moderate, detail.Aggression);
        Assert.Equal(Domain.Enums.AggressionLevel.High, detail.IntraAggression);
        Assert.Equal(Domain.Enums.SpeciesDiet.Herbivore, detail.Diet);
        Assert.Contains("Duboisi cichlid", detail.CommonNames);
        Assert.Contains("White spotted cichlid", detail.CommonNames);
        Assert.Single(detail.Links);
        Assert.Equal("https://example.test/tropheus", detail.Links[0].Url);
        Assert.Equal("Lake Tanganyika", detail.Origin);
        Assert.Equal("Rocky shore", detail.Habitat);
        Assert.Equal("Maswa, Bemba", detail.Morphs);
    }

    [Fact]
    public async Task GetById_ResolvesTheSameSpeciesAsGetBySlug()
    {
        var response = await fixture.Client.GetAsync($"/api/species/{fixture.Seed.Tropheus.Id}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<SpeciesDetailDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("tropheus-duboisi", detail.Slug);
    }

    [Fact]
    public async Task GetByUnknownIdOrSlug_ReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/species/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
