using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Dominio.Reglas;
using Modulo.FacturasProveedor.Tests.Fakes;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class ModuleSettingsServiceTests
{
    private sealed record Env(ModuleSettingsService Svc, FacturasProveedorDbContext Db, FakeCurrentCompanyAccessor Company, FakeCurrentUserContext User);

    private static Env Build()
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        var user = new FakeCurrentUserContext();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
        return new Env(new ModuleSettingsService(db, new InvoiceRuleEngine(RuleTestData.Catalog()), company, user, clock), db, company, user);
    }

    private static GeneralSettingsInput General(int legal = 8, int escalate = 2, string mode = ModuleModes.Simulation, DateOnly? from = null) => new()
    {
        LegalDeadlineDays = legal, EscalateDaysBefore = escalate, CutoffTime = new TimeOnly(10, 0),
        FinalActionWhenNoObjection = RuleActions.Accept, ProcessFromDate = from, Mode = mode,
    };

    private static RuleSettingsInput Rule(string code, string action, bool active = true, int? days = null, Dictionary<string, decimal>? p = null) =>
        new(code, active, action, days, p ?? new Dictionary<string, decimal>());

    [Fact]
    public async Task Get_PrimeraVez_CreaDefaultsDelCatalogo()
    {
        var env = Build();

        var dto = await env.Svc.GetAsync(CancellationToken.None);

        Assert.Equal(8, dto.Rules.Count);
        Assert.False(dto.Rules.Single(r => r.Code == "oc-abierta").IsActive);
        Assert.Equal(ModuleModes.Simulation, dto.General.Mode);
        Assert.Equal(8, dto.General.LegalDeadlineDays);
        Assert.Equal(new TimeOnly(10, 0), dto.General.CutoffTime);
        Assert.Equal(8, await env.Db.RuleSettings.CountAsync(r => r.CompanyId == env.Company.CompanyId));
        Assert.Equal(30m, dto.Rules.Single(r => r.Code == "oc-antiguedad").Parameters["max_days"]);
    }

    [Fact]
    public async Task SaveRule_AccionNoPermitida_Falla()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRuleAsync(Rule("oc-saldo", RuleActions.Accept), CancellationToken.None));
    }

    [Fact]
    public async Task SaveRule_ParametroFueraDeRango_Falla()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRuleAsync(Rule("oc-antiguedad", RuleActions.Reject, p: new() { ["max_days"] = 400 }), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRuleAsync(Rule("oc-antiguedad", RuleActions.Reject, p: new() { ["otro"] = 1 }), CancellationToken.None));
    }

    [Fact]
    public async Task SaveRule_DesactivarRegla7_Falla()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRuleAsync(Rule("revision-manual-proveedor", RuleActions.ManualReview, active: false), CancellationToken.None));
    }

    [Fact]
    public async Task SaveRule_AvisoConDiasIgualAlPlazo_Falla()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveRuleAsync(Rule("oc-referenciada", RuleActions.WarnThenReject, days: 8), CancellationToken.None));
    }

    [Fact]
    public async Task SaveGeneral_AvisoMayorOIgualQuePlazo_Falla()
    {
        var env = Build();
        await env.Svc.GetAsync(CancellationToken.None); // oc-referenciada: avisar 3 días

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SaveGeneralAsync(General(legal: 3, escalate: 1), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.SaveGeneralAsync(General(escalate: 8), CancellationToken.None));
    }

    [Fact]
    public async Task SaveGeneral_ActivoSinFinanzasOSinFecha_Falla()
    {
        var env = Build();
        await env.Svc.GetAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveGeneralAsync(General(mode: ModuleModes.Active, from: new DateOnly(2026, 11, 1)), CancellationToken.None));

        var settings = await env.Db.Settings.SingleAsync();
        settings.FinanceGroupId = 1;
        await env.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveGeneralAsync(General(mode: ModuleModes.Active), CancellationToken.None));

        await env.Svc.SaveGeneralAsync(General(mode: ModuleModes.Active, from: new DateOnly(2026, 11, 1)), CancellationToken.None);
        Assert.Equal(ModuleModes.Active, (await env.Db.Settings.SingleAsync()).Mode);
    }

    [Fact]
    public async Task Save_RegistraEventoDeAuditoria()
    {
        var env = Build();

        await env.Svc.SaveRuleAsync(Rule("oc-antiguedad", RuleActions.Reject, p: new() { ["max_days"] = 45 }), CancellationToken.None);
        await env.Svc.SaveGeneralAsync(General(legal: 9), CancellationToken.None);

        var events = await env.Db.ConfigEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal("rule", events[0].EntityType);
        Assert.Equal("oc-antiguedad", events[0].EntityKey);
        Assert.Contains("30", events[0].BeforeJson);
        Assert.Contains("45", events[0].AfterJson);
        Assert.Equal(env.User.UserId, events[0].UserId);
        Assert.Equal("settings", events[1].EntityType);
    }

    [Fact]
    public async Task GetEngineConfiguration_ReflejaLoGuardado()
    {
        var env = Build();
        await env.Svc.SaveRuleAsync(Rule("oc-abierta", RuleActions.Reject), CancellationToken.None);
        await env.Svc.SaveGeneralAsync(General() with { FinalActionWhenNoObjection = RuleActions.ManualReview }, CancellationToken.None);

        var config = await env.Svc.GetEngineConfigurationAsync(CancellationToken.None);

        Assert.True(config.ByRule["oc-abierta"].IsActive);
        Assert.Equal(RuleActions.Reject, config.ByRule["oc-abierta"].Action);
        Assert.Equal(RuleActions.ManualReview, config.FinalActionWhenNoObjection);
        Assert.Equal(4, config.OrderByRule["oc-saldo"]);
    }

    [Fact]
    public async Task Get_OtraCompania_NoVeLaConfiguracion()
    {
        var env = Build();
        await env.Svc.SaveGeneralAsync(General(legal: 9), CancellationToken.None);

        env.Company.CompanyId = Guid.NewGuid();
        var dto = await env.Svc.GetAsync(CancellationToken.None);

        Assert.Equal(8, dto.General.LegalDeadlineDays);
    }
}
