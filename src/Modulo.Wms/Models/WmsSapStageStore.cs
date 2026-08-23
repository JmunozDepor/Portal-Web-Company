namespace Modulo.Wms.Models;

public class WmsSapStageStore
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string Pk { get; set; } = string.Empty;
    public string? ExtraFieldsJson { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}
