using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Artículos
/// pendientes de enviar a Oracle WMS Cloud (etapa Subida de la Ronda C). Usa el
/// LineId de la propia fila como "_StagingLineIds" de una sola posición -- mismo
/// campo interno que WmsSlshInventoryReader (Ronda B), reutilizado acá aunque el
/// grupo siempre tenga tamaño 1 (no hay agrupación como en Traslados).
/// </summary>
public class WmsSapStageItemReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageItemReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Item.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await _contexto.WmsSapStageItems
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f => new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Item",
                ["ItemCode"] = f.ItemCode,
                ["ItemName"] = f.ItemName,
                ["BarCode"] = f.BarCode,
                ["_StagingLineIds"] = new List<long> { f.LineId },
            }))
            .ToList();
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageItems
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;

            if (exito)
            {
                await ResetearValidacionAsync(companyId, fila.ItemCode, cancellationToken);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Al (re)enviar exitosamente, resetea la fila de validación existente (clave de negocio
    /// estable entre reenvíos) para que WmsExistsReconciler no herede Intentos de un ciclo
    /// anterior y dispare ErrorWms de inmediato en el primer chequeo del reenvío.
    /// </summary>
    private async Task ResetearValidacionAsync(Guid companyId, string clave, CancellationToken cancellationToken)
    {
        var validacion = await _contexto.WmsExportValidations
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == "Item" && v.Clave == clave, cancellationToken);
        if (validacion is null)
        {
            validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = "Item", Clave = clave };
            _contexto.WmsExportValidations.Add(validacion);
        }

        validacion.Intentos = 0;
        validacion.WmsErrorMsg = null;
        validacion.ValidadoEn = null;
        validacion.WmsStatusId = null;
        validacion.EnviadoEn = DateTimeOffset.UtcNow;
    }
}
