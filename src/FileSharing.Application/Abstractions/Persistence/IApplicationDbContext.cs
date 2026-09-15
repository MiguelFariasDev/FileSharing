using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<File> Files { get; }
    DbSet<Download> Downloads { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
