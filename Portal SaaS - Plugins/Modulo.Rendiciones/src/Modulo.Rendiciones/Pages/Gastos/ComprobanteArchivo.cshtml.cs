using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Devuelve el archivo crudo del comprobante -- página "sin vista" (el .cshtml nunca
/// se renderiza, OnGetAsync corta con un FileResult). Es el "src"/"href" que consume
/// Pages/Gastos/Comprobante (el visor con zoom/rotar) o un &lt;iframe&gt; para PDF.
/// </summary>
public sealed class ComprobanteArchivoModel : ComprobanteAccesoBase
{
    public ComprobanteArchivoModel(IExpenseService expenses, IExpenseApprovalGroupService groups,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(expenses, groups, currentUser, currentCompany)
    {
    }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var receipt = await GetAuthorizedReceiptAsync(id, ct);
        if (receipt is null)
            return NotFound();

        // OJO: File(bytes, mime, fileDownloadName) manda Content-Disposition: attachment.
        // Eso rompe el <iframe>/<img> del visor -- un PDF con ese header no se renderiza
        // adentro del iframe, Chrome dispara la descarga del archivo solo y el visor queda
        // con la pantalla en negro (bug real reportado). "inline" deja que el navegador lo
        // muestre en el lugar donde se lo pida; el botón Descargar ya fuerza la bajada del
        // lado del cliente con el atributo HTML5 download=, así que no se pierde esa opción.
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(receipt.FileName)}\"";
        return File(receipt.Content, receipt.MimeType);
    }
}
