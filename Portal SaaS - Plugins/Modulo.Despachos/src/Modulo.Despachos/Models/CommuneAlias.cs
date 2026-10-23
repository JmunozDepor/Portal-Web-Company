namespace Modulo.Despachos.Models;

/// <summary>
/// Equivalencia de comuna: texto tal como viene de SAP (mal digitado o no oficial, ej. "CIUDAD SATELITE") y la
/// comuna del catálogo a la que corresponde. Se aprende al corregir la comuna de un documento y se mantiene en
/// Configuración > Equivalencias de comuna. <see cref="NormalizedText"/> (CommuneNames.Normalize) es la llave.
/// </summary>
public class CommuneAlias : AuditedEntity
{
    public string Text { get; set; } = "";
    public string NormalizedText { get; set; } = "";
    public long CommuneId { get; set; }
}
