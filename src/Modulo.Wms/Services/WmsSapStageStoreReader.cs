using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Bodegas/Clientes
/// (Store) pendientes de enviar a Oracle WMS Cloud (etapa Subida de la Ronda C). Mismo
/// patrón que WmsSapStageItemReader.
/// </summary>
public class WmsSapStageStoreReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageStoreReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Store.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await _contexto.WmsSapStageStores
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f => new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Store",
                ["CardCode"] = f.CardCode,
                ["CardName"] = f.CardName,
                ["Street"] = f.Street,
                ["City"] = f.City,
                ["ZipCode"] = f.ZipCode,
                ["_StagingLineIds"] = new List<long> { f.LineId },
            }))
            .ToList();
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageStores
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
