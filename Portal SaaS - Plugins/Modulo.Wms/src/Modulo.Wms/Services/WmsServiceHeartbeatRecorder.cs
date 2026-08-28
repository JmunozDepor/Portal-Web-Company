using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// Upsert por (CompanyId, ProcessorKey) sobre wms_oracle_service_heartbeats --
/// mismo contrato que ServiceHeartbeat.RecordAsync del legado. Llamado al
/// final (éxito o error) del ciclo de cada BackgroundService del plugin.
/// </summary>
public class WmsServiceHeartbeatRecorder : IWmsServiceHeartbeatRecorder
{
    private readonly WmsDbContext _contexto;

    public WmsServiceHeartbeatRecorder(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task RecordAsync(Guid companyId, string processorKey, string status, string? error = null, CancellationToken cancellationToken = default)
    {
        var fila = await _contexto.ServiceHeartbeats
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.ProcessorKey == processorKey, cancellationToken);

        if (fila is null)
        {
            fila = new WmsServiceHeartbeat { CompanyId = companyId, ProcessorKey = processorKey };
            _contexto.ServiceHeartbeats.Add(fila);
        }

        fila.LastRunAt = DateTimeOffset.UtcNow;
        fila.Status = status;
        fila.LastError = error;

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
