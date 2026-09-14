using FileSharing.Mobile.Core.Models;

namespace FileSharing.Mobile.Core.Services.Upload;

/// <summary>Richer than a bare 0..1 fraction — the upload screen (Fase 13 §15) needs the stage
/// label ("Enviando com segurança...") and the raw byte counts, not just a percentage.</summary>
public record UploadProgressUpdate(UploadStage Stage, double Fraction, long BytesSent, long TotalBytes);
