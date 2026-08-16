using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico de integración (IIntegrationEntityWriter) para
/// Artículos que llegan desde SAP (dirección Bajada) hacia el staging local -- upsert
/// por (CompanyId, ItemCode): si no existe, inserta Pendiente; si existe y sigue
/// Pendiente, no duplica; si existe y ya fue ProcesadoWms pero SAP tiene una versión
/// más nueva (SourceUpdateDate), vuelve a Pendiente para resync. Reemplaza el cursor
/// de fecha que el connector no puede consultar (ver Global Constraints del plan).
/// </summary>
public class WmsSapStageItemWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageItemWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Item";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var itemCode = (string)registro["ItemCode"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;

            var existente = await _contexto.WmsSapStageItems
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.ItemCode == itemCode, cancellationToken);

            if (existente is null)
            {
                _contexto.WmsSapStageItems.Add(new WmsSapStageItem
                {
                    CompanyId = companyId,
                    ItemCode = itemCode,
                    ItemName = (string)registro["ItemName"]!,
                    BarCode = (string?)registro["BarCode"],
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                });
                continue;
            }

            var debeResincronizar = (existente.Status == WmsSapStageStatus.ProcesadoWms && sourceUpdateDate > existente.SourceUpdateDate)
                || existente.Status == WmsSapStageStatus.ErrorWms;

            if (debeResincronizar)
            {
                existente.Status = WmsSapStageStatus.Pendiente;
                existente.ErrorMsg = null;
            }

            existente.ItemName = (string)registro["ItemName"]!;
            existente.BarCode = (string?)registro["BarCode"];
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
