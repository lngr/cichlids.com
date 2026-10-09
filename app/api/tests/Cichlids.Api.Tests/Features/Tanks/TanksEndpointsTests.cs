using System.Net;
using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public class TanksEndpointsTests(ApiFixture fixture)
{
    private async Task<PagedResponse<TankListItemDto>> ListAsync(string query = "")
    {
        var response = await fixture.Client.GetAsync($"/api/tanks{query}");
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<PagedResponse<TankListItemDto>>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    [Fact]
    public async Task List_ReturnsOnlyPublishedTanksNewestFirst()
    {
        var body = await ListAsync();

        Assert.Equal(2, body.Total);
        Assert.Equal(fixture.Seed.PublishedTank.Id, body.Items[0].Id);
        Assert.Equal(fixture.Seed.PublishedTankWithExplicitMainImage.Id, body.Items[1].Id);
        Assert.DoesNotContain(body.Items, t => t.Id == fixture.Seed.DraftTank.Id);
    }

    [Fact]
    public async Task List_FiltersByCategory()
    {
        var body = await ListAsync("?category=tanganyika");

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.PublishedTank.Id, body.Items[0].Id);
    }

    [Fact]
    public async Task List_FiltersByUser()
    {
        var body = await ListAsync($"?user={fixture.Seed.Alice.Id}");

        Assert.Equal(1, body.Total);
        Assert.Equal(fixture.Seed.PublishedTankWithExplicitMainImage.Id, body.Items[0].Id);
    }

    [Fact]
    public async Task List_UnknownCategoryReturnsBadRequest()
    {
        var response = await fixture.Client.GetAsync("/api/tanks?category=atlantis");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_MainImageFallsBackToFirstShowcasePhotoWhenNoMainMediaIsSet()
    {
        var body = await ListAsync();
        var tank = body.Items.Single(t => t.Id == fixture.Seed.PublishedTank.Id);

        Assert.NotNull(tank.MainImage);
        Assert.StartsWith(ApiFixture.ExpectedPublicUrlPrefix, tank.MainImage!.Thumb);
        Assert.Contains("showcase-1", tank.MainImage.Original);
        Assert.Equal(3, tank.ImageCount);
    }

    [Fact]
    public async Task List_MainImageUsesExplicitMainMediaIdWhenSet()
    {
        var body = await ListAsync();
        var tank = body.Items.Single(t => t.Id == fixture.Seed.PublishedTankWithExplicitMainImage.Id);

        Assert.NotNull(tank.MainImage);
        Assert.Contains("malawi-main", tank.MainImage!.Original);
        Assert.Equal(0, tank.ImageCount);
    }

    [Fact]
    public async Task GetById_ReturnsFullDetailWithSectionsWaterValuesDimensionsAndInhabitants()
    {
        var response = await fixture.Client.GetAsync($"/api/tanks/{fixture.Seed.PublishedTank.Id}");
        response.EnsureSuccessStatusCode();

        var detail = JsonSerializer.Deserialize<TankDetailDto>(await response.Content.ReadAsStringAsync(), TestJson.Options)!;

        Assert.Equal("Tanganyika 240L", detail.Title);
        Assert.Equal(TankCategory.Tanganyika, detail.Category);
        Assert.Equal("Rocks", detail.Decoration);
        Assert.Equal("8.2", detail.WaterValues.Ph);
        Assert.Equal("12", detail.WaterValues.Kh);
        Assert.Equal("Weekly 30% change", detail.WaterValues.Notes);
        Assert.NotNull(detail.Dimensions);
        Assert.Equal(120, detail.Dimensions!.Width);
        Assert.Equal(DimensionUnit.Cm, detail.Dimensions.Unit);

        Assert.Equal(2, detail.Sections.Showcase.Count);
        Assert.Single(detail.Sections.Decoration);
        Assert.Empty(detail.Sections.Technic);

        Assert.Equal(2, detail.Inhabitants.Count);
        Assert.Equal(fixture.Seed.Tropheus.Id, detail.Inhabitants[0].Species!.Id);
        Assert.Equal(6, detail.Inhabitants[0].Count);
        Assert.Equal(fixture.Seed.Neolamprologus.Id, detail.Inhabitants[1].Species!.Id);
        Assert.Equal(8, detail.Inhabitants[1].Count);

        Assert.Equal("bob", detail.Author.Username);
    }

    [Fact]
    public async Task GetById_DraftTankReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync($"/api/tanks/{fixture.Seed.DraftTank.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownIdReturnsNotFound()
    {
        var response = await fixture.Client.GetAsync("/api/tanks/999999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
