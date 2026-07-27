using System.Collections;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Aplana un objeto tipado del wire format de Service Layer (SapSalesDocumentHeader/Line,
/// SapPurchaseDocumentHeader/Line, SapInventoryDocumentHeader/Line) a un diccionario plano,
/// mezclando por encima los pares de su propiedad "AdditionalFields" -- así
/// Modulo.ImportacionGenerica puede postear campos de usuario (UDF dinámicos) sin que
/// SalesDocumentService/PurchaseDocumentService/InventoryDocumentService necesiten conocer sus
/// nombres de antemano. Portado de SapCamposAdicionalesHelper (referencia-original/PortalSAP_v2).
///
/// Funciona por reflection (nunca por tipo concreto) a propósito: sirve igual para los 3 motores
/// sin duplicar la lógica, y evita depender de qué serializador usa B1SLayer internamente -- un
/// IDictionary&lt;string, object&gt; plano se serializa igual en cualquiera.
///
/// Propiedades null se omiten (mismo comportamiento que ya usa el posteo tipado hoy, ej.
/// UnitPrice null), así el payload aplanado sin campos adicionales es idéntico byte a byte al
/// que se posteaba antes de este mecanismo.
/// </summary>
public static class SapAdditionalFieldsHelper
{
    private const string AdditionalFieldsPropertyName = "AdditionalFields";
    private static readonly string[] LinesPropertyNames = ["DocumentLines", "StockTransferLines"];

    /// <summary>
    /// Nombres de propiedad ya resueltos por la lógica núcleo de los 3 motores genéricos
    /// (cabecera y línea, combinados) -- un campo de usuario nunca puede declarar uno de estos,
    /// para no pisar por accidente un valor que el motor ya calculó.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Línea
        "LineNum", "ItemType", "ItemCode", "ItemDescription", "Quantity", "UnitPrice",
        "DiscountPercent", "AccountCode", "WarehouseCode", "CostingCode", "CostingCode2",
        "CostingCode3", "RequiredDate", "FromWarehouseCode",
        // Cabecera
        "DocEntry", "DocNum", "DocType", "CardCode", "CardName", "SalesPersonCode", "Series",
        "TransportationCode", "GroupNumber", "Comments", "DocDate", "DocDueDate", "RequriedDate",
        "TaxDate", "NumAtCard", "DocTotal", "DocumentStatus",
        "DocumentLines", "StockTransferLines", "U_PortalUser",
        AdditionalFieldsPropertyName,
    };

    public static bool IsReservedName(string fieldName) => ReservedNames.Contains(fieldName.Trim());

    public static Dictionary<string, object?> Flatten(object typedObject)
    {
        var result = new Dictionary<string, object?>();
        var type = typedObject.GetType();

        foreach (var property in type.GetProperties())
        {
            if (string.Equals(property.Name, AdditionalFieldsPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = property.GetValue(typedObject);
            if (value is null)
            {
                continue;
            }

            if (Array.IndexOf(LinesPropertyNames, property.Name) >= 0 && value is IEnumerable lines and not string)
            {
                var flattenedLines = new List<Dictionary<string, object?>>();
                foreach (var line in lines)
                {
                    if (line is not null)
                    {
                        flattenedLines.Add(Flatten(line));
                    }
                }
                result[property.Name] = flattenedLines;
                continue;
            }

            result[property.Name] = value;
        }

        var additionalFieldsProperty = type.GetProperty(AdditionalFieldsPropertyName);
        if (additionalFieldsProperty?.GetValue(typedObject) is IReadOnlyDictionary<string, object?> additionalFields)
        {
            foreach (var (key, value) in additionalFields)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
