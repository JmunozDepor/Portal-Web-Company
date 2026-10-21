namespace Modulo.Despachos.Dominio.Tarifas;

/// <summary>Status: ok | no_rate | ambiguous. Total solo existe cuando Status = ok.</summary>
public record CostResult(string Status, decimal? Total, decimal BaseAmount, IReadOnlyList<CostLine> Lines);

/// <summary>Kind: base | surcharge.</summary>
public record CostLine(string Kind, long? CommuneId, long? RateCardId, long? RateSurchargeId, decimal Amount, string Explanation);
