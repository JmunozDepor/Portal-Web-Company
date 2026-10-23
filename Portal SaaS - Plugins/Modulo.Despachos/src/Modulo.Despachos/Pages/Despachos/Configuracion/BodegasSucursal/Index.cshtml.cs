using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.BodegasSucursal;

/// <summary>
/// Mantenedor de bodegas sucursal (Fase B): solo las transferencias a estas bodegas llevan guía de traslado. El código
/// se sugiere desde SAP (consulta «Bodegas»); dirección y comuna vacías se completan con las de SAP.
/// Menú "bodegas-sucursal" (permisos: Despachos.bodegas-sucursal).
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;
    private readonly ISapDocumentLookupService _sap;

    public IndexModel(IMasterDataService data, ISapDocumentLookupService sap, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
        _sap = sap;
    }

    protected override string MenuCode => "bodegas-sucursal";

    public IReadOnlyList<BranchWarehouse> Items { get; private set; } = Array.Empty<BranchWarehouse>();

    /// <summary>Bodegas de SAP para sugerir el código (vacío si la consulta no está disponible).</summary>
    public IReadOnlyList<SapWarehouse> SapWarehouses { get; private set; } = Array.Empty<SapWarehouse>();

    [BindProperty]
    public BodegaInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListBranchWarehousesAsync(includeInactive: true);
        try { SapWarehouses = await _sap.ListSapWarehousesAsync(); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
    {
        var code = (Input.WarehouseCode ?? "").Trim().ToUpperInvariant();
        string? address = NullIfBlank(Input.Address), commune = NullIfBlank(Input.CommuneName);
        if (address is null || commune is null)
        {
            SapWarehouse? w = null;
            try { w = (await _sap.ListSapWarehousesAsync()).FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)); }
            catch (InvalidOperationException) { /* sin consulta de bodegas: se guarda lo ingresado */ }
            address ??= w?.Address;
            commune ??= w?.Commune;
        }
        await _data.SaveBranchWarehouseAsync(new BranchWarehouse
        {
            Id = Input.Id, WarehouseCode = code, Name = Input.Name ?? "", Address = address, CommuneName = commune,
            IsActive = Input.IsActive,
        });
    }, Input.Id == 0 ? "Bodega sucursal creada." : "Bodega sucursal actualizada.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteBranchWarehouseAsync(id), "Bodega sucursal eliminada.");

    private static string? NullIfBlank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public sealed class BodegaInput
    {
        public long Id { get; set; }
        public string? WarehouseCode { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? CommuneName { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
