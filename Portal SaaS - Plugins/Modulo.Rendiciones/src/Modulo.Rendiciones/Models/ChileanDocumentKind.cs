namespace Modulo.Rendiciones.Models;

/// <summary>
/// Mapa entre el tipo de documento tributario chileno leído por OCR (un slug estable
/// que devuelve el modelo, o una palabra clave del encabezado impreso) y el código SII
/// del DTE. El código SII es el que se guarda en <see cref="DocumentType.SiiCode"/>,
/// así el mapeo a la fila de catálogo de la compañía es estable aunque el nombre
/// cambie ("Boleta" vs "Boleta Electrónica" vs "BOLETA").
/// </summary>
public static class ChileanDocumentKind
{
    public const int FacturaAfecta = 33;
    public const int FacturaExenta = 34;
    public const int BoletaAfecta = 39;
    public const int BoletaExenta = 41;
    public const int NotaCredito = 61;
    public const int NotaDebito = 56;
    public const int GuiaDespacho = 52;

    /// <summary>Slugs que se le piden al modelo de OCR (enum cerrado del responseSchema).</summary>
    public static readonly IReadOnlyList<string> Slugs = new[]
    {
        "factura_electronica", "factura_exenta", "boleta_electronica",
        "boleta_exenta", "nota_credito", "nota_debito", "guia_despacho", "otro",
    };

    /// <summary>slug del OCR -&gt; código SII (null si "otro" o no reconocido).</summary>
    public static int? SiiCodeForSlug(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "factura_electronica" or "factura_afecta" or "factura" => FacturaAfecta,
        "factura_exenta" => FacturaExenta,
        "boleta_electronica" or "boleta_afecta" or "boleta" => BoletaAfecta,
        "boleta_exenta" or "boleta_no_afecta" => BoletaExenta,
        "nota_credito" or "nota_de_credito" => NotaCredito,
        "nota_debito" or "nota_de_debito" => NotaDebito,
        "guia_despacho" or "guia_de_despacho" => GuiaDespacho,
        _ => null,
    };

    /// <summary>
    /// Heurística de respaldo para el extractor de Azure (que no clasifica): busca el
    /// tipo en el texto plano del comprobante. Devuelve el código SII o null.
    /// </summary>
    public static int? SiiCodeFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var t = text.ToUpperInvariant();
        if (t.Contains("NOTA DE CREDITO") || t.Contains("NOTA DE CRÉDITO")) return NotaCredito;
        if (t.Contains("NOTA DE DEBITO") || t.Contains("NOTA DE DÉBITO")) return NotaDebito;
        if (t.Contains("GUIA DE DESPACHO") || t.Contains("GUÍA DE DESPACHO")) return GuiaDespacho;

        var esExenta = t.Contains("EXENT") || t.Contains("NO AFECT");
        if (t.Contains("FACTURA")) return esExenta ? FacturaExenta : FacturaAfecta;
        if (t.Contains("BOLETA")) return esExenta ? BoletaExenta : BoletaAfecta;
        return null;
    }

    /// <summary>True si el tipo lleva IVA (afecto) -- para derivar neto/IVA cuando el comprobante no los desglosa.</summary>
    public static bool IsAfecto(int? siiCode) => siiCode is FacturaAfecta or BoletaAfecta or NotaCredito or NotaDebito;
}
