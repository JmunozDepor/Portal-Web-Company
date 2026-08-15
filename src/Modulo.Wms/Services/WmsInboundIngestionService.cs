using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

public class WmsInboundIngestionService : IWmsInboundIngestionService
{
    private readonly WmsDbContext _contexto;

    public WmsInboundIngestionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsInboundIngestionResult> InsertPendingAsync(
        Guid companyId,
        string tipoDoc,
        string formato,
        string nombreArchivo,
        string hashArchivo,
        string contenido,
        CancellationToken cancellationToken)
    {
        var yaExiste = await _contexto.WmsOracleInboundStages
            .AnyAsync(s => s.CompanyId == companyId && s.HashArchivo == hashArchivo, cancellationToken);

        if (yaExiste)
        {
            return new WmsInboundIngestionResult { Insertado = false, Duplicado = true };
        }

        _contexto.WmsOracleInboundStages.Add(new WmsOracleInboundStage
        {
            CompanyId = companyId,
            TipoDoc = tipoDoc,
            Formato = Enum.Parse<WmsInboundFormato>(formato, ignoreCase: true),
            NombreArchivo = nombreArchivo,
            HashArchivo = hashArchivo,
            Contenido = contenido,
            Estado = WmsInboundEstado.Pendiente,
        });
        await _contexto.SaveChangesAsync(cancellationToken);

        return new WmsInboundIngestionResult { Insertado = true, Duplicado = false };
    }
}
