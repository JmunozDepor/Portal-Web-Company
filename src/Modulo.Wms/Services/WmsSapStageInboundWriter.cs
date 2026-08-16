using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Traslados (Solicitudes
/// de Traslado, OWTQ) que llegan desde SAP. Mismo criterio de upsert por clave natural
/// que WmsSapStageItemWriter (ver ese archivo), sobre (CompanyId, SapDocEntry). El
/// detalle se reemplaza completo en cada resync -- más simple que un diff línea por
/// línea, y el volumen por traslado es chico (mismo criterio que
/// ItemCrossReferenceService.SyncAsync).
/// </summary>
public class WmsSapStageInboundWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageInboundWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Traslado";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var docEntry = (int)registro["SapDocEntry"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;
            var lineas = (List<IntegrationRecord>)registro["Lineas"]!;

            var hdr = await _contexto.WmsSapStageInboundHdrs
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.SapDocEntry == docEntry, cancellationToken);

            // Solo se toca el detalle (borrar + volver a insertar) cuando el header es
            // NUEVO (nada que borrar todavía) o cuando hace falta resync -- si el header
            // ya existe, sigue ProcesadoWms y SAP lo vuelve a mandar SIN cambios reales
            // (mismo SourceUpdateDate o más vieja), el detalle existente debe quedar
            // intacto: insertar de nuevo sin haber borrado antes duplica las líneas en
            // cada ciclo (bug real encontrado en la revisión final de Ronda C).
            bool insertarDetalle;

            if (hdr is null)
            {
                hdr = new WmsSapStageInboundHdr
                {
                    CompanyId = companyId,
                    SapDocEntry = docEntry,
                    ShipmentType = (string)registro["ShipmentType"]!,
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                };
                _contexto.WmsSapStageInboundHdrs.Add(hdr);
                await _contexto.SaveChangesAsync(cancellationToken);
                insertarDetalle = true;
            }
            else
            {
                var debeResincronizar = (hdr.Status == WmsSapStageStatus.ProcesadoWms && sourceUpdateDate > hdr.SourceUpdateDate)
                    || hdr.Status == WmsSapStageStatus.ErrorWms;
                var esResync = debeResincronizar || hdr.Status == WmsSapStageStatus.Pendiente;

                if (debeResincronizar)
                {
                    hdr.Status = WmsSapStageStatus.Pendiente;
                    hdr.ErrorMsg = null;
                }

                hdr.ShipmentType = (string)registro["ShipmentType"]!;
                hdr.SourceUpdateDate = sourceUpdateDate;

                if (esResync)
                {
                    var detalleExistente = await _contexto.WmsSapStageInboundDtls
                        .Where(d => d.ParentId == hdr.LineId)
                        .ToListAsync(cancellationToken);
                    _contexto.WmsSapStageInboundDtls.RemoveRange(detalleExistente);
                    await _contexto.SaveChangesAsync(cancellationToken);
                }

                insertarDetalle = esResync;
            }

            if (insertarDetalle)
            {
                foreach (var linea in lineas)
                {
                    _contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
                    {
                        ParentId = hdr.LineId,
                        ItemCode = (string)linea["ItemCode"]!,
                        Quantity = (decimal)linea["Quantity"]!,
                        WhsCode = (string)linea["WhsCode"]!,
                        LineNum = (int)linea["LineNum"]!,
                    });
                }
            }

            await _contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
