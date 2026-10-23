using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    public string BuildXml(DteDocument doc, DteIssuerData issuer) => $"<DTE tipo=\"{doc.IdDoc.DteType}\" emisor=\"{issuer.TaxId}\"/>";

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
