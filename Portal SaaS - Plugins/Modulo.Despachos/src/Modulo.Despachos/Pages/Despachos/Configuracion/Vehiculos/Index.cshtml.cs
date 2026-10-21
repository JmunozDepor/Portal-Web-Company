using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Vehiculos;

/// <summary>Mantenedor de vehículos. Menú "vehiculos" (permisos: Despachos.vehiculos).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "vehiculos";

    public IReadOnlyList<Vehicle> Items { get; private set; } = Array.Empty<Vehicle>();
    public IReadOnlyList<VehicleType> Types { get; private set; } = Array.Empty<VehicleType>();
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();

    public string TypeName(long? id) => Types.FirstOrDefault(t => t.Id == id)?.Name ?? "—";
    public string CarrierName(long? id) => Carriers.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    [BindProperty]
    public VehiculoInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListVehiclesAsync(includeInactive: true);
        Types = await _data.ListVehicleTypesAsync(includeInactive: true);
        Carriers = await _data.ListCarriersAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveVehicleAsync(new Vehicle
            {
                Id = Input.Id,
                Plate = Input.Plate ?? "",
                VehicleTypeId = Input.VehicleTypeId,
                CarrierId = Input.CarrierId,
                CapacityM3 = ParseDecimal(Input.CapacityM3, "La capacidad en m³"),
                IsTrailer = Input.IsTrailer,
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Vehículo creado." : "Vehículo actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteVehicleAsync(id), "Vehículo eliminado.");

    public sealed class VehiculoInput
    {
        public long Id { get; set; }
        public string? Plate { get; set; }
        public long? VehicleTypeId { get; set; }
        public long? CarrierId { get; set; }
        public string? CapacityM3 { get; set; }
        public bool IsTrailer { get; set; }
        public bool IsActive { get; set; }
    }
}
