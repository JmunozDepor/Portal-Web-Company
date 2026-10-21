namespace Modulo.Despachos.Models;

/// <summary>Ajustes del módulo por compañía (una fila por compañía).</summary>
public class DespachosSetting : AuditedEntity
{
    /// <summary>RUT de la compañía como emisor de los DTE (se compara con el RE del timbre).</summary>
    public string? IssuerTaxId { get; set; }
}
