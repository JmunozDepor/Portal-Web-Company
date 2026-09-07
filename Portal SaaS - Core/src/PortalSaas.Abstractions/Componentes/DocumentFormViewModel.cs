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

    /// <summary>
    /// DocEntry real del documento -- necesario para postear Cerrar/Cancelar
    /// (asp-route-id) desde el chrome compartido, sin que cada plugin repita el botón.
    /// Null en un documento nuevo (todavía no tiene DocEntry).
    /// </summary>
    public int? DocEntry { get; init; }

    /// <summary>
    /// Habilita el botón Cerrar -- mismo gate que "Crear" (CanCreateAsync del tipo) más
    /// el permiso Eliminar del usuario sobre este menú, portado de PuedeCerrarOCancelar
    /// (DetalleGenericoVentaModelBase, referencia-original/PortalSAP_v2). Solo tiene
    /// sentido en un documento existente que no esté Cerrado.
    /// </summary>
    public bool CanClose { get; init; }

    /// <summary>
    /// Habilita el botón Cancelar -- mismo gate que CanClose, MÁS que el tipo de
    /// documento soporte la acción (ver *DocumentTypeCatalog.Entry.SupportsCancel):
    /// Service Layer rechaza "/Cancel" sobre documentos de intención (Orden/Solicitud)
    /// con "The requested action is not supported for this object" -- error real
    /// confirmado 2026-09-04 cancelando una Solicitud de Traslado.
    /// </summary>
    public bool CanCancel { get; init; }
}
