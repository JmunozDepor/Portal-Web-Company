namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Marca de origen en los documentos que el portal crea en SAP, para reconocerlos después
/// y NO duplicarlos si un envío se interrumpe (servidor caído entre "SAP lo creó" y "el
/// portal lo registró"). Común a todos los flujos: borradores digitados (clave = Id del
/// borrador) e Importación Genérica (clave = trabajo + documento del archivo).
///
/// Dónde se escribe, por compañía (company_document_settings.draft_marker_udf_name):
/// - UDF de cabecera configurado (ej. U_PortalDraftId): la clave tal cual, exacta.
/// - Sin UDF: una etiqueta corta al final de Comentarios, ej. "(BR-8f3a2c1d9e0b)" (hash de
///   la clave) -- funciona sin configurar nada en SAP.
/// La búsqueda siempre mira las dos (un documento pudo crearse antes de configurar el UDF).
/// </summary>
public interface IDocumentSapMarkerService
{
    /// <summary>Cómo marcar un documento con esta clave en la compañía activa.</summary>
    Task<DocumentSapMarker> GetMarkerAsync(string key, CancellationToken ct = default);

    /// <summary>DocEntry del documento NO cancelado del tipo dado que lleva la marca de esta clave, o null.</summary>
    Task<int?> FindDocEntryAsync(string engine, string documentType, string key, CancellationToken ct = default);
}

/// <param name="UdfName">UDF de cabecera configurado (null = se marca en Comentarios).</param>
/// <param name="UdfValue">Valor a escribir en el UDF (la clave).</param>
/// <param name="CommentsTag">Etiqueta para Comentarios, ej. "(BR-8f3a2c1d9e0b)".</param>
public sealed record DocumentSapMarker(string? UdfName, string UdfValue, string CommentsTag)
{
    /// <summary>Largo máximo de Comentarios en la cabecera de un documento SAP.</summary>
    public const int CommentsMaxLength = 254;

    /// <summary>Campos de usuario con el UDF agregado (si hay UDF configurado).</summary>
    public IReadOnlyDictionary<string, object?>? ApplyToAdditionalFields(IReadOnlyDictionary<string, object?>? fields)
    {
        if (UdfName is null)
        {
            return fields;
        }

        var merged = fields is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(fields);
        merged[UdfName] = UdfValue;
        return merged;
    }

    /// <summary>Comentarios con la etiqueta al final (solo sin UDF), recortando el texto del usuario si no entra.</summary>
    public string? ApplyToComments(string? comments)
    {
        if (UdfName is not null)
        {
            return comments;
        }

        var text = (comments ?? string.Empty).Trim();
        var room = CommentsMaxLength - CommentsTag.Length - 1;
        if (text.Length > room)
        {
            text = text[..Math.Max(0, room)].TrimEnd();
        }

        return text.Length == 0 ? CommentsTag : $"{text} {CommentsTag}";
    }
}
