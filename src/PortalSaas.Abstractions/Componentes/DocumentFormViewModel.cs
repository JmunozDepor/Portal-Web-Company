namespace PortalSaas.Abstractions.Componentes;

/// <summary>
/// Modelo del chrome compartido "documento con tabs" estilo SAP B1 Web Client (header +
/// botones de tab que anclan a cada sección) -- portado de PortalSAP_v2
/// (DocumentFormViewModel), consumido vía DocumentFormViewComponent
/// (PortalSaas.Host.ViewComponents). Cada tab es una vista parcial que el propio plugin
/// posee -- el componente solo arma el chrome, nunca el contenido de una tab.
/// LogisticsView/AccountingView/UserFieldsView en null ocultan esa tab -- no es un hack,
/// es el mecanismo previsto para un documento que todavía no necesita esos campos (ver
/// CLAUDE.md, primera entrega de Modulo.Ventas: solo General + Contenido).
/// </summary>
public sealed class DocumentFormViewModel
{
    public required string Title { get; init; }

    public bool ReadOnly { get; init; }

    public string? BackUrl { get; init; }

    public string? StatusText { get; init; }

    public string? StatusClass { get; init; }

    /// <summary>El PageModel del plugin -- se pasa tal cual a cada partial de tab.</summary>
    public required object Model { get; init; }

    public required string GeneralView { get; init; }

    public required string ContentView { get; init; }

    public string? LogisticsView { get; init; }

    public string? AccountingView { get; init; }

    public string? UserFieldsView { get; init; }

    public string AccountingTitle { get; init; } = "Contabilidad";
}
