using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Manifiestos;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.AgruparTraslados;

/// <summary>Detalle de una agrupación: transferencias, agregar LPN / quitar (EDIT), anular (DELETE) e imprimir la hoja (VIEW).</summary>
public sealed class DetalleModel : DespachosPageModelBase
{
    private readonly IDispatchGroupService _groups;
    private readonly IDispatchGroupPdfService _pdf;

    public DetalleModel(IDispatchGroupService groups, IDispatchGroupPdfService pdf, ICurrentUserContext currentUser) : base(currentUser)
    {
        _groups = groups;
        _pdf = pdf;
    }

    protected override string MenuCode => "agrupar-traslados";

    public DispatchGroup G { get; private set; } = null!;
    public string Code => DispatchGroupCode.Format(G.Number);
    public bool IsArmed => G.Status == DispatchGroupStatuses.Armed;
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        // Las agrupaciones de Entregas (EG-) se abren en Entregas con guía previa.
        if (await _groups.GetAsync(id) is not { Kind: DispatchGroupKinds.Transfer } g) return NotFound();
        G = g;
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        return Page();
    }

    public async Task<IActionResult> OnPostAgregarLpnAsync(long id, string? lpn)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try { SuccessMessage = await _groups.AddLpnAsync(id, lpn ?? ""); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostQuitarAsync(long id, long transferId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _groups.RemoveTransferAsync(id, transferId);
            SuccessMessage = "Transferencia quitada de la agrupación.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAnularAsync(long id, string? reason)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            await _groups.AnnulAsync(id, reason ?? "");
            SuccessMessage = "Agrupación anulada; sus transferencias quedaron libres.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetPdfAsync(long id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            if (await _groups.GetAsync(id) is not { Kind: DispatchGroupKinds.Transfer } g) return NotFound();
            return new InlinePdfResult(await _pdf.RenderAsync(new[] { id }), $"{DispatchGroupCode.Format(g.Number)}.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }
    }
}
