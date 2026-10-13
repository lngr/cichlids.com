// Story: TASK-3.37
using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Cichlids.Api.Tests.Features.Common;

/// <summary>
/// The request size limit for JSON bodies: a declared length above the limit is refused, and
/// the server's body size limit is lowered for JSON requests only, so uploads keep their own
/// limit.
/// </summary>
public class JsonRequestSizeLimitTests
{
    private sealed class FakeMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    private static (DefaultHttpContext Context, FakeMaxRequestBodySizeFeature Feature) NewContext(string contentType, long? contentLength)
    {
        var context = new DefaultHttpContext();
        var feature = new FakeMaxRequestBodySizeFeature();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        context.Request.ContentType = contentType;
        context.Request.ContentLength = contentLength;
        context.Response.Body = new MemoryStream();
        return (context, feature);
    }

    [Fact]
    public async Task JsonRequestWithoutDeclaredLengthGetsTheLimitOnTheServerFeature()
    {
        var (context, feature) = NewContext("application/json", contentLength: null);
        var called = false;

        await JsonRequestSizeLimit.InvokeAsync(context, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
        Assert.Equal(JsonRequestSizeLimit.MaxBytes, feature.MaxRequestBodySize);
        Assert.Equal(64 * 1024, JsonRequestSizeLimit.MaxBytes);
    }

    [Fact]
    public async Task JsonRequestAboveTheLimitIsRefusedWithoutReachingTheEndpoint()
    {
        var (context, _) = NewContext("application/json; charset=utf-8", contentLength: JsonRequestSizeLimit.MaxBytes + 1);
        var called = false;

        await JsonRequestSizeLimit.InvokeAsync(context, _ => { called = true; return Task.CompletedTask; });

        Assert.False(called);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task JsonRequestAtTheLimitPasses()
    {
        var (context, _) = NewContext("application/json", contentLength: JsonRequestSizeLimit.MaxBytes);
        var called = false;

        await JsonRequestSizeLimit.InvokeAsync(context, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    [Fact]
    public async Task MultipartRequestKeepsItsOwnLimit()
    {
        var (context, feature) = NewContext("multipart/form-data; boundary=x", contentLength: 20_000_000);
        var called = false;

        await JsonRequestSizeLimit.InvokeAsync(context, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
        Assert.Equal(30_000_000, feature.MaxRequestBodySize);
    }
}
