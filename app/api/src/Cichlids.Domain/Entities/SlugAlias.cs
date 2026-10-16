namespace Cichlids.Domain.Entities;

/// <summary>
/// A public URL alias for a story. Every alias a story ever had stays resolvable; exactly one is
/// marked canonical at a time.
/// </summary>
public class SlugAlias
{
    public long Id { get; set; }
    public long StoryId { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsCanonical { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
