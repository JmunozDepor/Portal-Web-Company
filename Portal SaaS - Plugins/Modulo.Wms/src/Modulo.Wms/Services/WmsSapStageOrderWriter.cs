using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Picking (Listas de
/// Picking liberadas en SAP) que llegan desde SAP. Mismo criterio de upsert por clave
/// natural (CompanyId, OrderNbr) y mismo tratamiento de detalle que
/// WmsSapStageInboundWriter (Ronda C) -- incluido el resync desde ErrorWms desde el
/// primer intento (bug real encontrado y corregido recién en la revisión final de
/// Ronda C para las otras 3 entidades, no se repite acá).
/// </summary>
public class WmsSapStageOrderWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageOrderWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Order";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var orderNbr = (string)registro["OrderNbr"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;
            var lineas = (List<IntegrationRecord>)registro["Lineas"]!;

            var hdr = await _contexto.WmsSapStageOrderHdrs
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.OrderNbr == orderNbr, cancellationToken);

            bool insertarDetalle;

            if (hdr is null)
            {
                hdr = new WmsSapStageOrderHdr
                {
                    CompanyId = companyId,
                    OrderNbr = orderNbr,
                    OrderType = (string)registro["OrderType"]!,
                    PickListAbsEntry = (int)registro["PickListAbsEntry"]!,
                    BaseObjectType = (int)registro["BaseObjectType"]!,
                    BaseEntry = (int)registro["BaseEntry"]!,
                    CardCode = (string)registro["CardCode"]!,
                    CardName = (string)registro["CardName"]!,
                    CustomerPoNbr = (string?)registro["CustomerPoNbr"],
                    OrdDate = (DateTime?)registro["OrdDate"],
                    ExpDate = (DateTime?)registro["ExpDate"],
                    ReqShipDate = (DateTime?)registro["ReqShipDate"],
                    ShipToCode = (string?)registro["ShipToCode"],
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                };
                _contexto.WmsSapStageOrderHdrs.Add(hdr);
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

                hdr.OrderType = (string)registro["OrderType"]!;
                hdr.PickListAbsEntry = (int)registro["PickListAbsEntry"]!;
                hdr.BaseObjectType = (int)registro["BaseObjectType"]!;
                hdr.BaseEntry = (int)registro["BaseEntry"]!;
                hdr.CardCode = (string)registro["CardCode"]!;
                hdr.CardName = (string)registro["CardName"]!;
                hdr.CustomerPoNbr = (string?)registro["CustomerPoNbr"];
                hdr.OrdDate = (DateTime?)registro["OrdDate"];
                hdr.ExpDate = (DateTime?)registro["ExpDate"];
                hdr.ReqShipDate = (DateTime?)registro["ReqShipDate"];
                hdr.ShipToCode = (string?)registro["ShipToCode"];
                hdr.SourceUpdateDate = sourceUpdateDate;

                if (esResync)
                {
                    var detalleExistente = await _contexto.WmsSapStageOrderDtls
                        .Where(d => d.ParentId == hdr.LineId)
                        .ToListAsync(cancellationToken);
                    _contexto.WmsSapStageOrderDtls.RemoveRange(detalleExistente);
                    await _contexto.SaveChangesAsync(cancellationToken);
                }

                insertarDetalle = esResync;
            }

            if (insertarDetalle)
            {
                foreach (var linea in lineas)
                {
                    _contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl
                    {
                        ParentId = hdr.LineId,
                        ItemCode = (string)linea["ItemCode"]!,
                        Quantity = (decimal)linea["Quantity"]!,
                        WhsCode = (string?)linea["WhsCode"],
                        LineNum = (int)linea["LineNum"]!,
                        SeqNbr = (int)linea["SeqNbr"]!,
                    });
                }
            }

            await _contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
