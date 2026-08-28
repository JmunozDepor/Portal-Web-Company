namespace Modulo.Wms.Services;

public interface IWmsServiceHeartbeatRecorder
{
    Task RecordAsync(Guid companyId, string processorKey, string status, string? error = null, CancellationToken cancellationToken = default);
}
