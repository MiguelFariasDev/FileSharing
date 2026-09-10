using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileSharing.Infrastructure.Persistence.Configurations;

public class DownloadConfiguration : IEntityTypeConfiguration<Download>
{
    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.ToTable("downloads");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.IpAddress)
            .IsRequired()
            .HasMaxLength(45);

        builder.Property(d => d.UserAgent)
            .IsRequired()
            .HasMaxLength(Download.MaxUserAgentLength);

        builder.Property(d => d.DownloadedAt)
            .IsRequired();

        builder.HasIndex(d => d.FileId);

        builder.HasIndex(d => d.DownloadedAt);

        builder.HasOne(d => d.File)
            .WithMany(f => f.Downloads)
            .HasForeignKey(d => d.FileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
