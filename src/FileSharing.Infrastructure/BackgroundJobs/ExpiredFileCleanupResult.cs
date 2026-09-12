namespace FileSharing.Infrastructure.BackgroundJobs;

/// <summary>
/// Summary of one <see cref="IExpiredFileCleanupJob"/> execution, returned so both logging
/// and tests can assert on outcome counts without re-querying the database.
/// </summary>
public record ExpiredFileCleanupResult(int CandidatesFound, int Expired, int Failed);
