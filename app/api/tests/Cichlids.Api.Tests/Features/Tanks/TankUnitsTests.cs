using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Tanks;

[Collection(ApiCollection.Name)]
public sealed class TankUnitsTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("inch", 48.125, 122.2375, "us_gallon", 55, 208.19764812)]
    [InlineData("cm", 48.125, 48.125, "liter", 55.125, 55.125)]
    public async Task CreatesMeasurementsAndReadsExactInputAlongsideMetricValues(
        string lengthUnit, decimal width, decimal centimeters, string volumeUnit, decimal inputVolume, decimal liters)
    {
        var title = $"Units {Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tanks")
        {
            Content = JsonContent.Create(new
            {
                title,
                dimensions = new { width, height = 20m, depth = 12m, unit = lengthUnit },
                volume = new { value = inputVolume, unit = volumeUnit },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(Guid.NewGuid().ToString(), $"units-{Guid.NewGuid():N}"[..24]));
        try
        {
            var response = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var id = created.RootElement.GetProperty("id").GetInt64();
            var read = await fixture.Client.GetAsync($"/api/tanks/{id}");
            read.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            var dimensions = json.RootElement.GetProperty("dimensions");
            Assert.Equal(centimeters, dimensions.GetProperty("width").GetDecimal());
            Assert.Equal("cm", dimensions.GetProperty("unit").GetString());
            Assert.Equal(width, dimensions.GetProperty("inputWidth").GetDecimal());
            Assert.Equal(lengthUnit, dimensions.GetProperty("inputUnit").GetString());
            var volume = json.RootElement.GetProperty("volume");
            Assert.Equal(liters, volume.GetProperty("liters").GetDecimal());
            Assert.Equal(inputVolume, volume.GetProperty("inputValue").GetDecimal());
            Assert.Equal(volumeUnit, volume.GetProperty("inputUnit").GetString());
        }
        finally
        {
            await using var db = fixture.CreateDbContext();
            await db.Tanks.Where(t => t.Title == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CreateRequiresAuthentication()
    {
        var response = await fixture.Client.PostAsJsonAsync("/api/tanks", new { title = "Units" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0, "us_gallon")]
    [InlineData(-1, "liter")]
    [InlineData(55, "imperial_gallon")]
    [InlineData(55, "")]
    public async Task RejectsInvalidMeasurements(decimal value, string unit)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tanks")
        {
            Content = JsonContent.Create(new { title = "Invalid units", volume = new { value, unit } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(Guid.NewGuid().ToString(), "units-invalid"));
        var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
