using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Pages.Despachos.Manifiestos;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.GuiasManuales;

/// <summary>
/// Detalle de una guía manual: datos, detalle y observaciones; emitir sola (CREATE), anular un borrador (DELETE),
/// PDF borrador y emitido y el código GM- para imprimir y escanear en el manifiesto (VIEW).
/// </summary>
public sealed class DetalleModel : DespachosPageModelBase
{
    private readonly IManualGuideService _guides;

    public DetalleModel(IManualGuideService guides, ICurrentUserContext currentUser) : base(currentUser)
    {
        _guides = guides;
    }

    protected override string MenuCode => "guias-manuales";

    public TransportGuide G { get; private set; } = null!;
    public ManualGuideInput Input { get; private set; } = null!;
    public string Code => ManualGuideCode.Format(G.Number ?? 0);
    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    /// <summary>Editable: borrador, en manifiesto o rechazada.</summary>
    public bool IsEditable => G.Status is TransportGuideStatuses.Draft or TransportGuideStatuses.InManifest or TransportGuideStatuses.Failed;

    /// <summary>Se puede emitir sola: no está en un manifiesto ni emitida/anulada.</summary>
    public bool CanEmitNow => G.Status is TransportGuideStatuses.Draft or TransportGuideStatuses.Failed or TransportGuideStatuses.Pending;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        var g = await _guides.GetAsync(id);
        if (g is null) return NotFound();
        G = g;
        Input = await _guides.GetInputAsync(id);
        CanCreate = await HasActionAsync(PortalActions.Create);
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        return Page();
    }

    public async Task<IActionResult> OnPostEmitirAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        try
        {
            var g = await _guides.EmitAsync(id);
            if (g.Status == TransportGuideStatuses.Issued) SuccessMessage = $"Guía emitida con folio {g.Folio}.";
            else if (g.Status == TransportGuideStatuses.Pending)
                WarningMessage = "El proveedor no confirmó la emisión; reintente en unos minutos (no se duplica el folio).";
            else ErrorMessage = $"El proveedor rechazó la guía: {g.LastError}";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAnularAsync(Guid id, string? reason)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            await _guides.AnnulAsync(id, reason ?? "");
            SuccessMessage = "Guía anulada.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetPdfAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var g = await _guides.GetAsync(id);
            if (g is null) return NotFound();
            return new InlinePdfResult(await _guides.GetPdfAsync(id), $"guia-{g.Folio}.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }
    }

    public async Task<IActionResult> OnGetBorradorPdfAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var g = await _guides.GetAsync(id);
            if (g is null) return NotFound();
            return new InlinePdfResult(await _guides.PreviewPdfAsync(id), $"{ManualGuideCode.Format(g.Number ?? 0)}-borrador.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }
    }

    /// <summary>Code128 del código GM- (para imprimirlo y escanearlo en el manifiesto).</summary>
    public async Task<IActionResult> OnGetCodigoAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        var g = await _guides.GetAsync(id);
        if (g is null) return NotFound();
        return File(BarcodeRenderer.Code128Png(ManualGuideCode.Format(g.Number ?? 0)), "image/png");
    }
}
