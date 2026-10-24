using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.MotivosTraslado;

/// <summary>
/// Motivos de traslado (SII IndTraslado) habilitados para guías manuales y si llevan IVA. Menú "motivos-traslado"
/// (permisos: Despachos.motivos-traslado). Sin eliminar: un motivo en desuso se desactiva.
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IManualGuideService _guides;

    public IndexModel(IManualGuideService guides, ICurrentUserContext currentUser) : base(currentUser)
    {
        _guides = guides;
    }

    protected override string MenuCode => "motivos-traslado";

    public IReadOnlyList<TransferMotive> Items { get; private set; } = Array.Empty<TransferMotive>();

    [BindProperty]
    public MotivoInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _guides.ListMotivesAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, () =>
        _guides.SaveMotiveAsync(new TransferMotive
        {
            Id = Input.Id, Code = Input.Code, Name = Input.Name ?? "", VatAffected = Input.VatAffected, IsActive = Input.IsActive,
        }),
        Input.Id == 0 ? "Motivo creado." : "Motivo actualizado.");

    public sealed class MotivoInput
    {
        public long Id { get; set; }
        public int Code { get; set; }
        public string? Name { get; set; }
        public bool VatAffected { get; set; }
        public bool IsActive { get; set; }
    }
}
