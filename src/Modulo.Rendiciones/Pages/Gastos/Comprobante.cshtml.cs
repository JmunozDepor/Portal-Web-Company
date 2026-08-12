using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Visor con zoom/rotar/arrastrar para imágenes, o &lt;iframe&gt; (visor nativo del
/// navegador, ya trae zoom) para PDF. La misma autorización que ComprobanteArchivo,
/// que es de donde en realidad sale el archivo (esta página solo arma el "marco"
/// alrededor). Layout = null: se abre en pestaña nueva, sin sidebar/topbar del portal
/// -- más lugar para la imagen, como cualquier visor de documentos dedicado.
/// </summary>
public sealed class ComprobanteModel : ComprobanteAccesoBase
{
    public ComprobanteModel(IExpenseService expenses, IExpenseApprovalGroupService groups,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(expenses, groups, currentUser, currentCompany)
    {
    }

    public long ExpenseId { get; private set; }
    public string? FileName { get; private set; }
    public bool IsPdf { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var receipt = await GetAuthorizedReceiptAsync(id, ct);
        if (receipt is null)
            return NotFound();

        ExpenseId = id;
        FileName = receipt.FileName;
        IsPdf = receipt.MimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        return Page();
    }
}
