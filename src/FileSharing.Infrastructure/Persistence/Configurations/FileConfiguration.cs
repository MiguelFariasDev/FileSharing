using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.Infrastructure.Persistence.Configurations;

public class FileConfiguration : IEntityTypeConfiguration<File>
{
    public void Configure(EntityTypeBuilder<File> builder)
    {
        builder.ToTable("files");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.StorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(f => f.ContentType)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.SizeBytes)
            .IsRequired();

        builder.Property(f => f.IsFolder)
            .IsRequired();

        builder.Property(f => f.CompressionType)
            .IsRequired();

        // Nulo enquanto o upload está PendingUpload, e também enquanto Active sem nenhum link
        // gerado ainda — só passa a existir quando POST /api/files/{id}/link associar um
        // token de acesso (já hasheado; o token em texto puro nunca é persistido) a um
        // arquivo Active e não expirado.
        builder.Property(f => f.AccessTokenHash)
            .HasMaxLength(64);

        builder.HasIndex(f => f.AccessTokenHash)
            .IsUnique();

        builder.Property(f => f.Status)
            .IsRequired();

        // Nulos enquanto PendingUpload; definidos somente na conclusão do upload (CompleteUpload).
        builder.Property(f => f.CreatedAt);

        builder.Property(f => f.ExpiresAt);

        builder.HasIndex(f => f.ExpiresAt);

        builder.HasOne(f => f.User)
            .WithMany(u => u.Files)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
