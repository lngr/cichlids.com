namespace Cichlids.Domain.Entities;

/// <summary>
/// A public URL alias for a post. Every alias a post ever had stays resolvable; exactly one is
/// marked canonical at a time.
/// </summary>
public class SlugAlias
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsCanonical { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
