using Microsoft.AspNetCore.Razor.TagHelpers;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.TagHelpers;

/// <summary>
/// &lt;rnd-amount value="@d.Amount" currency="@d.Currency" /&gt; -- reemplaza a mano
/// @d.Amount.ToString("N2") en las vistas. Formatea según la preferencia de la compañía
/// (ver IAmountFormatService): un solo lugar decide si se ven decimales o no, en vez de
/// repetir el criterio en cada .cshtml. No es un partial -- es un tag helper (clase
/// compilada normal, sin resolución de vista en runtime), seguro para este plugin
/// cargado por AssemblyLoadContext.
/// </summary>
[HtmlTargetElement("rnd-amount")]
public sealed class AmountTagHelper : TagHelper
{
    private readonly IAmountFormatService _format;

    public AmountTagHelper(IAmountFormatService format)
    {
        _format = format;
    }

    /// <summary>Monto a mostrar. Null -&gt; se usa <see cref="Empty"/>.</summary>
    public decimal? Value { get; set; }

    /// <summary>Prefijo opcional (ej. "CLP") antes del monto formateado.</summary>
    public string? Currency { get; set; }

    /// <summary>Qué mostrar cuando Value es null.</summary>
    public string Empty { get; set; } = "—";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null; // sin elemento envolvente -- solo el texto formateado

        if (Value is not { } value)
        {
            output.Content.SetContent(Empty);
            return;
        }

        var formatted = await _format.FormatAsync(value);
        output.Content.SetContent(string.IsNullOrWhiteSpace(Currency) ? formatted : $"{Currency} {formatted}");
    }
}
