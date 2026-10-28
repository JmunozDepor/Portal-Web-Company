using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

/// <summary>Reloj fijo para tests (UTC).</summary>
internal sealed class FixedClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero); // 12:00 en Chile
    public override DateTimeOffset GetUtcNow() => Now;
}

public class DteReceptionSyncServiceTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private sealed record Env(DteReceptionSyncService Sync, PortalSaasDbContext Db, FakeReceptionProvider Provider, FixedClock Clock);

    private static async Task<Env> BuildAsync()
    {
        var db = DteTestSupport.Db();
        var provider = new FakeReceptionProvider();
        var settings = new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { new FakeDteProvider() }),
            DteReceptionTestSupport.Registry(provider));
        await settings.SaveAsync(CompanyId, DteTestSupport.ValidInput().WithReception());
        var clock = new FixedClock();
        return new Env(new DteReceptionSyncService(db, settings, clock, NullLogger<DteReceptionSyncService>.Instance), db, provider, clock);
    }

    internal static DteReceivedRecord Record(long folio = 18452, string? claimStatus = "Por_Aceptar", string? respondedAction = null, long total = 2380000) =>
        new("76412553-1", "Comercial Textil Andes SpA", 33, folio, new DateOnly(2026, 10, 23),
            new DateTimeOffset(2026, 10, 24, 12, 10, 0, TimeSpan.Zero), 2000000, 0, 380000, total, null, "SII_Documento_OK",
            "Por_Aprobar", claimStatus, false, false, null, respondedAction);

    [Fact]
    public async Task Sync_PrimeraVez_UsaDiasIniciales_YMarcaCargaInicial()
    {
        var env = await BuildAsync();

        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 8, 28), new DateOnly(2026, 10, 27)), env.Provider.ListCalls.Single());
        var s = await env.Db.CompanyDteSettings.SingleAsync();
        Assert.Equal(env.Clock.Now, s.ReceptionInitialLoadDoneAt);
        Assert.Equal(env.Clock.Now, s.ReceptionLastSyncAt);
        Assert.Null(s.ReceptionLastError);
    }

    [Fact]
    public async Task Sync_Siguiente_UsaVentana()
    {
        var env = await BuildAsync();
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 27)), env.Provider.ListCalls[1]);
    }

    [Fact]
    public async Task Sync_Nuevo_TraeReferencias_Y801EnOc()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        env.Provider.References[("76412553-1", 33, 18452)] = new()
        {
            new("801", "4512", new DateOnly(2026, 10, 12), null, "ORDEN DE COMPRA"),
            new("HES", "77", null, null, null),
            new("801", "OC-SIN-NUMERO", null, null, null),
        };

        var result = await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal(1, result.New);
        var row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(CompanyId, row.CompanyId);
        Assert.Equal("FAKE", row.ProviderCode);
        Assert.Equal("certification", row.Environment);
        Assert.Equal(2380000, row.TotalAmount);
        Assert.Equal(DteResponseStatuses.None, row.ResponseStatus);
        Assert.Equal(3, JsonSerializer.Deserialize<List<DteReceivedReference>>(row.ReferencesJson)!.Count);
        var po = DteReceivedJson.PurchaseOrderRefs(row.PurchaseOrderRefsJson).Single();
        Assert.Equal(4512, po.DocNum);
        Assert.Equal(DtePurchaseOrderRefSources.Dte, po.Source);
    }

    [Fact]
    public async Task Sync_Existente_SinCambios_NoTocaUpdatedAt()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);
        var before = (await env.Db.DteReceived.SingleAsync()).UpdatedAt;

        env.Clock.Now = env.Clock.Now.AddHours(1);
        var result = await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal(0, result.New);
        Assert.Equal(0, result.Updated);
        Assert.Equal(before, (await env.Db.DteReceived.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task Sync_Existente_ConCambio_ActualizaUpdatedAt()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        env.Clock.Now = env.Clock.Now.AddHours(1);
        env.Provider.Received[0] = Record() with { IsAssigned = true };
        var result = await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal(1, result.Updated);
        var row = await env.Db.DteReceived.SingleAsync();
        Assert.True(row.IsAssigned);
        Assert.Equal(env.Clock.Now, row.UpdatedAt);
    }

    [Fact]
    public async Task Sync_RespondidoEnOperador_SinRespuestaPortal_QuedaExternal()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        env.Provider.Received[0] = Record(claimStatus: "Reclamo_Contenido_Documento", respondedAction: DteResponseActions.ClaimContent);
        var result = await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal(1, result.MarkedExternal);
        var row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(DteResponseStatuses.External, row.ResponseStatus);
        Assert.Equal(DteResponseActions.ClaimContent, row.ResponseAction);
    }

    [Fact]
    public async Task Sync_NuevoYaRespondido_NaceExternal()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record(claimStatus: "Acepta_Contenido_Documento", respondedAction: DteResponseActions.AcceptContent));

        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Equal(DteResponseStatuses.External, (await env.Db.DteReceived.SingleAsync()).ResponseStatus);
    }

    [Fact]
    public async Task Sync_PendienteYOperadorCoincide_QuedaSent()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);
        var row = await env.Db.DteReceived.SingleAsync();
        row.ResponseStatus = DteResponseStatuses.Pending;
        row.ResponseAction = DteResponseActions.AcceptContent;
        await env.Db.SaveChangesAsync();

        env.Provider.Received[0] = Record(claimStatus: "Acepta_Contenido_Documento", respondedAction: DteResponseActions.AcceptContent);
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(DteResponseStatuses.Sent, row.ResponseStatus);
        Assert.Equal(env.Clock.Now, row.RespondedAt);
    }

    [Fact]
    public async Task Sync_PendienteYOperadorDistinto_QuedaExternal()
    {
        var env = await BuildAsync();
        env.Provider.Received.Add(Record());
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);
        var row = await env.Db.DteReceived.SingleAsync();
        row.ResponseStatus = DteResponseStatuses.Pending;
        row.ResponseAction = DteResponseActions.AcceptContent;
        await env.Db.SaveChangesAsync();

        env.Provider.Received[0] = Record(claimStatus: "Reclamo_Contenido_Documento", respondedAction: DteResponseActions.ClaimContent);
        await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        row = await env.Db.DteReceived.SingleAsync();
        Assert.Equal(DteResponseStatuses.External, row.ResponseStatus);
        Assert.Equal(DteResponseActions.ClaimContent, row.ResponseAction);
    }

    [Fact]
    public async Task Sync_ErrorProveedor_GuardaErrorSinClave()
    {
        var env = await BuildAsync();
        env.Provider.ThrowOnList = "FAKE cayó con clave-secreta en el mensaje";

        var result = await env.Sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.DoesNotContain("clave-secreta", result.Error);
        var s = await env.Db.CompanyDteSettings.SingleAsync();
        Assert.NotNull(s.ReceptionLastError);
        Assert.DoesNotContain("clave-secreta", s.ReceptionLastError);
        Assert.Null(s.ReceptionInitialLoadDoneAt);
        Assert.Equal(env.Clock.Now, s.ReceptionLastSyncAt);
    }

    [Fact]
    public async Task Sync_RecepcionDeshabilitada_NoLlamaAlProveedor()
    {
        var db = DteTestSupport.Db();
        var provider = new FakeReceptionProvider();
        var settings = new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { new FakeDteProvider() }),
            DteReceptionTestSupport.Registry(provider));
        await settings.SaveAsync(CompanyId, DteTestSupport.ValidInput().WithReception(enabled: false));
        var sync = new DteReceptionSyncService(db, settings, new FixedClock(), NullLogger<DteReceptionSyncService>.Instance);

        var result = await sync.SyncCompanyAsync(CompanyId, CancellationToken.None);

        Assert.Empty(provider.ListCalls);
        Assert.NotNull(result.Error);
    }
}
