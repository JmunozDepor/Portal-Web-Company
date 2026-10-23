using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteReconciliationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed record Env(PortalSaasDbContext Db, FakeDteProvider Provider, DteSettingsService Settings);

    private static Env Build()
    {
        var db = DteTestSupport.Db();
        var provider = new FakeDteProvider();
        return new Env(db, provider, new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { provider })));
    }

    private static DteEmissionService Svc(Env env, Guid companyId) =>
        new(env.Db, new FakeCompany { Id = companyId }, new FakeUser(), env.Settings, TimeProvider.System);

    /// <summary>Crea una emisión que quedó en folio_reserved (caída en la confirmación) y la envejece.</summary>
    private static async Task<Guid> StuckAsync(Env env, Guid companyId, int minutesAgo, bool failReserve = false)
    {
        if (failReserve) env.Provider.FailNextReserves = 1; else env.Provider.FailNextConfirms = 1;
        var key = Guid.NewGuid();
        await Svc(env, companyId).EmitAsync(new DteEmissionRequest(key, "despachos", "m:1/g:1", DteRulesTests.Guia52Valida()));
        var row = await env.Db.DteEmissions.SingleAsync(x => x.EmissionKey == key);
        row.UpdatedAt = Now.AddMinutes(-minutesAgo);
        await env.Db.SaveChangesAsync();
        return key;
    }

    private static Task<int> RunAsync(Env env, Guid companyId) =>
        DteReconciliationHostedService.ReconcileCompanyAsync(Svc(env, companyId), env.Db, companyId, Now, NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task Reconciler_FolioReservadoViejo_Confirma()
    {
        var env = Build();
        var company = Guid.NewGuid();
        await env.Settings.SaveAsync(company, DteTestSupport.ValidInput());
        var key = await StuckAsync(env, company, minutesAgo: 30);

        Assert.Equal(1, await RunAsync(env, company));
        Assert.Equal(DteEmissionStatuses.Issued, (await env.Db.DteEmissions.SingleAsync(x => x.EmissionKey == key)).Status);
        Assert.Equal(1, env.Provider.Reserves);
    }

    [Fact]
    public async Task Reconciler_PendienteSinFolio_Reserva()
    {
        var env = Build();
        var company = Guid.NewGuid();
        await env.Settings.SaveAsync(company, DteTestSupport.ValidInput());
        var key = await StuckAsync(env, company, minutesAgo: 30, failReserve: true);

        Assert.Equal(1, await RunAsync(env, company));
        Assert.Equal(DteEmissionStatuses.Issued, (await env.Db.DteEmissions.SingleAsync(x => x.EmissionKey == key)).Status);
    }

    [Fact]
    public async Task Reconciler_FilaReciente_NoSeToca()
    {
        var env = Build();
        var company = Guid.NewGuid();
        await env.Settings.SaveAsync(company, DteTestSupport.ValidInput());
        var key = await StuckAsync(env, company, minutesAgo: 2);

        Assert.Equal(0, await RunAsync(env, company));
        Assert.Equal(DteEmissionStatuses.FolioReserved, (await env.Db.DteEmissions.SingleAsync(x => x.EmissionKey == key)).Status);
    }

    [Fact]
    public async Task Reconciler_Failed_NoSeToca()
    {
        var env = Build();
        var company = Guid.NewGuid();
        await env.Settings.SaveAsync(company, DteTestSupport.ValidInput());
        env.Provider.RejectNextReserve = "Error de esquema";
        var key = Guid.NewGuid();
        await Svc(env, company).EmitAsync(new DteEmissionRequest(key, "despachos", "m:1/g:1", DteRulesTests.Guia52Valida()));
        var row = await env.Db.DteEmissions.SingleAsync();
        row.UpdatedAt = Now.AddHours(-1);
        await env.Db.SaveChangesAsync();
        var reservesBefore = env.Provider.Reserves;

        Assert.Equal(0, await RunAsync(env, company));
        Assert.Equal(reservesBefore, env.Provider.Reserves);
        Assert.Equal(DteEmissionStatuses.Failed, (await env.Db.DteEmissions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reconciler_UsaConfiguracionDeCadaCompania()
    {
        var env = Build();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await env.Settings.SaveAsync(a, DteTestSupport.ValidInput(baseUrl: "https://proveedor-a.example.cl/ws"));
        await env.Settings.SaveAsync(b, DteTestSupport.ValidInput(baseUrl: "https://proveedor-b.example.cl/ws"));
        await StuckAsync(env, a, minutesAgo: 30);
        await StuckAsync(env, b, minutesAgo: 30);

        env.Provider.BaseUrls.Clear();
        Assert.Equal(1, await RunAsync(env, a));
        Assert.All(env.Provider.BaseUrls, u => Assert.Equal("https://proveedor-a.example.cl/ws", u));

        env.Provider.BaseUrls.Clear();
        Assert.Equal(1, await RunAsync(env, b));
        Assert.All(env.Provider.BaseUrls, u => Assert.Equal("https://proveedor-b.example.cl/ws", u));
        Assert.Equal(2, await env.Db.DteEmissions.CountAsync(x => x.Status == DteEmissionStatuses.Issued));
    }

    [Fact]
    public async Task Reconciler_TopeDeIntentos_NoSigue()
    {
        var env = Build();
        var company = Guid.NewGuid();
        await env.Settings.SaveAsync(company, DteTestSupport.ValidInput());
        var key = await StuckAsync(env, company, minutesAgo: 30, failReserve: true);
        var row = await env.Db.DteEmissions.SingleAsync(x => x.EmissionKey == key);
        row.Attempts = DteReconciliationHostedService.MaxAttempts;
        await env.Db.SaveChangesAsync();

        Assert.Equal(0, await RunAsync(env, company));
    }
}
