namespace Modulo.Despachos.Models;

/// <summary>Línea de detalle del costo calculado (base por localidad o recargo), con su explicación legible.</summary>
public class ManifestCostLine
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManifestId { get; set; }

    /// <summary>"base" o "surcharge".</summary>
    public string Kind { get; set; } = "base";
    public long? CommuneId { get; set; }
    public long? RateCardId { get; set; }
    public long? RateSurchargeId { get; set; }
    public decimal Amount { get; set; }
    public string Explanation { get; set; } = "";

    // Conteos de la localidad (solo líneas base).
    public int Documents { get; set; }
    public decimal Units { get; set; }
    public int Packages { get; set; }
}
