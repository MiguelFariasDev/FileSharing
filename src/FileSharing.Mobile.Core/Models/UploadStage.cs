namespace FileSharing.Mobile.Core.Models;

/// <summary>
/// Drives the "Preparando... / Enviando... / Finalizando... / Concluído / Falhou" states the
/// upload screen (Fase 13) shows — distinct from the 0..1 progress fraction, which only moves
/// during <see cref="Uploading"/>.
/// </summary>
public enum UploadStage
{
    Idle,
    Preparing,
    Uploading,
    Completing,
    Completed,
    Failed
}
