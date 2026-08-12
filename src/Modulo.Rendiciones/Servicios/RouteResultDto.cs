namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Resultado de calcular la distancia de ruta entre dos direcciones. DistanceKm nulo
/// implica Error seteado -- nunca ambos nulos ni ambos con valor.
/// </summary>
public sealed record RouteResultDto(decimal? DistanceKm, string? Error);
