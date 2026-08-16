using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico de integración (IIntegrationEntityWriter) para
/// Tiendas/Clientes (CardCode) que llegan desde SAP hacia el staging local -- mismo
/// criterio de upsert por clave natural que WmsSapStageItemWriter (ver ese archivo),
/// sobre (CompanyId, CardCode).
/// </summary>
public class WmsSapStageStoreWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageStoreWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Store";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var cardCode = (string)registro["CardCode"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;

            var existente = await _contexto.WmsSapStageStores
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.CardCode == cardCode, cancellationToken);

            if (existente is null)
            {
                _contexto.WmsSapStageStores.Add(new WmsSapStageStore
                {
                    CompanyId = companyId,
                    CardCode = cardCode,
                    CardName = (string)registro["CardName"]!,
                    Street = (string?)registro["Street"],
                    City = (string?)registro["City"],
                    ZipCode = (string?)registro["ZipCode"],
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

            existente.CardName = (string)registro["CardName"]!;
            existente.Street = (string?)registro["Street"];
            existente.City = (string?)registro["City"];
            existente.ZipCode = (string?)registro["ZipCode"];
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
