using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Manifiestos;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.EntregasGuiaPrevia;

/// <summary>
/// Detalle de una agrupación EG-: Entregas, condiciones de la guía, agregar LPN / quitar / cambiar condiciones (EDIT),
/// anular (DELETE) e imprimir la hoja (VIEW).
/// </summary>
public sealed class DetalleModel : DespachosPageModelBase
{
    private readonly IDispatchGroupService _groups;
    private readonly IDeliveryGroupService _deliveries;
    private readonly IDispatchGroupPdfService _pdf;

    public DetalleModel(IDispatchGroupService groups, IDeliveryGroupService deliveries, IDispatchGroupPdfService pdf,
        ICurrentUserContext currentUser) : base(currentUser)
    {
        _groups = groups;
        _deliveries = deliveries;
        _pdf = pdf;
    }

    protected override string MenuCode => "entregas-guia-previa";

    public DispatchGroup G { get; private set; } = null!;
    public string Code => DispatchGroupCode.Format(G.Number, G.Kind);
    public bool IsArmed => G.Status == DispatchGroupStatuses.Armed;
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        if (await LoadAsync(id) is not { } g) return NotFound();
        G = g;
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        return Page();
    }

    /// <summary>Agrupación de Entregas (las de traslados se abren en Agrupar traslados).</summary>
    private async Task<DispatchGroup?> LoadAsync(long id) =>
        await _groups.GetAsync(id) is { Kind: DispatchGroupKinds.Delivery } g ? g : null;

    public async Task<IActionResult> OnPostAgregarLpnAsync(long id, string? lpn)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try { SuccessMessage = await _deliveries.AddLpnAsync(id, lpn ?? ""); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostQuitarAsync(long id, long lineId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _deliveries.RemoveDeliveryAsync(id, lineId);
            SuccessMessage = "Entrega quitada de la agrupación.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCondicionesAsync(long id, int transferIndicator, int dispatchType)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _deliveries.SetTermsAsync(id, transferIndicator, dispatchType);
            SuccessMessage = "Condiciones de la guía actualizadas.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAnularAsync(long id, string? reason)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            await _deliveries.AnnulAsync(id, reason ?? "");
            SuccessMessage = "Agrupación anulada; sus Entregas quedaron libres.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetPdfAsync(long id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            if (await LoadAsync(id) is not { } g) return NotFound();
            return new InlinePdfResult(await _pdf.RenderAsync(new[] { id }), $"{DispatchGroupCode.Format(g.Number, g.Kind)}.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }
    }
}
