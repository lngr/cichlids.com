using System.ComponentModel.DataAnnotations;

namespace Cichlids.Infrastructure.Storage;

/// <summary>
/// Configuration for the S3 object store implementation, bound from the "ObjectStorage"
/// configuration section. Every field is plain provider configuration; none of it leaks
/// provider-specific behavior into the object store abstraction.
/// </summary>
public sealed class S3ObjectStoreOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// Endpoint of the S3-compatible service, for example "http://localhost:9000".
    /// </summary>
    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Region passed to the S3 client for request signing. S3-compatible providers that do not
    /// have real regions still require a non-empty value here.
    /// </summary>
    [Required]
    public string Region { get; set; } = string.Empty;

    [Required]
    public string Bucket { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether to address the bucket as part of the URL path (http://host/bucket/key) instead of
    /// as a subdomain (http://bucket.host/key). Path-style addressing is what self-hosted
    /// S3-compatible stores without wildcard DNS require.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Base URL under which stored objects are publicly reachable, for example through a CDN in
    /// front of the bucket. The public URL for an object appends its key to this value; it is
    /// not presigned and does not query the provider.
    /// </summary>
    [Required]
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional page size for list requests. Left unset, the provider's own default applies.
    /// Only relevant for tuning or for exercising pagination in tests.
    /// </summary>
    public int? ListPageSize { get; set; }
}
