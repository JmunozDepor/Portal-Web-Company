using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.TiposVehiculo;

/// <summary>Mantenedor de tipos de vehículo. Menú "tipos-vehiculo" (permisos: Despachos.tipos-vehiculo).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "tipos-vehiculo";

    public IReadOnlyList<VehicleType> Items { get; private set; } = Array.Empty<VehicleType>();

    [BindProperty]
    public TipoVehiculoInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListVehicleTypesAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveVehicleTypeAsync(new VehicleType
            {
                Id = Input.Id,
                Code = Input.Code ?? "",
                Name = Input.Name ?? "",
                CapacityM3 = ParseDecimal(Input.CapacityM3, "La capacidad en m³"),
                CapacityKg = ParseDecimal(Input.CapacityKg, "La capacidad en kg"),
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Tipo de vehículo creado." : "Tipo de vehículo actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteVehicleTypeAsync(id), "Tipo de vehículo eliminado.");

    public sealed class TipoVehiculoInput
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? CapacityM3 { get; set; }
        public string? CapacityKg { get; set; }
        public bool IsActive { get; set; }
    }
}
