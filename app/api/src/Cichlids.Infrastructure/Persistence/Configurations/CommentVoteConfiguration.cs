using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class CommentVoteConfiguration : IEntityTypeConfiguration<CommentVote>
{
    public void Configure(EntityTypeBuilder<CommentVote> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.CommentId, x.ProfileId }).IsUnique();

        builder.HasOne<Comment>()
            .WithMany(x => x.Votes)
            .HasForeignKey(x => x.CommentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("comment_vote", tb => tb.HasCheckConstraint(
            "ck_comment_vote_value",
            "value IN (-1, 1)"));
    }
}
