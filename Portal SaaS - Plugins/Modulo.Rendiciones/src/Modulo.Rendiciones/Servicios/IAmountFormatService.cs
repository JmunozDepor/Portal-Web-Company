namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Formatea montos en plata según la preferencia de la compañía (RendicionesSettings.
/// ShowAmountDecimals) -- por defecto SIN decimales (la mayoría de las empresas
/// chilenas no factura con centavos de CLP), configurable en Configuración &gt; Formato
/// de Montos. Lo consume <see cref="Modulo.Rendiciones.TagHelpers.AmountTagHelper"/>
/// (&lt;rnd-amount&gt;) en las vistas, así ningún .cshtml llama a ToString("N0"/"N2")
/// a mano -- un solo lugar decide cuántos decimales se ven.
/// </summary>
public interface IAmountFormatService
{
    /// <summary>true = con decimales (2), false (default) = redondeado, sin decimales.</summary>
    Task<bool> ShowDecimalsAsync(CancellationToken ct = default);

    Task<string> FormatAsync(decimal amount, CancellationToken ct = default);
}
