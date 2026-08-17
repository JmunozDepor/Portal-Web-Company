using System.Text.RegularExpressions;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Motor de templates para wms_oracle_field_mappings (ValueTemplate) -- no existía
/// ningún resolver de este lenguaje en este repo: el único consumidor previo de esta
/// tabla es WmsSapIntegration.Service, un Windows Service standalone fuera de este
/// código. Sustituye cada {NombreCampo} por el valor de ese campo en el
/// IntegrationRecord, dejando el resto del texto literal -- así "{A} ; {B}" concatena
/// dos campos sin necesitar ningún caso especial, y un template sin placeholders
/// (ej. "DEPOR") es simplemente un literal.
/// </summary>
public static class WmsFieldTemplateResolver
{
    private static readonly Regex PlaceholderRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static string Resolve(string template, IntegrationRecord registro)
    {
        return PlaceholderRegex.Replace(template, match =>
        {
            var campo = match.Groups[1].Value;
            return registro[campo]?.ToString() ?? string.Empty;
        });
    }
}
