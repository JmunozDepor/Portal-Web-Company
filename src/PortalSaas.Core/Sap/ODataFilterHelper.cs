namespace PortalSaas.Core.Sap;

/// <summary>
/// Ayuda a construir filtros OData ($filter) seguros contra Service Layer -- portado
/// tal cual de referencia-original/PortalSAP_v2. Todo valor que venga de un usuario
/// (Excel importado, parámetros de formulario) debe pasar por acá antes de
/// interpolarse en un filtro, para evitar inyección de filtro OData -- mismo criterio
/// que la regla de "SQL siempre parametrizado" aplicada a HANA, adaptado a OData (que
/// no soporta parámetros bindeados como SQL).
/// </summary>
internal static class ODataFilterHelper
{
    /// <summary>Escapa comillas simples duplicándolas, tal como exige la sintaxis de literales OData.</summary>
    public static string Escape(string? value) => value?.Replace("'", "''") ?? "";

    /// <summary>Construye "campo eq 'valorEscapado'".</summary>
    public static string Eq(string field, string? value) => $"{field} eq '{Escape(value)}'";
}
