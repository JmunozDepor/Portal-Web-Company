namespace PortalSaas.Data.Entities;

/// <summary>
/// Progreso de un job de importación genérica -- reemplaza el ConcurrentDictionary
/// en memoria (rompía en web farm sin sticky sessions, ver docs/superpowers/plans).
/// Fila efímera: se sobrescribe en cada Update, no es historial.
/// </summary>
public sealed class GenericImportJobProgress
{
    public string JobId { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
