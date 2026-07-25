using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Componentes;

namespace PortalSaas.Host.ViewComponents;

/// <summary>
/// Renderiza el chrome compartido "listado de documentos" (filtros + tabla +
/// paginación) -- invocado vía @await Component.InvokeAsync("DocumentList", modelo).
/// Ver DocumentListViewModel para el contrato completo.
/// </summary>
public sealed class DocumentListViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(DocumentListViewModel model) => View(model);
}
