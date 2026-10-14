using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Inventario;
using PortalSaas.Core.Ventas;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// La OC del archivo (CustomerReferenceNumber) ya existe en SAP para el MISMO socio, en un
/// documento no cancelado del mismo tipo -- evita cargas duplicadas (pedido del dueño del
/// proyecto, 2026-10-14). Mismo N° de OC de OTRO socio no es duplicado.
///
/// Tabla = la del tipo de documento del formato (SalesDocumentTypeCatalog/etc., ej. ORDR);
/// "documentType" lo agrega GenericImportService a los parámetros antes de correr el motor
/// (la regla no lo recibe de otra forma). Columna de la OC: NumAtCard en Venta/Compra,
/// U_NumAtCard en Inventario (OWTQ/OWTR no traen NumAtCard estándar -- mismo criterio que
/// InventoryDocumentService.ListAsync).
/// </summary>
public sealed class DuplicateOrderInSapRule : IGenericImportValidationRule
{
    internal const string DocumentTypeParameter = "documentType";

    private readonly IHanaService _hana;

    public DuplicateOrderInSapRule(IHanaService hana)
    {
        _hana = hana;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.DuplicateOrderInSap;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        var documentType = parameters.TryGetValue(DocumentTypeParameter, out var dt) ? dt?.ToString() : null;
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return result;
        }

        var relevantRows = rows.Where(r => !string.IsNullOrWhiteSpace(r.CustomerReferenceNumber) && r.BusinessPartnerCardCode is not null).ToList();
        var references = relevantRows.Select(r => r.CustomerReferenceNumber!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (references.Count == 0)
        {
            return result;
        }

        var (table, referenceColumn) = SourceOf(module, documentType);

        var parametersSql = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        for (var i = 0; i < references.Count; i++)
        {
            placeholders.Add($":ref{i}");
            parametersSql[$"ref{i}"] = references[i];
        }

        // table/referenceColumn salen de catálogos estáticos del código, nunca del usuario.
        var sql = $"""
            SELECT "DocNum", "CardCode", "{referenceColumn}" AS "Reference"
            FROM "{table}"
            WHERE "CANCELED" = 'N' AND "{referenceColumn}" IN ({string.Join(",", placeholders)})
            """;
        var existing = await _hana.QueryDynamicAsync(sql, parametersSql, ct);

        // (Referencia, CardCode) -> N° de documento(s) SAP existentes.
        var docNumsByKey = existing
            .Select(r => (Reference: r["Reference"]?.ToString()?.Trim() ?? string.Empty,
                CardCode: r["CardCode"]?.ToString()?.Trim() ?? string.Empty,
                DocNum: r["DocNum"]?.ToString() ?? string.Empty))
            .GroupBy(r => (r.Reference.ToUpperInvariant(), r.CardCode.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Select(x => x.DocNum).Distinct().ToList());

        foreach (var row in relevantRows)
        {
            var key = (row.CustomerReferenceNumber!.Trim().ToUpperInvariant(), row.BusinessPartnerCardCode!.Trim().ToUpperInvariant());
            if (docNumsByKey.TryGetValue(key, out var docNums))
            {
                result[row.RowNumber] =
                [
                    $"La OC \"{row.CustomerReferenceNumber!.Trim()}\" ya está cargada en SAP para el socio \"{row.BusinessPartnerCardCode}\" " +
                    $"(documento N° {string.Join(", ", docNums)}) -- posible carga duplicada.",
                ];
            }
        }

        return result;
    }

    internal static (string Table, string ReferenceColumn) SourceOf(GenericImportModule module, string documentType) => module switch
    {
        GenericImportModule.Sales => (SalesDocumentTypeCatalog.Resolve(Enum.Parse<SalesDocumentType>(documentType)).Table, "NumAtCard"),
        GenericImportModule.Purchase => (PurchaseDocumentTypeCatalog.Resolve(Enum.Parse<PurchaseDocumentType>(documentType)).Table, "NumAtCard"),
        GenericImportModule.Inventory => (InventoryDocumentTypeCatalog.Resolve(Enum.Parse<InventoryDocumentType>(documentType)).Table, "U_NumAtCard"),
        _ => throw new InvalidOperationException("Módulo no soportado."),
    };
}
