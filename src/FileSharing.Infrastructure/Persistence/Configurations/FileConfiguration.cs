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

        // Nulo enquanto o upload está PendingUpload — só passa a existir quando a Etapa 4
        // (link público) gerar e associar um token de acesso a um arquivo já Active.
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
