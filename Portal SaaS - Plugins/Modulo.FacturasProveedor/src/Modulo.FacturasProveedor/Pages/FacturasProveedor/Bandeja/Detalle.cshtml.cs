using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Bandeja;

/// <summary>Detalle de una factura: resumen, evaluación de reglas, líneas e historial; aceptar, rechazar y asociar OC.</summary>
public sealed class DetalleModel : FacturasProveedorPageModelBase
{
    private readonly IReviewQueryService _queries;
    private readonly IReviewDecisionService _decisions;
    private readonly IDteInboxService _inbox;

    public DetalleModel(IReviewQueryService queries, IReviewDecisionService decisions, IDteInboxService inbox, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _queries = queries;
        _decisions = decisions;
        _inbox = inbox;
    }

    protected override string MenuCode => "bandeja";

    public ReviewDetail? Detail { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        Detail = await _queries.GetDetailAsync(id, ct);
        return Detail is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(long id, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        var detail = await _queries.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();
        var pdf = await _inbox.GetPdfAsync(detail.Row.ReceivedId, ct);
        return pdf is null ? NotFound() : File(pdf, "application/pdf");
    }

    public async Task<IActionResult> OnPostAceptarAsync(long id, Guid stamp, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        return await RunAsync(id, () => _decisions.DecideAsync(id, RuleActions.Accept, null, null, stamp, ct), "Factura aceptada.");
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, Guid stamp, string? motivo, string? tipoReclamo, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        return await RunAsync(id, () => _decisions.DecideAsync(id, RuleActions.Reject, motivo, tipoReclamo, stamp, ct), "Factura rechazada.");
    }

    public async Task<IActionResult> OnPostAsociarOcAsync(long id, int docNum, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        return await RunAsync(id, () => _decisions.AssociatePurchaseOrderAsync(id, docNum, ct), $"OC {docNum} asociada.");
    }

    private async Task<IActionResult> RunAsync(long id, Func<Task> action, string success)
    {
        try
        {
            await action();
            SuccessMessage = success;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage(new { id });
    }
}
