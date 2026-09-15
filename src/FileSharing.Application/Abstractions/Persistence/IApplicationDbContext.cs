using FileSharing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using File = FileSharing.Domain.Entities.File;

namespace FileSharing.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<File> Files { get; }
    DbSet<Download> Downloads { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    // Já no mesmo nível de compromisso que os DbSet<T> acima (esta interface já não é 100%
    // livre de EF Core) — exposto especificamente para ExpiredFileCleanupJob poder desanexar uma
    // entidade já salva entre iterações de um lote grande (Etapa 16, medido em
    // docs/performance.md: sem isto, o change tracker cresce a cada iteração e o custo por item
    // sobe com o tamanho do lote, já que SaveChangesAsync roda DetectChanges() sobre todas as
    // entidades já rastreadas no laço).
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
