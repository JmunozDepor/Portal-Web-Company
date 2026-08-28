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

        return File(receipt.Content, receipt.MimeType, receipt.FileName);
    }
}
