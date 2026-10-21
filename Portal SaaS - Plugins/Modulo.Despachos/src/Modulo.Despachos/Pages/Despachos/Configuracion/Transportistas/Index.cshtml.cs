using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Transportistas;

/// <summary>Mantenedor de transportistas. Menú "transportistas" (permisos: Despachos.transportistas).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "transportistas";

    public IReadOnlyList<Carrier> Items { get; private set; } = Array.Empty<Carrier>();

    [BindProperty]
    public TransportistaInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListCarriersAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveCarrierAsync(new Carrier
            {
                Id = Input.Id,
                Code = Input.Code ?? "",
                Name = Input.Name ?? "",
                TaxId = Input.TaxId,
                Kind = Input.Kind ?? "",
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Transportista creado." : "Transportista actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteCarrierAsync(id), "Transportista eliminado.");

    public sealed class TransportistaInput
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? TaxId { get; set; }
        public string? Kind { get; set; }
        public bool IsActive { get; set; }
    }
}
