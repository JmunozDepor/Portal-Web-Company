using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteEmissionServiceTests
{
    private sealed record Env(DteEmissionService Svc, PortalSaasDbContext Db, FakeDteProvider Provider, FakeCompany Company);

    private static async Task<Env> BuildAsync(bool configure = true)
    {
        var db = DteTestSupport.Db();
        var provider = new FakeDteProvider();
        var company = new FakeCompany();
        var settings = new DteSettingsService(db, DteTestSupport.Crypto(), new DteProviderRegistry(new IDteProvider[] { provider }));
        if (configure) await settings.SaveAsync(company.CompanyId, DteTestSupport.ValidInput());
        return new Env(new DteEmissionService(db, company, new FakeUser(), settings, TimeProvider.System), db, provider, company);
    }

    private static DteEmissionRequest Request(Guid? key = null, DteDocument? doc = null) =>
        new(key ?? Guid.NewGuid(), "despachos", "manifest:1/guide:1", doc ?? DteRulesTests.Guia52Valida());

    [Fact]
    public async Task Emit_Exito_PasaPorReservaYConfirma()
    {
        var env = await BuildAsync();
        var req = Request();

        var r = await env.Svc.EmitAsync(req);

        Assert.Equal(DteEmissionStatuses.Issued, r.Status);
        Assert.Equal(103, r.Folio);
        Assert.Equal("FAKE", r.ProviderCode);
        Assert.Equal(new[] { req.EmissionKey }, env.Provider.ReservedKeys);
        var row = await env.Db.DteEmissions.SingleAsync();
        Assert.NotNull(row.XmlSigned);
        Assert.NotNull(row.XmlSent);
        Assert.Equal("76030680-0", row.IssuerTaxId);
        Assert.NotNull(row.IssuedAt);
    }

    [Fact]
    public async Task Emit_DosVecesMismaClave_UnaSolaReserva()
    {
        var env = await BuildAsync();
        var req = Request();
        await env.Svc.EmitAsync(req);
        var again = await env.Svc.EmitAsync(req);

        Assert.Equal(DteEmissionStatuses.Issued, again.Status);
        Assert.Equal(1, env.Provider.Reserves);
        Assert.Equal(1, env.Provider.Confirms);
        Assert.Equal(1, await env.Db.DteEmissions.CountAsync());
    }

    [Fact]
    public async Task Emit_UuidYaUtilizado_RecuperaFolioYConfirma()
    {
        var env = await BuildAsync();
        env.Provider.UuidUsedFolio = 777;
        var r = await env.Svc.EmitAsync(Request());
        Assert.Equal(DteEmissionStatuses.Issued, r.Status);
        Assert.Equal(777, r.Folio);
    }

    [Fact]
    public async Task Emit_CaidaEntreReservaYConfirmacion_ReintentoSoloConfirma()
    {
        var env = await BuildAsync();
        env.Provider.FailNextConfirms = 1;
        var req = Request();

        var first = await env.Svc.EmitAsync(req);
        Assert.Equal(DteEmissionStatuses.FolioReserved, first.Status);
        Assert.Equal(103, first.Folio);
        Assert.Contains("sin conexión", first.Error);

        var second = await env.Svc.EmitAsync(req);
        Assert.Equal(DteEmissionStatuses.Issued, second.Status);
        Assert.Equal(103, second.Folio);
        Assert.Equal(1, env.Provider.Reserves);
    }

    [Fact]
    public async Task Emit_CaidaEnReserva_QuedaPendienteSinLanzar()
    {
        var env = await BuildAsync();
        env.Provider.FailNextReserves = 1;
        var r = await env.Svc.EmitAsync(Request());
        Assert.Equal(DteEmissionStatuses.Pending, r.Status);
        Assert.Null(r.Folio);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public async Task Emit_RechazoNegocio_FailedConGlosa_YReintentoConContenidoCorregido()
    {
        var env = await BuildAsync();
        env.Provider.RejectNextReserve = "Error de esquema: CmnaDest";
        var key = Guid.NewGuid();

        var failed = await env.Svc.EmitAsync(Request(key));
        Assert.Equal(DteEmissionStatuses.Failed, failed.Status);
        Assert.Equal("Error de esquema: CmnaDest", failed.Error);

        var doc = DteRulesTests.Guia52Valida();
        var fixedDoc = doc with { Transport = doc.Transport! with { DestinationCommune = "Talca" } };
        var ok = await env.Svc.EmitAsync(Request(key, fixedDoc));
        Assert.Equal(DteEmissionStatuses.Issued, ok.Status);
        Assert.Contains("Talca", (await env.Db.DteEmissions.SingleAsync()).RequestJson);
    }

    [Fact]
    public async Task Emit_MismaClaveOtroContenido_TrasReserva_Falla()
    {
        var env = await BuildAsync();
        var key = Guid.NewGuid();
        await env.Svc.EmitAsync(Request(key));
        var doc = DteRulesTests.Guia52Valida();
        var other = doc with { Receiver = doc.Receiver with { Name = "Otro receptor" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.EmitAsync(Request(key, other)));
    }

    [Fact]
    public async Task Emit_SinConfiguracion_Falla()
    {
        var env = await BuildAsync(configure: false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.EmitAsync(Request()));
        Assert.Contains("documentos electrónicos", ex.Message);
    }

    [Fact]
    public async Task Emit_SinCompania_Falla()
    {
        var env = await BuildAsync();
        env.Company.Id = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.EmitAsync(Request()));
    }

    [Fact]
    public async Task Emit_DocumentoInvalido_NoLlamaAlProveedor()
    {
        var env = await BuildAsync();
        var doc = DteRulesTests.Guia52Valida() with { Transport = null };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.EmitAsync(Request(doc: doc)));
        Assert.Contains("transporte", ex.Message);
        Assert.Equal(0, env.Provider.Reserves);
        Assert.Equal(0, await env.Db.DteEmissions.CountAsync());
    }

    [Fact]
    public async Task Get_OtraCompania_Null()
    {
        var env = await BuildAsync();
        var req = Request();
        await env.Svc.EmitAsync(req);
        env.Company.Id = Guid.NewGuid();
        Assert.Null(await env.Svc.GetAsync(req.EmissionKey));
    }

    [Fact]
    public async Task Abandon_FolioReservado_Libera()
    {
        var env = await BuildAsync();
        env.Provider.FailNextConfirms = 1;
        var req = Request();
        await env.Svc.EmitAsync(req);

        await env.Svc.AbandonAsync(req.EmissionKey, "Manifiesto anulado");
        Assert.Equal(1, env.Provider.Releases);
        Assert.Equal(DteEmissionStatuses.Released, (await env.Svc.GetAsync(req.EmissionKey))!.Status);
    }

    [Fact]
    public async Task Abandon_RechazadaSinFolio_Abandona()
    {
        var env = await BuildAsync();
        env.Provider.RejectNextReserve = "Error de esquema";
        var req = Request();
        await env.Svc.EmitAsync(req);
        await env.Svc.AbandonAsync(req.EmissionKey, "Manifiesto anulado");
        Assert.Equal(DteEmissionStatuses.Abandoned, (await env.Svc.GetAsync(req.EmissionKey))!.Status);
        Assert.Equal(0, env.Provider.Releases);
    }

    [Fact]
    public async Task Abandon_Emitida_Falla()
    {
        var env = await BuildAsync();
        var req = Request();
        await env.Svc.EmitAsync(req);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AbandonAsync(req.EmissionKey, "x"));
    }

    [Fact]
    public async Task Emit_Abandonada_NoReemite()
    {
        var env = await BuildAsync();
        env.Provider.RejectNextReserve = "Error de esquema";
        var req = Request();
        await env.Svc.EmitAsync(req);
        await env.Svc.AbandonAsync(req.EmissionKey, "x");
        var r = await env.Svc.EmitAsync(req);
        Assert.Equal(DteEmissionStatuses.Abandoned, r.Status);
        Assert.Equal(1, env.Provider.Reserves); // solo el intento original
    }

    [Fact]
    public async Task GetPdf_SoloEmitidas()
    {
        var env = await BuildAsync();
        env.Provider.FailNextReserves = 1;
        var pending = Request();
        await env.Svc.EmitAsync(pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.GetPdfAsync(pending.EmissionKey, false, 1));

        var ok = Request();
        await env.Svc.EmitAsync(ok);
        Assert.NotEmpty(await env.Svc.GetPdfAsync(ok.EmissionKey, false, 1));
    }

    [Fact]
    public async Task Preview_ConErrores_SinPdf_YSinErrores_ConPdf()
    {
        var env = await BuildAsync();
        var bad = await env.Svc.PreviewAsync(DteRulesTests.Guia52Valida() with { Transport = null });
        Assert.NotEmpty(bad.Errors);
        Assert.Null(bad.Pdf);

        var good = await env.Svc.PreviewAsync(DteRulesTests.Guia52Valida());
        Assert.Empty(good.Errors);
        Assert.NotNull(good.Pdf);
        Assert.Equal(0, env.Provider.Reserves);
    }

    [Fact]
    public async Task GetConfiguration_InformaProveedorYEmisor()
    {
        var env = await BuildAsync();
        var c = await env.Svc.GetConfigurationAsync();
        Assert.True(c.IsConfigured);
        Assert.Equal("FAKE", c.ProviderCode);
        Assert.Equal("76030680-0", c.IssuerTaxId);
        Assert.Contains(52, c.SupportedTypes);

        var none = await (await BuildAsync(configure: false)).Svc.GetConfigurationAsync();
        Assert.False(none.IsConfigured);
        Assert.NotNull(none.Error);
    }

    [Fact]
    public async Task Emit_TipoNoSoportado_Falla()
    {
        var env = await BuildAsync();
        var doc = DteRulesTests.Guia52Valida() with { IdDoc = new DteIdDoc(39, new DateOnly(2026, 11, 10), null, null) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.EmitAsync(Request(doc: doc)));
    }

    [Fact]
    public async Task Emit_BuildXmlLanza_QuedaFailedConMensaje_YCuentaIntento()
    {
        var env = await BuildAsync();
        env.Provider.ThrowOnBuild = _ => true;

        var r = await env.Svc.EmitAsync(Request());

        Assert.Equal(DteEmissionStatuses.Failed, r.Status);
        Assert.Contains("No se pudo armar", r.Error);
        Assert.Equal(1, (await env.Db.DteEmissions.SingleAsync()).Attempts);
        Assert.Equal(0, env.Provider.Reserves);
    }

    [Fact]
    public async Task Advance_AmbienteCambiado_NoConfirmaContraOtroAmbiente()
    {
        var env = await BuildAsync();
        env.Provider.FailNextConfirms = 1;
        var req = Request();
        await env.Svc.EmitAsync(req);                       // folio reservado en certificación
        var s = await env.Db.CompanyDteSettings.SingleAsync();
        s.Environment = "production";                        // cambio directo (saltando la validación)
        await env.Db.SaveChangesAsync();

        var r = await env.Svc.EmitAsync(req);

        Assert.Equal(DteEmissionStatuses.FolioReserved, r.Status);
        Assert.Contains("configuración", r.Error);
        Assert.Equal(0, env.Provider.Confirms);
    }

    [Fact]
    public async Task Abandon_PendienteConIntentos_ResuelveFolioYLibera()
    {
        var env = await BuildAsync();
        env.Provider.FailNextReserves = 1;                   // generaDTE sin respuesta: ¿asignó folio?
        var req = Request();
        await env.Svc.EmitAsync(req);

        await env.Svc.AbandonAsync(req.EmissionKey, "Manifiesto anulado");

        Assert.Equal(new[] { req.EmissionKey }, env.Provider.ReservedKeys); // misma clave: devuelve el folio si existía
        Assert.Equal(1, env.Provider.Releases);
        Assert.Equal(DteEmissionStatuses.Released, (await env.Svc.GetAsync(req.EmissionKey))!.Status);
    }

    [Fact]
    public async Task Abandon_PendienteConIntentos_ProveedorCaido_FallaYNoAbandona()
    {
        var env = await BuildAsync();
        env.Provider.FailNextReserves = 2;
        var req = Request();
        await env.Svc.EmitAsync(req);

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.AbandonAsync(req.EmissionKey, "x"));
        Assert.Equal(DteEmissionStatuses.Pending, (await env.Svc.GetAsync(req.EmissionKey))!.Status);
    }
}
