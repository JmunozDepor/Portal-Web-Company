using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>
/// Cabecera de un manifiesto nuevo. Al guardar se crea el borrador y se continúa en el detalle,
/// pestaña Documentos, donde vive el escaneo. Menú "manifiestos" (acción CREATE).
/// </summary>
public sealed class NuevoModel : DespachosPageModelBase
{
    private readonly IManifestService _manifests;
    private readonly IMasterDataService _data;

    public NuevoModel(IManifestService manifests, IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _manifests = manifests;
        _data = data;
    }

    protected override string MenuCode => "manifiestos";

    public ManifestLookups Lookups { get; private set; } = null!;

    [BindProperty]
    public ManifestForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        Lookups = await ManifestLookups.LoadAsync(_data);
        Input.DispatchDate = ChileClock.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        try
        {
            var id = await _manifests.CreateDraftAsync(Input.ToInput());
            SuccessMessage = "Borrador creado. Escanee los documentos del manifiesto.";
            return RedirectToPage("Detalle", new { id });
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Lookups = await ManifestLookups.LoadAsync(_data);
            return Page();
        }
    }
}
