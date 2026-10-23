using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;
using Bodegas = Modulo.Despachos.Pages.Despachos.Configuracion.BodegasSucursal;

namespace Modulo.Despachos.Tests.Pages;

public class BodegasSucursalPageTests
{
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static (Bodegas.IndexModel Page, MasterDataService Svc, FakeSapDocumentLookupService Sap) Build(FakeCurrentUserContext user)
    {
        var svc = new MasterDataService(TestDb.Create(), new FakeCurrentCompanyAccessor(), user);
        var sap = new FakeSapDocumentLookupService();
        var page = new Bodegas.IndexModel(svc, sap, user)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider()),
        };
        return (page, svc, sap);
    }

    [Fact]
    public async Task BodegasSucursal_Guardar_SinDireccion_CompletaDesdeSap()
    {
        var (page, svc, sap) = Build(new FakeCurrentUserContext().Grant("Despachos.bodegas-sucursal", PortalActions.View, PortalActions.Create));
        sap.Warehouses.Add(new SapWarehouse("TDA01", "Tienda Estado", "Estado 387", "Santiago"));
        page.Input = new Bodegas.IndexModel.BodegaInput { WarehouseCode = "tda01", Name = "Tienda Estado", IsActive = true };

        await page.OnPostSaveAsync();

        var b = Assert.Single(await svc.ListBranchWarehousesAsync());
        Assert.Equal(("TDA01", "Estado 387", "Santiago"), (b.WarehouseCode, b.Address, b.CommuneName));
    }

    [Fact]
    public async Task BodegasSucursal_GuardarSinMarcarActiva_QuedaInactiva()
    {
        var (page, svc, _) = Build(new FakeCurrentUserContext().Grant("Despachos.bodegas-sucursal", PortalActions.View, PortalActions.Create, PortalActions.Edit));
        var id = await svc.SaveBranchWarehouseAsync(new Models.BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda", Address = "A", CommuneName = "Santiago" });
        // Checkbox desmarcado: el formulario no envía Input.IsActive.
        page.Input = new Bodegas.IndexModel.BodegaInput { Id = id, WarehouseCode = "TDA01", Name = "Tienda", Address = "A", CommuneName = "Santiago" };

        await page.OnPostSaveAsync();

        Assert.False((await svc.FindBranchWarehouseAsync("TDA01"))!.IsActive);
    }

    [Fact]
    public async Task BodegasSucursal_SapNoResponde_LaPaginaCargaIgual()
    {
        var (page, _, sap) = Build(new FakeCurrentUserContext().Grant("Despachos.bodegas-sucursal", PortalActions.View));
        sap.WarehousesError = "Configure la consulta SAP «Bodegas»";

        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.Empty(page.SapWarehouses);
        Assert.Contains("Bodegas", page.ErrorMessage);
    }

    [Fact]
    public async Task BodegasSucursal_SinPermisoView_Forbid()
    {
        var (page, _, _) = Build(new FakeCurrentUserContext());
        Assert.IsType<ForbidResult>(await page.OnGetAsync());
    }
}
