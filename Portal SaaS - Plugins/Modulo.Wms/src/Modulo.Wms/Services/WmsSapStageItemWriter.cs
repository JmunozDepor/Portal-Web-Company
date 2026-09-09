using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico de integración (IIntegrationEntityWriter) para
/// Artículos que llegan desde SAP (dirección Bajada) hacia el staging local -- upsert
/// por (CompanyId, ItemCode): si no existe, inserta Pendiente; si existe y sigue
/// Pendiente, no duplica; si existe y ya fue ProcesadoWms, solo vuelve a Pendiente
/// cuando VALORES de campos listados en wms_validation_fields (WmsDbContext.ValidationFields,
/// TipoEntidad="Item") cambiaron de verdad -- un cambio en un campo no listado ahí
/// actualiza el dato en staging pero no dispara reenvío. Reemplaza el cursor de fecha
/// que el connector no puede consultar (ver Global Constraints del plan).
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
        var itemCodes = registros.Select(r => (string)r["item_alternate_code"]!).ToList();
        var existentes = await _contexto.WmsSapStageItems
            .Where(f => f.CompanyId == companyId && itemCodes.Contains(f.ItemCode))
            .ToDictionaryAsync(f => f.ItemCode, cancellationToken);

        var camposValidacion = (await _contexto.ValidationFields
            .Where(v => v.CompanyId == companyId && v.TipoEntidad == "Item" && v.IsActive)
            .Select(v => v.FieldName)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var registro in registros)
        {
            var itemCode = (string)registro["item_alternate_code"]!;
            var existente = existentes.GetValueOrDefault(itemCode);

            var (itemName, barCode, extraFieldsJson) = SepararCampos(registro);
            // Cursor de cambio (OITM.UpdateDate) -> columna tipada. ToUtc obligatorio: la
            // columna es timestamptz y Npgsql 8 rechaza cualquier Kind != Utc en
            // SaveChanges (ver WmsSourceDate). Antes no se asignaba -> quedaba en
            // default(DateTime) y el dato del cursor se perdía en extra_fields.
            var sourceUpdateDate = WmsSourceDate.ToUtc(registro["SourceUpdateDate"]);

            if (existente is null)
            {
                _contexto.WmsSapStageItems.Add(new WmsSapStageItem
                {
                    CompanyId = companyId,
                    ItemCode = itemCode,
                    ItemName = itemName,
                    BarCode = barCode,
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

            var cambioAlgunCampoDeValidacion = CampoDeValidacionCambio("description", existente.ItemName, itemName, camposValidacion)
                || CampoDeValidacionCambio("barcode", existente.BarCode, barCode, camposValidacion)
                || camposValidacion.Any(campo => ValorCambio(extraFieldsExistentes, extraFieldsNuevos, campo));

            if (cambioAlgunCampoDeValidacion || existente.Status == WmsSapStageStatus.ErrorWms)
            {
                existente.Status = WmsSapStageStatus.Pendiente;
                existente.ErrorMsg = null;
            }

            existente.ItemName = itemName;
            existente.BarCode = barCode;
            existente.ExtraFieldsJson = extraFieldsJson;
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// "description"/"barcode" son las claves que trae SqlDirectConnector (nombres de
    /// columna reales de la query, ver spec) -- item_name/bar_code son las columnas
    /// tipadas que las reciben. "SourceUpdateDate" también tiene columna tipada propia
    /// (cursor de cambio). El resto de las claves del registro cae a extra_fields.
    /// </summary>
    private static (string ItemName, string? BarCode, string ExtraFieldsJson) SepararCampos(IntegrationRecord registro)
    {
        var itemName = (string?)registro["description"] ?? string.Empty;
        var barCode = (string?)registro["barcode"];

        var extra = registro.Fields
            .Where(kvp => kvp.Key is not ("item_alternate_code" or "description" or "barcode" or "SourceUpdateDate"))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return (itemName, barCode, System.Text.Json.JsonSerializer.Serialize(extra));
    }

    private static bool CampoDeValidacionCambio(string campo, string? valorExistente, string? valorNuevo, HashSet<string> camposValidacion) =>
        camposValidacion.Contains(campo) && valorExistente != valorNuevo;

    private static bool ValorCambio(Dictionary<string, object?> existentes, Dictionary<string, object?> nuevos, string campo)
    {
        var tieneExistente = existentes.TryGetValue(campo, out var valorExistente);
        var tieneNuevo = nuevos.TryGetValue(campo, out var valorNuevo);
        if (!tieneExistente && !tieneNuevo) return false;
        return valorExistente?.ToString() != valorNuevo?.ToString();
    }
}
