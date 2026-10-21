namespace Modulo.Despachos.Dominio;

/// <summary>Patente en forma canónica: mayúsculas, sin espacios, guiones ni puntos.</summary>
public static class PlateNormalizer
{
    public static string Normalize(string plate) =>
        new(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
