using FileSharing.Mobile.Core.Models;
using FileSharing.Mobile.Core.Services.Upload;

namespace FileSharing.Mobile.Tests.Fakes;

public class FakeFilePickerService : IFilePickerService
{
    public UploadableItem? ItemToReturn { get; set; }

    public Task<UploadableItem?> PickFileAsync(CancellationToken cancellationToken = default) => Task.FromResult(ItemToReturn);
}

public class FakeFolderPickerService : IFolderPickerService
{
    public UploadableItem? ItemToReturn { get; set; }

    public Task<UploadableItem?> PickFolderAndZipAsync(IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        Task.FromResult(ItemToReturn);
}

public class FakeFileUploadService : IFileUploadService
{
    public UploadOutcome OutcomeToReturn { get; set; } = UploadOutcome.Failure(UploadFailureReason.Unknown, "not configured");
    public List<UploadProgressUpdate> ReportedProgress { get; } = [];
    public UploadableItem? LastItem { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>When set, UploadAsync blocks here instead of returning immediately — lets a
    /// test simulate an in-flight upload long enough to cancel it mid-flight. Cancelling the
    /// token unblocks it on its own (via the registration below), the same way a real
    /// FileUploadService's own await points would naturally observe cancellation.</summary>
    public TaskCompletionSource<bool>? PauseBeforeReturning { get; set; }

    public async Task<UploadOutcome> UploadAsync(UploadableItem item, IProgress<UploadProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        LastItem = item;
        LastCancellationToken = cancellationToken;

        if (progress is not null)
        {
            foreach (var update in ReportedProgress)
                progress.Report(update);
        }

        if (PauseBeforeReturning is not null)
        {
            using var registration = cancellationToken.Register(() => PauseBeforeReturning.TrySetResult(true));
            await PauseBeforeReturning.Task;
        }

        // Mirrors the real FileUploadService's contract exactly: cancellation is reported as a
        // Cancelled outcome, never as a thrown OperationCanceledException.
        return cancellationToken.IsCancellationRequested
            ? UploadOutcome.Failure(UploadFailureReason.Cancelled, "Envio cancelado.")
            : OutcomeToReturn;
    }
}
