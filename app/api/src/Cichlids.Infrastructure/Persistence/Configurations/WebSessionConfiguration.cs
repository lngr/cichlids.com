using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class WebSessionConfiguration : IEntityTypeConfiguration<WebSession>
{
    public void Configure(EntityTypeBuilder<WebSession> builder)
    {
        builder.ToTable("web_session");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SessionIdHash).IsRequired();
        builder.Property(x => x.Subject).IsRequired();
        builder.Property(x => x.EncryptedAccessToken).IsRequired();
        builder.Property(x => x.AccessTokenExpiresAt).IsRequired();
        builder.Property(x => x.EncryptedRefreshToken).IsRequired();
        builder.Property(x => x.CsrfToken).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastSeenAt).IsRequired();

        builder.HasIndex(x => x.SessionIdHash).IsUnique();
        builder.HasIndex(x => x.Subject);
    }
}
