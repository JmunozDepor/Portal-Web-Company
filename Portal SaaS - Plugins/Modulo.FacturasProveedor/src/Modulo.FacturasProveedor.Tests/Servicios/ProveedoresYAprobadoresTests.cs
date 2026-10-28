using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Dominio.Reglas;
using Modulo.FacturasProveedor.Tests.Fakes;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class ProveedoresYAprobadoresTests
{
    private sealed record Env(SupplierProfileService Profiles, ApproverGroupService Groups, FacturasProveedorDbContext Db,
        FakeSapLookupService Sap, FakeCurrentCompanyAccessor Company);

    private static Env Build()
    {
        var db = TestDb.Create();
        var sap = new FakeSapLookupService().AddSupplier().AddSupplier("77555010-K", "P77555010", "Inmobiliaria Los Robles SpA");
        var company = new FakeCurrentCompanyAccessor();
        var user = new FakeCurrentUserContext();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
        return new Env(
            new SupplierProfileService(db, sap, new InvoiceRuleEngine(RuleTestData.Catalog()), company, user, clock),
            new ApproverGroupService(db, company, user, clock), db, sap, company);
    }

    private static SupplierProfileInput Input(string taxId = "77.555.010-k", string condition = SupplierConditions.FixedMonthly,
        long? group = null, DateOnly? from = null, DateOnly? to = null, IReadOnlyList<RuleOverrideInput>? overrides = null) =>
        new(null, taxId, condition, group, null, from ?? new DateOnly(2026, 11, 1), to, "Arriendo", true,
            overrides ?? Array.Empty<RuleOverrideInput>());

    private static async Task<long> GroupAsync(Env env, string name = "Gerencia Finanzas", bool finance = false, params Guid[] members) =>
        await env.Groups.SaveAsync(new ApproverGroupInput(null, name, finance, true, members), CancellationToken.None);

    [Fact]
    public async Task Save_RutInexistenteEnSap_Falla()
    {
        var env = Build();
        var g = await GroupAsync(env);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Profiles.SaveAsync(Input(taxId: "11.111.111-1", group: g), CancellationToken.None));
        Assert.Contains("SAP", ex.Message);
    }

    [Fact]
    public async Task Save_GastoFijoSinAprobador_Falla()
    {
        var env = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Profiles.SaveAsync(Input(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Profiles.SaveAsync(Input(condition: SupplierConditions.AlwaysManual), CancellationToken.None));
    }

    [Fact]
    public async Task Save_GuardaCardCodeYNombreDeSap()
    {
        var env = Build();
        var g = await GroupAsync(env);

        var id = await env.Profiles.SaveAsync(Input(group: g), CancellationToken.None);

        var row = await env.Db.SupplierProfiles.SingleAsync(p => p.Id == id);
        Assert.Equal("P77555010", row.CardCode);
        Assert.Equal("Inmobiliaria Los Robles SpA", row.SupplierName);
        Assert.Equal("77555010-K", row.TaxId);
        Assert.Single(await env.Db.ConfigEvents.Where(e => e.EntityType == "supplier").ToListAsync());
    }

    [Fact]
    public async Task Save_RutRepetido_Falla()
    {
        var env = Build();
        var g = await GroupAsync(env);
        await env.Profiles.SaveAsync(Input(group: g), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Profiles.SaveAsync(Input(taxId: "77555010-K", group: g), CancellationToken.None));
    }

    [Fact]
    public async Task GetActiveFacts_FueraDeVigencia_Null()
    {
        var env = Build();
        var g = await GroupAsync(env);
        await env.Profiles.SaveAsync(Input(group: g, from: new DateOnly(2026, 11, 1), to: new DateOnly(2026, 12, 31)), CancellationToken.None);

        Assert.Null(await env.Profiles.GetActiveFactsAsync("77555010-K", new DateOnly(2026, 10, 31), CancellationToken.None));
        Assert.Null(await env.Profiles.GetActiveFactsAsync("77555010-K", new DateOnly(2027, 1, 1), CancellationToken.None));
        var facts = await env.Profiles.GetActiveFactsAsync("77.555.010-k", new DateOnly(2026, 11, 15), CancellationToken.None);
        Assert.Equal(SupplierConditions.FixedMonthly, facts!.Condition);
        Assert.Equal(g, facts.ApproverGroupId);
    }

    [Fact]
    public async Task Save_OverrideFueraDeRango_Falla()
    {
        var env = Build();
        var overrides = new[] { new RuleOverrideInput("oc-antiguedad", RuleOverrideModes.Custom, null, null, new Dictionary<string, decimal> { ["max_days"] = 999 }) };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Profiles.SaveAsync(Input(taxId: "76412553-1", condition: SupplierConditions.Custom, overrides: overrides), CancellationToken.None));

        var notAllowed = new[] { new RuleOverrideInput("plazo-por-vencer", RuleOverrideModes.Custom, RuleActions.ManualReview, null, null) };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Profiles.SaveAsync(Input(taxId: "76412553-1", condition: SupplierConditions.Custom, overrides: notAllowed), CancellationToken.None));

        var ok = new[] { new RuleOverrideInput("oc-antiguedad", RuleOverrideModes.Custom, null, null, new Dictionary<string, decimal> { ["max_days"] = 60 }) };
        await env.Profiles.SaveAsync(Input(taxId: "76412553-1", condition: SupplierConditions.Custom, overrides: ok), CancellationToken.None);
        var facts = await env.Profiles.GetActiveFactsAsync("76412553-1", new DateOnly(2026, 11, 2), CancellationToken.None);
        Assert.Equal(60, facts!.Overrides["oc-antiguedad"].Parameters!["max_days"]);
    }

    [Fact]
    public async Task Groups_SoloUnFinanzas()
    {
        var env = Build();
        var a = await GroupAsync(env, "Finanzas", finance: true);
        var b = await GroupAsync(env, "Pago a proveedores", finance: true);

        var groups = await env.Db.ApproverGroups.ToListAsync();
        Assert.False(groups.Single(x => x.Id == a).IsFinance);
        Assert.True(groups.Single(x => x.Id == b).IsFinance);
        Assert.Equal(b, (await env.Db.Settings.SingleAsync()).FinanceGroupId);
    }

    [Fact]
    public async Task DeleteGroup_EnUso_Falla()
    {
        var env = Build();
        var g = await GroupAsync(env);
        await env.Profiles.SaveAsync(Input(group: g), CancellationToken.None);
        var finance = await GroupAsync(env, "Finanzas", finance: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.DeleteAsync(g, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.DeleteAsync(finance, CancellationToken.None));
        var free = await GroupAsync(env, "Libre");
        await env.Groups.DeleteAsync(free, CancellationToken.None);
        Assert.Equal(2, await env.Db.ApproverGroups.CountAsync());
    }

    [Fact]
    public async Task GetGroupIdsForUser()
    {
        var env = Build();
        var user = Guid.NewGuid();
        var a = await GroupAsync(env, "A", false, user);
        await GroupAsync(env, "B", false, Guid.NewGuid());
        var c = await GroupAsync(env, "C", false, user, Guid.NewGuid());

        Assert.Equal(new[] { a, c }, (await env.Groups.GetGroupIdsForUserAsync(user, CancellationToken.None)).OrderBy(x => x));
        Assert.Equal(2, (await env.Groups.GetMemberIdsAsync(c, CancellationToken.None)).Count);
    }

    /// <summary>I5 (revisión final): en modo activo el grupo de Finanzas no puede quedar desmarcado, inactivo ni sin integrantes.</summary>
    [Fact]
    public async Task Grupos_EnActivo_FinanzasNoQuedaInactivaNiVacia()
    {
        var env = await new Servicios.ReviewTestEnv().StartAsync(ModuleModes.Active);
        var finance = (await env.Db.Settings.SingleAsync()).FinanceGroupId!.Value;
        var members = new[] { env.FinanceUser };

        foreach (var input in new[]
        {
            new ApproverGroupInput(finance, "Finanzas", false, true, members),
            new ApproverGroupInput(finance, "Finanzas", true, false, members),
            new ApproverGroupInput(finance, "Finanzas", true, true, Array.Empty<Guid>()),
            new ApproverGroupInput(null, "Finanzas 2", true, true, Array.Empty<Guid>()),
        })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.SaveAsync(input, CancellationToken.None));
        }
        Assert.Equal(finance, (await env.Db.Settings.AsNoTracking().SingleAsync()).FinanceGroupId);

        var moved = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Tesorería", true, true, members), CancellationToken.None);
        Assert.Equal(moved, (await env.Db.Settings.AsNoTracking().SingleAsync()).FinanceGroupId);
    }
}
