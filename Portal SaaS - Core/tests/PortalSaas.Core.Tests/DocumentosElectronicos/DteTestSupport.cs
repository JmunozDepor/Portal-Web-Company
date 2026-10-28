using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

/// <summary>Proveedor DTE falso y scripteable: registra cada llamada y su contexto.</summary>
internal sealed class FakeDteProvider : IDteProvider
{
    public string Code { get; init; } = "FAKE";
    public DteProviderCapabilities Capabilities { get; } = new(new[] { 33, 52, 61 }, TwoPhase: true, SupportsPreview: true, SupportsRelease: true);

    public long NextFolio { get; set; } = 103;
    public int Reserves { get; private set; }
    public int Confirms { get; private set; }
    public int Releases { get; private set; }
    public List<Guid> ReservedKeys { get; } = new();
    public List<string> BaseUrls { get; } = new();

    /// <summary>Si no es null, la próxima reserva responde este rechazo de negocio.</summary>
    public string? RejectNextReserve { get; set; }
    /// <summary>Si no es null, la próxima reserva responde "uuid ya utilizado" con este folio.</summary>
    public long? UuidUsedFolio { get; set; }
    public int FailNextConfirms { get; set; }
    public int FailNextReserves { get; set; }
    public bool TestFails { get; set; }

    /// <summary>Si devuelve true para el documento, BuildXml lanza (simula un error inesperado al armar el XML).</summary>
    public Func<DteDocument, bool>? ThrowOnBuild { get; set; }

    public string BuildXml(DteDocument doc, DteIssuerData issuer) =>
        ThrowOnBuild?.Invoke(doc) == true
            ? throw new ArgumentException("String contains invalid Unicode code points.")
            : $"<DTE tipo=\"{doc.IdDoc.DteType}\" emisor=\"{issuer.TaxId}\"/>";

    public Task<byte[]> PreviewAsync(DteProviderContext ctx, string xml, int dteType, CancellationToken ct)
    {
        BaseUrls.Add(ctx.BaseUrl);
        return Task.FromResult("%PDF-borrador"u8.ToArray());
    }

    public Task<DteReserveResult> ReserveAsync(DteProviderContext ctx, string xml, int dteType, Guid emissionKey, CancellationToken ct)
    {
        BaseUrls.Add(ctx.BaseUrl);
        if (FailNextReserves > 0) { FailNextReserves--; throw new DteProviderUnavailableException("FAKE: sin conexión (reserva)."); }
        Reserves++;
        ReservedKeys.Add(emissionKey);
        if (RejectNextReserve is { } msg) { RejectNextReserve = null; return Task.FromResult(new DteReserveResult(false, null, msg, false)); }
        if (UuidUsedFolio is { } f) { UuidUsedFolio = null; return Task.FromResult(new DteReserveResult(false, f, null, true)); }
        return Task.FromResult(new DteReserveResult(true, NextFolio++, null, false));
    }

    public Task<DteConfirmResult> ConfirmAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)
    {
        BaseUrls.Add(ctx.BaseUrl);
        if (FailNextConfirms > 0) { FailNextConfirms--; throw new DteProviderUnavailableException("FAKE: sin conexión (confirmación)."); }
        Confirms++;
        return Task.FromResult(new DteConfirmResult(true, $"<DTE folio=\"{folio}\"><TED/></DTE>", null));
    }

    public Task ReleaseAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)
    {
        Releases++;
        return Task.CompletedTask;
    }

    public Task<byte[]> GetPdfAsync(DteProviderContext ctx, int dteType, long folio, bool cedible, int copies, CancellationToken ct) =>
        Task.FromResult("%PDF-emitido"u8.ToArray());

    public Task TestAsync(DteProviderContext ctx, CancellationToken ct) =>
        TestFails ? throw new DteProviderUnavailableException("FAKE: HTTP 405 en sinConfirmar.") : Task.CompletedTask;
}

internal sealed class FakeCompany : ICurrentCompanyAccessor
{
    public Guid? Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId => Id ?? throw new InvalidOperationException("Sin compañía.");
    public string Code => "TEST";
    public string Database => "TEST";
    public string ServiceLayerUrl => "";
    public string Country => "CL";
    public bool HasCompany => Id is not null;
}

internal sealed class FakeUser : ICurrentUserContext
{
    public Guid UserId { get; } = Guid.NewGuid();
    public string Username => "tester";
    public bool IsAdmin => false;
    public Guid OrganizationId { get; } = Guid.NewGuid();
    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

internal static class DteTestSupport
{
    public static PortalSaasDbContext Db(string? name = null) => new(new DbContextOptionsBuilder<PortalSaasDbContext>()
        .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);

    /// <summary>Clave AES-256 fija, SOLO para tests.</summary>
    public static SecretoCifradoService Crypto() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MasterSecretKey"] = Convert.ToBase64String(new byte[32]) })
        .Build());

    public static DteSettingsInput ValidInput(string provider = "FAKE", string? password = "clave-secreta", bool enabled = true,
        string baseUrl = "https://caoba.docele.cl/cl-dol-auth") => new()
    {
        ProviderCode = provider,
        Environment = DteEnvironments.Certification,
        BaseUrl = baseUrl,
        Username = "usuario-prueba",
        Password = password,
        IsEnabled = enabled,
        IssuerTaxId = "76030680-0",
        IssuerName = "Comercial Depor Ltda.",
        IssuerActivity = "Venta al por mayor de vestuario",
        IssuerActivityCode = 464100,
        IssuerAddress = "Av. Presidente Frei 1234",
        IssuerCommune = "Rancagua",
        IssuerCity = "Rancagua",
    };
}

/// <summary>Proveedor de recepción falso y scripteable: devuelve lo cargado en Received/References y registra cada llamada.</summary>
internal sealed class FakeReceptionProvider : IDteReceptionProvider
{
    public string Code { get; init; } = "FAKE";
    public DteReceptionCapabilities Capabilities { get; set; } = new(
        new[] { 33, 34, 43 },
        new[] { DteResponseActions.AcceptContent, DteResponseActions.ClaimContent, DteResponseActions.ClaimPartialGoods,
                DteResponseActions.ClaimTotalGoods, DteResponseActions.GoodsReceipt },
        SupportsResponseNote: false, SupportsDocumentDownload: false);

    public List<DteReceivedRecord> Received { get; } = new();
    public Dictionary<(string Issuer, int Type, long Folio), List<DteReceivedReference>> References { get; } = new();
    public Dictionary<(string Issuer, int Type, long Folio), string> Xmls { get; } = new();
    public List<(DateOnly From, DateOnly To)> ListCalls { get; } = new();
    public List<(string Issuer, int Type, long Folio, string Action)> Responds { get; } = new();
    public List<string> Notes { get; } = new();
    public List<DteReceptionContext> Contexts { get; } = new();

    /// <summary>Si no es null, la próxima respuesta devuelve esto en vez de Ok.</summary>
    public DteRespondProviderResult? NextRespond { get; set; }
    public int FailNextResponds { get; set; }
    public string? ThrowOnList { get; set; }
    public bool TestFails { get; set; }

    /// <summary>Demora del listado (para simular una sincronización lenta en paralelo con otra).</summary>
    public TimeSpan ListDelay { get; set; }

    public async Task<IReadOnlyList<DteReceivedRecord>> ListReceivedAsync(DteReceptionContext ctx, DateOnly from, DateOnly to, CancellationToken ct)
    {
        lock (ListCalls)
        {
            Contexts.Add(ctx);
            ListCalls.Add((from, to));
        }
        if (ListDelay > TimeSpan.Zero) await Task.Delay(ListDelay, ct);
        if (ThrowOnList is { } msg) throw new DteProviderUnavailableException(msg);
        return Received.ToList();
    }

    public Task<IReadOnlyList<DteReceivedReference>> GetReferencesAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DteReceivedReference>>(
            References.TryGetValue((issuerTaxId, dteType, folio), out var refs) ? refs : new List<DteReceivedReference>());

    public int XmlCalls { get; private set; }

    public Task<string?> GetXmlAsync(DteReceptionContext ctx, DteReceivedRecord record, CancellationToken ct)
    {
        XmlCalls++;
        Contexts.Add(ctx);
        return Task.FromResult(Xmls.TryGetValue((record.IssuerTaxId, record.DteType, record.Folio), out var xml) ? xml : null);
    }

    public byte[]? Pdf { get; set; }
    public bool ThrowOnPdf { get; set; }
    public List<string?> PdfUrls { get; } = new();

    public Task<byte[]?> GetPdfAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string? url, CancellationToken ct)
    {
        PdfUrls.Add(url);
        if (ThrowOnPdf) throw new DteProviderUnavailableException("FAKE: sin conexión (PDF).");
        return Task.FromResult(Pdf);
    }

    public Task<DteRespondProviderResult> RespondAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string action, CancellationToken ct)
    {
        if (FailNextResponds > 0) { FailNextResponds--; throw new DteProviderUnavailableException("FAKE: sin conexión (respuesta)."); }
        Responds.Add((issuerTaxId, dteType, folio, action));
        var result = NextRespond ?? new DteRespondProviderResult(true, false, null);
        NextRespond = null;
        return Task.FromResult(result);
    }

    public Task SetNoteAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string note, CancellationToken ct)
    {
        Notes.Add(note);
        return Task.CompletedTask;
    }

    public Task TestAsync(DteReceptionContext ctx, CancellationToken ct) =>
        TestFails ? throw new DteProviderUnavailableException("FAKE: HTTP 405 en consultarResumen.") : Task.CompletedTask;
}

internal static class DteReceptionTestSupport
{
    public static DteReceptionProviderRegistry Registry(params IDteReceptionProvider[] providers) =>
        new(providers.Length == 0 ? new IDteReceptionProvider[] { new FakeReceptionProvider() } : providers);

    public static DteSettingsInput WithReception(this DteSettingsInput input, bool enabled = true,
        string baseUrl = "https://caoba.docele.cl/cl-dol", string userMail = "facturas@depor.cl")
    {
        input.ReceptionEnabled = enabled;
        input.ReceptionBaseUrl = baseUrl;
        input.ReceptionUserMail = userMail;
        return input;
    }
}
