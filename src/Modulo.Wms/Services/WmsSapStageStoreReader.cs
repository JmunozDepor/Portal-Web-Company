using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

public class WmsSapStageStoreReader : IIntegrationEntityReader
{
    private const int MaximoPorCicloPorDefecto = 500;

    private readonly WmsDbContext _contexto;

    public WmsSapStageStoreReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Store.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken)
    {
        var maximoPorCiclo = limiteMaximo is > 0 ? limiteMaximo.Value : MaximoPorCicloPorDefecto;
        var filas = await _contexto.WmsSapStageStores
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .OrderBy(f => f.CreatedAt)
            .Take(maximoPorCiclo)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f =>
            {
                var campos = new Dictionary<string, object?>
                {
                    ["TipoDocumento"] = "Store",
                    ["PK"] = f.Pk,
                    ["_StagingLineIds"] = new List<long> { f.LineId },
                };

                if (!string.IsNullOrEmpty(f.ExtraFieldsJson))
                {
                    var extra = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(f.ExtraFieldsJson)!;
                    foreach (var (clave, valor) in extra)
                    {
                        campos[clave] = valor;
                    }
                }

                return new IntegrationRecord(campos);
            })
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

            if (exito)
            {
                await ResetearValidacionAsync(companyId, fila.Pk, cancellationToken);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private async Task ResetearValidacionAsync(Guid companyId, string clave, CancellationToken cancellationToken)
    {
        var validacion = await _contexto.WmsExportValidations
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == "Store" && v.Clave == clave, cancellationToken);
        if (validacion is null)
        {
            validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = "Store", Clave = clave };
            _contexto.WmsExportValidations.Add(validacion);
        }

        validacion.Intentos = 0;
        validacion.WmsErrorMsg = null;
        validacion.ValidadoEn = null;
        validacion.WmsStatusId = null;
        validacion.EnviadoEn = DateTimeOffset.UtcNow;
    }
}
