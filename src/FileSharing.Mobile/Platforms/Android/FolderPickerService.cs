using System.IO.Compression;
using Android.App;
using Android.Content;
using Android.Provider;
using FileSharing.Mobile.Models;
using FileSharing.Mobile.Services.Upload;
using AndroidUri = Android.Net.Uri;

namespace FileSharing.Mobile.Platforms.Android;

/// <summary>
/// Android's Storage Access Framework has no concept of a plain filesystem path for a
/// user-picked directory — everything is addressed through content:// document URIs and a
/// ContentResolver, which is why this cannot be implemented on top of System.IO the way
/// FilePickerService is. Picking, recursively enumerating and zipping are done together
/// here because streaming each entry straight from the ContentResolver into the zip is the
/// only way to build it without first copying the whole tree to a scratch folder.
/// </summary>
public class FolderPickerService : IFolderPickerService
{
    public async Task<UploadableItem?> PickFolderAndZipAsync(IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        var treeUri = await ActivityResultBridge.RequestOpenDocumentTreeAsync();
        if (treeUri is null)
            return null;

        var context = global::Android.App.Application.Context;
        var resolver = context.ContentResolver!;

        resolver.TakePersistableUriPermission(treeUri, ActivityFlags.GrantReadUriPermission);

        var rootDocumentId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        var rootDocumentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootDocumentId)!;
        var folderName = SanitizeFileName(GetDisplayName(resolver, rootDocumentUri) ?? "pasta");

        var files = new List<(string RelativePath, AndroidUri Uri)>();
        CollectFiles(resolver, treeUri, rootDocumentUri, folderName, files);

        if (files.Count == 0)
            throw new InvalidOperationException("A pasta selecionada não contém arquivos.");

        var zipPath = Path.Combine(FileSystem.CacheDirectory, $"{folderName}-{Guid.NewGuid():N}.zip");

        try
        {
            await using (var zipStream = File.Create(zipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                for (var i = 0; i < files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var (relativePath, uri) = files[i];
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);

                    await using var entryStream = entry.Open();
                    await using var sourceStream = resolver.OpenInputStream(uri)
                        ?? throw new InvalidOperationException($"Não foi possível ler '{relativePath}'.");

                    await sourceStream.CopyToAsync(entryStream, cancellationToken);

                    progress?.Report((i + 1) / (double)files.Count);
                }
            }
        }
        catch
        {
            TryDeleteFile(zipPath);
            throw;
        }

        return new UploadableItem
        {
            LocalFilePath = zipPath,
            FileName = $"{folderName}.zip",
            ContentType = "application/zip",
            SizeBytes = new FileInfo(zipPath).Length,
            IsFolder = true,
            IsTemporaryFile = true
        };
    }

    private static void CollectFiles(
        ContentResolver resolver,
        AndroidUri treeUri,
        AndroidUri parentDocumentUri,
        string relativePath,
        List<(string, AndroidUri)> files)
    {
        var parentDocumentId = DocumentsContract.GetDocumentId(parentDocumentUri);
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentDocumentId)!;

        using var cursor = resolver.Query(
            childrenUri,
            [
                DocumentsContract.Document.ColumnDocumentId!,
                DocumentsContract.Document.ColumnDisplayName!,
                DocumentsContract.Document.ColumnMimeType!
            ],
            null, null, null);

        if (cursor is null)
            return;

        while (cursor.MoveToNext())
        {
            var documentId = cursor.GetString(0);
            var displayName = cursor.GetString(1) ?? documentId ?? "arquivo";
            var mimeType = cursor.GetString(2);

            var childUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId)!;
            var childRelativePath = $"{relativePath}/{SanitizeFileName(displayName)}";

            if (mimeType == DocumentsContract.Document.MimeTypeDir)
                CollectFiles(resolver, treeUri, childUri, childRelativePath, files);
            else
                files.Add((childRelativePath, childUri));
        }
    }

    private static string? GetDisplayName(ContentResolver resolver, AndroidUri documentUri)
    {
        using var cursor = resolver.Query(documentUri, [DocumentsContract.Document.ColumnDisplayName!], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            name = name.Replace(invalidChar, '_');

        return name;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup — an orphaned temp file here is not worth failing the
            // original operation over.
        }
    }
}
