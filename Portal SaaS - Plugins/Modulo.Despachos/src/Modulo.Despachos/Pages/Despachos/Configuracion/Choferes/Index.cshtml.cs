using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Choferes;

/// <summary>Mantenedor de choferes. Menú "choferes" (permisos: Despachos.choferes).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "choferes";

    public IReadOnlyList<Driver> Items { get; private set; } = Array.Empty<Driver>();
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();

    public string CarrierName(long? id) => Carriers.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    [BindProperty]
    public ChoferInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListDriversAsync(includeInactive: true);
        Carriers = await _data.ListCarriersAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveDriverAsync(new Driver
            {
                Id = Input.Id,
                TaxId = Input.TaxId ?? "",
                FullName = Input.FullName ?? "",
                CarrierId = Input.CarrierId,
                LicenseClass = Input.LicenseClass,
                LicenseExpiresAt = ParseDate(Input.LicenseExpiresAt, "La fecha de vencimiento"),
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Chofer creado." : "Chofer actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteDriverAsync(id), "Chofer eliminado.");

    public sealed class ChoferInput
    {
        public long Id { get; set; }
        public string? TaxId { get; set; }
        public string? FullName { get; set; }
        public long? CarrierId { get; set; }
        public string? LicenseClass { get; set; }
        public string? LicenseExpiresAt { get; set; }
        public bool IsActive { get; set; }
    }
}
