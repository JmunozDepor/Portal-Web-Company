namespace Modulo.Wms.Models;

public enum WmsSapStageStatus { Pendiente, ProcesadoWms, ErrorWms }

public class WmsSapStageItem
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? BarCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}
