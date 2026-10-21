namespace Modulo.Despachos.Models;

/// <summary>
/// Consulta SAP configurable por compañía y tipo de documento (sap_document_queries):
/// SQL de búsqueda por folio que devuelve el contrato fijo de columnas.
/// </summary>
public class SapDocumentQuery : AuditedEntity
{
    /// <summary>"invoice" o "transfer_guide" (ver <see cref="DocumentKinds"/>).</summary>
    public string DocumentKind { get; set; } = "";
    public string SqlText { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>Última vez (UTC) que la consulta pasó la validación del contrato.</summary>
    public DateTime? ValidatedAt { get; set; }
}
