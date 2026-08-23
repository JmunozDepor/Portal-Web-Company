using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Traslados
/// (IbShipment) pendientes de enviar a Oracle WMS Cloud (etapa Subida de la Ronda C).
/// Cada WmsSapStageInboundHdr ya es una unidad -- no hay agrupación real como en
/// WmsSlshInventoryReader (Ronda B), el detalle simplemente se anida bajo "Lineas".
/// </summary>
public class WmsSapStageInboundReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageInboundReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Traslado.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken)
    {
        var hdrs = await _contexto.WmsSapStageInboundHdrs
            .Where(h => h.CompanyId == companyId && h.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        var registros = new List<IntegrationRecord>();
        foreach (var hdr in hdrs)
        {
            var detalle = await _contexto.WmsSapStageInboundDtls
                .Where(d => d.ParentId == hdr.LineId)
                .OrderBy(d => d.LineNum)
                .ToListAsync(cancellationToken);

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "IbShipment",
                ["SapDocEntry"] = hdr.SapDocEntry,
                ["ShipmentType"] = hdr.ShipmentType,
                ["Lineas"] = detalle
                    .Select(d => new IntegrationRecord(new Dictionary<string, object?>
                    {
                        ["ItemCode"] = d.ItemCode,
                        ["Quantity"] = d.Quantity,
                        ["WhsCode"] = d.WhsCode,
                        ["LineNum"] = d.LineNum,
                    }))
                    .ToList(),
                ["_StagingLineIds"] = new List<long> { hdr.LineId },
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageInboundHdrs
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;

            if (exito)
            {
                await ResetearValidacionAsync(companyId, fila.SapDocEntry.ToString(), cancellationToken);
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
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == "IbShipment" && v.Clave == clave, cancellationToken);
        if (validacion is null)
        {
            validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = "IbShipment", Clave = clave };
            _contexto.WmsExportValidations.Add(validacion);
        }

        validacion.Intentos = 0;
        validacion.WmsErrorMsg = null;
        validacion.ValidadoEn = null;
        validacion.WmsStatusId = null;
        validacion.EnviadoEn = DateTimeOffset.UtcNow;
    }
}
