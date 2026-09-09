using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Sucursales/Tiendas que llegan
/// desde SAP vía SqlDirectConnector -- upsert por Pk (clave de negocio que la propia Query SQL
/// calcula, ver plan 2026-08-23-ingesta-sql-directa-sucursal.md). Mismo criterio que
/// WmsSapStageItemWriter: Pendiente si nuevo, reenvío solo si cambió un campo listado en
/// wms_validation_fields (TipoEntidad="Store").
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
        // La Query de Bajada calcula PK como UPPER(REPLACE(CardCode,'-','') || '-' || LineNum)
        // sobre un JOIN OCRD ⋈ CRD1 filtrado solo por AdresType='S' -- dos filas de HANA
        // pueden colapsar al mismo PK (CardCodes que difieren únicamente en guiones o
        // mayúsculas, o >1 dirección de despacho con el mismo LineNum). Sin deduplicar, cada
        // ocurrencia se .Add()ea como fila nueva y SaveChangesAsync revienta contra el índice
        // único ix_wms_sap_stage_store_company_pk ("An error occurred while saving the entity
        // changes"). Nos quedamos con la última de cada grupo -- la Query ordena por
        // U_NX_UPDATEDATE ascendente, así que la última fila del grupo es la más reciente.
        registros = registros
            .GroupBy(r => (string)r["PK"]!)
            .Select(g => g.Last())
            .ToList();

        var pks = registros.Select(r => (string)r["PK"]!).ToList();
        var existentes = await _contexto.WmsSapStageStores
            .Where(f => f.CompanyId == companyId && pks.Contains(f.Pk))
            .ToDictionaryAsync(f => f.Pk, cancellationToken);

        var camposValidacion = (await _contexto.ValidationFields
            .Where(v => v.CompanyId == companyId && v.TipoEntidad == "Store" && v.IsActive)
            .Select(v => v.FieldName)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var registro in registros)
        {
            var pk = (string)registro["PK"]!;
            var sourceUpdateDate = WmsSourceDate.ToUtc(registro["SourceUpdateDate"]);
            var existente = existentes.GetValueOrDefault(pk);

            var extra = registro.Fields
                .Where(kvp => kvp.Key is not ("PK" or "SourceUpdateDate"))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var extraFieldsJson = System.Text.Json.JsonSerializer.Serialize(extra);

            if (existente is null)
            {
                _contexto.WmsSapStageStores.Add(new WmsSapStageStore
                {
                    CompanyId = companyId,
                    Pk = pk,
                    ExtraFieldsJson = extraFieldsJson,
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                });
                continue;
            }

            var extraFieldsExistentes = string.IsNullOrEmpty(existente.ExtraFieldsJson)
                ? new Dictionary<string, object?>()
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(existente.ExtraFieldsJson)!;
            var extraFieldsNuevos = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(extraFieldsJson)!;

            var cambioAlgunCampoDeValidacion = camposValidacion.Any(campo => ValorCambio(extraFieldsExistentes, extraFieldsNuevos, campo));

            if (cambioAlgunCampoDeValidacion || existente.Status == WmsSapStageStatus.ErrorWms)
            {
                existente.Status = WmsSapStageStatus.Pendiente;
                existente.ErrorMsg = null;
            }

            existente.ExtraFieldsJson = extraFieldsJson;
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static bool ValorCambio(Dictionary<string, object?> existentes, Dictionary<string, object?> nuevos, string campo)
    {
        var tieneExistente = existentes.TryGetValue(campo, out var valorExistente);
        var tieneNuevo = nuevos.TryGetValue(campo, out var valorNuevo);
        if (!tieneExistente && !tieneNuevo) return false;
        return valorExistente?.ToString() != valorNuevo?.ToString();
    }
}
