using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteSettingsServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static (DteSettingsService Svc, PortalSaasDbContext Db, FakeDteProvider Provider) Build()
    {
        var db = DteTestSupport.Db();
        var provider = new FakeDteProvider();
        return (new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { provider })), db, provider);
    }

    [Fact]
    public async Task Save_CifraLaClave_YGetNoLaDevuelve()
    {
        var (svc, db, _) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());

        var dto = await svc.GetAsync(CompanyId);
        Assert.NotNull(dto);
        Assert.True(dto!.HasSecret);
        Assert.Equal("FAKE", dto.ProviderCode);
        var row = await db.CompanyDteSettings.SingleAsync();
        Assert.NotNull(row.SecretKey);
        Assert.DoesNotContain("clave-secreta", row.SecretKey);
    }

    [Fact]
    public async Task Save_ClaveVacia_ConservaLaAnterior()
    {
        var (svc, db, _) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());
        var before = (await db.CompanyDteSettings.SingleAsync()).SecretKey;

        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput(password: ""));
        Assert.Equal(before, (await db.CompanyDteSettings.SingleAsync()).SecretKey);
    }

    [Fact]
    public async Task Save_ProveedorDesconocido_Falla()
    {
        var (svc, _, _) = Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(CompanyId, DteTestSupport.ValidInput(provider: "NOEXISTE")));
        Assert.Contains("NOEXISTE", ex.Message);
    }

    [Fact]
    public async Task Save_HabilitarSinClave_Falla()
    {
        var (svc, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(CompanyId, DteTestSupport.ValidInput(password: null)));
    }

    [Fact]
    public async Task Save_RutEmisorInvalido_Falla()
    {
        var (svc, _, _) = Build();
        var input = DteTestSupport.ValidInput();
        input.IssuerTaxId = "76030680-1";
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveAsync(CompanyId, input));
    }

    [Fact]
    public async Task Delete_ConEmisionesPendientes_Falla()
    {
        var (svc, db, _) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());
        db.DteEmissions.Add(new DteEmission
        {
            CompanyId = CompanyId, EmissionKey = Guid.NewGuid(), SourceModule = "despachos", SourceRef = "x", DteType = 52,
            ProviderCode = "FAKE", IssuerTaxId = "76030680-0", Environment = "certification", Status = "folio_reserved",
            RequestHash = "h", RequestJson = "{}",
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(CompanyId));
        Assert.Equal(1, await db.CompanyDteSettings.CountAsync());
    }

    [Fact]
    public async Task Delete_SinPendientes_Borra()
    {
        var (svc, db, _) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());
        await svc.DeleteAsync(CompanyId);
        Assert.Equal(0, await db.CompanyDteSettings.CountAsync());
    }

    [Fact]
    public async Task Resolve_Deshabilitada_Null()
    {
        var (svc, _, _) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput(enabled: false));
        Assert.Null(await svc.ResolveAsync(CompanyId));
    }

    [Fact]
    public async Task Resolve_Habilitada_DescifraClave()
    {
        var (svc, _, provider) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());
        var r = await svc.ResolveAsync(CompanyId);
        Assert.NotNull(r);
        Assert.Equal("clave-secreta", r!.Value.Ctx.Password);
        Assert.Equal("76030680-0", r.Value.Issuer.TaxId);
        Assert.Same(provider, r.Value.Provider);
    }

    [Fact]
    public async Task Test_ErrorProveedor_MensajeSinCredenciales()
    {
        var (svc, _, provider) = Build();
        await svc.SaveAsync(CompanyId, DteTestSupport.ValidInput());
        provider.TestFails = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.TestAsync(CompanyId));
        Assert.DoesNotContain("clave-secreta", ex.Message);
        Assert.DoesNotContain("usuario-prueba", ex.Message);
    }
}
