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
            var sourceUpdateDate = ConvertirFecha(registro["SourceUpdateDate"]);
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

    /// <summary>
    /// A diferencia de Item, "U_NX_UPDATEDATE" en OCRD es un UDF tipado "nvarchar" en HANA
    /// (confirmado 24 ago 2026 contra CLPRDDEPOR: GetDataTypeName devuelve "nvarchar", el
    /// valor llega como System.String con formato "2025-02-08 13:52:59.1690000"), no un
    /// timestamp nativo -- un cast directo a DateTime tira InvalidCastException. Se acepta
    /// tanto DateTime (si algún día la columna cambia de tipo) como el string actual.
    /// </summary>
    private static DateTime ConvertirFecha(object? valor) => valor switch
    {
        DateTime dt => dt,
        string s => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"SourceUpdateDate con tipo inesperado: {valor?.GetType().FullName ?? "null"}"),
    };

    private static bool ValorCambio(Dictionary<string, object?> existentes, Dictionary<string, object?> nuevos, string campo)
    {
        var tieneExistente = existentes.TryGetValue(campo, out var valorExistente);
        var tieneNuevo = nuevos.TryGetValue(campo, out var valorNuevo);
        if (!tieneExistente && !tieneNuevo) return false;
        return valorExistente?.ToString() != valorNuevo?.ToString();
    }
}
