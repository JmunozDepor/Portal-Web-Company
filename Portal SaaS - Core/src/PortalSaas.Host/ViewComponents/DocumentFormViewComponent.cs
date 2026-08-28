using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Componentes;

namespace PortalSaas.Host.ViewComponents;

/// <summary>
/// Renderiza el chrome compartido "documento con tabs" (header + botones de tab) --
/// invocado vía @await Component.InvokeAsync("DocumentForm", modelo). Ver
/// DocumentFormViewModel para el contrato completo -- el contenido de cada tab lo
/// posee el plugin, este componente solo arma el marco.
/// </summary>
public sealed class DocumentFormViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(DocumentFormViewModel model) => View(model);
}
