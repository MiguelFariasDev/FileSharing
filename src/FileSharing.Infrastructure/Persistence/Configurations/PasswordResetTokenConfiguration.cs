using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileSharing.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);

        // SHA-256 hex digest, same shape as File.AccessTokenHash — see AccessTokenHasher.
        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        // Looking up a token by its hash (validate/reset) must be a direct equality lookup —
        // unique because a hash collision would otherwise let one token resolve to two users.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        // ForgotPasswordAsync invalidates a user's still-active tokens before issuing a new
        // one — this index is what keeps that lookup ("this user's tokens") fast.
        builder.HasIndex(t => t.UserId);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
