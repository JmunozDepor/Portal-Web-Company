using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Pasarela de emisión DTE (ver IDteEmissionService). Cada cambio de estado se guarda ANTES de la
/// siguiente llamada al proveedor, así un corte a mitad de camino se retoma sin duplicar: la reserva
/// usa uuid = EmissionKey (FACELE devuelve el folio original si ya existía) y una fila folio_reserved
/// solo necesita confirmarse. El reconciliador (DteReconciliationHostedService) usa AdvanceAsync.
/// </summary>
public sealed class DteEmissionService : IDteEmissionService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly DteSettingsService _settings;
    private readonly TimeProvider _time;

    public DteEmissionService(
        PortalSaasDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, DteSettingsService settings, TimeProvider time)
    {
        _db = db;
        _company = company;
        _user = user;
        _settings = settings;
        _time = time;
    }

    public async Task<DteConfigurationStatus> GetConfigurationAsync(CancellationToken ct = default)
    {
        if (!_company.HasCompany)
            return new DteConfigurationStatus(false, null, null, null, Array.Empty<int>(), "No hay una compañía seleccionada.");
        var resolved = await _settings.ResolveAsync(_company.CompanyId);
        if (resolved is not { } r)
            return new DteConfigurationStatus(false, null, null, null, Array.Empty<int>(), NotConfiguredMessage);
        return new DteConfigurationStatus(true, r.Provider.Code, r.Ctx.Environment, r.Issuer.TaxId, r.Provider.Capabilities.SupportedTypes, null);
    }

    public Task<IReadOnlyList<DteValidationError>> ValidateAsync(DteDocument doc, CancellationToken ct = default) =>
        Task.FromResult(DteRules.Validate(doc));

    public async Task<DtePreviewResult> PreviewAsync(DteDocument doc, CancellationToken ct = default)
    {
        var (ctx, issuer, provider) = await RequireConfigurationAsync();
        var errors = DteRules.Validate(doc);
        if (errors.Count > 0 || !provider.Capabilities.SupportsPreview) return new DtePreviewResult(errors, null);
        var pdf = await provider.PreviewAsync(ctx, provider.BuildXml(doc, issuer), doc.IdDoc.DteType, ct);
        return new DtePreviewResult(errors, pdf);
    }

    public async Task<DteEmissionResult> EmitAsync(DteEmissionRequest request, CancellationToken ct = default)
    {
        var (_, issuer, provider) = await RequireConfigurationAsync();
        if (!provider.Capabilities.SupportedTypes.Contains(request.Document.IdDoc.DteType))
            throw new InvalidOperationException(
                $"El proveedor {provider.Code} no emite documentos tipo {request.Document.IdDoc.DteType} en esta instalación.");
        var errors = DteRules.Validate(request.Document);
        if (errors.Count > 0)
            throw new InvalidOperationException("El documento no es válido: " + string.Join("; ", errors.Select(e => e.Message)));

        var json = JsonSerializer.Serialize(request.Document, Json);
        var hash = Hash(json);
        var companyId = _company.CompanyId;
        var row = await _db.DteEmissions.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmissionKey == request.EmissionKey, ct);
        if (row is null)
        {
            var now = _time.GetUtcNow();
            row = new DteEmission
            {
                CompanyId = companyId,
                EmissionKey = request.EmissionKey,
                SourceModule = request.SourceModule,
                SourceRef = request.SourceRef,
                DteType = request.Document.IdDoc.DteType,
                ProviderCode = provider.Code,
                IssuerTaxId = issuer.TaxId,
                Environment = (await _settings.ResolveAsync(companyId))!.Value.Ctx.Environment,
                Status = DteEmissionStatuses.Pending,
                RequestHash = hash,
                RequestJson = json,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = _user.UserId,
            };
            _db.DteEmissions.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        else if (row.RequestHash != hash)
        {
            // Solo se corrige el contenido de una emisión que el proveedor rechazó sin asignar folio.
            if (row.Status != DteEmissionStatuses.Failed || row.Folio is not null)
                throw new InvalidOperationException("La emisión ya existe con otro contenido; genere una clave nueva para reemitir.");
            row.RequestJson = json;
            row.RequestHash = hash;
            row.DteType = request.Document.IdDoc.DteType;
            row.UpdatedAt = _time.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }

        return await AdvanceAsync(row, ct);
    }

    /// <summary>
    /// Avanza una emisión lo que se pueda: pending/failed sin folio → reserva; folio_reserved → confirma.
    /// Las caídas del proveedor dejan la fila en su estado con LastError (no lanza). issued/released/abandoned no se tocan.
    /// </summary>
    internal async Task<DteEmissionResult> AdvanceAsync(DteEmission row, CancellationToken ct)
    {
        if (DteEmissionStatuses.IsFinal(row.Status)) return ToResult(row);

        var resolved = await _settings.ResolveAsync(row.CompanyId);
        if (resolved is not { } r || !string.Equals(r.Provider.Code, row.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            await SaveErrorAsync(row, "La configuración de documentos electrónicos de la compañía cambió o se deshabilitó; revísela en Administración.", ct);
            return ToResult(row);
        }
        var (ctx, issuer, provider) = r;

        try
        {
            if (row.Status is DteEmissionStatuses.Pending or DteEmissionStatuses.Failed && row.Folio is null)
            {
                var doc = JsonSerializer.Deserialize<DteDocument>(row.RequestJson, Json)!;
                row.Attempts++;
                row.XmlSent = provider.BuildXml(doc, issuer);
                var reserve = await provider.ReserveAsync(ctx, row.XmlSent, row.DteType, row.EmissionKey, ct);
                if (reserve.Ok || reserve.UuidAlreadyUsed)
                {
                    row.Folio = reserve.Folio;
                    row.Status = DteEmissionStatuses.FolioReserved;
                    row.ReservedAt = _time.GetUtcNow();
                    row.LastError = null;
                }
                else
                {
                    row.Status = DteEmissionStatuses.Failed;
                    row.LastError = Truncate(reserve.Error);
                }
                row.UpdatedAt = _time.GetUtcNow();
                await _db.SaveChangesAsync(ct);
            }

            if (row.Status == DteEmissionStatuses.FolioReserved && row.Folio is { } folio)
            {
                var confirm = await provider.ConfirmAsync(ctx, row.DteType, folio, ct);
                if (confirm.Ok)
                {
                    row.Status = DteEmissionStatuses.Issued;
                    row.XmlSigned = confirm.SignedXml;
                    row.IssuedAt = _time.GetUtcNow();
                    row.LastError = null;
                }
                else
                {
                    row.LastError = Truncate(confirm.Error);
                }
                row.UpdatedAt = _time.GetUtcNow();
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (DteProviderUnavailableException ex)
        {
            await SaveErrorAsync(row, ex.Message, ct);
        }
        return ToResult(row);
    }

    public async Task<DteEmissionResult?> GetAsync(Guid emissionKey, CancellationToken ct = default)
    {
        var row = await FindAsync(emissionKey, ct);
        return row is null ? null : ToResult(row);
    }

    public async Task<byte[]> GetPdfAsync(Guid emissionKey, bool cedible, int copies, CancellationToken ct = default)
    {
        var row = await FindAsync(emissionKey, ct) ?? throw new InvalidOperationException("La emisión no existe.");
        if (row.Status != DteEmissionStatuses.Issued || row.Folio is not { } folio)
            throw new InvalidOperationException("El documento todavía no está emitido.");
        var (ctx, _, provider) = await RequireConfigurationAsync();
        try
        {
            return await provider.GetPdfAsync(ctx, row.DteType, folio, cedible, copies, ct);
        }
        catch (DteProviderUnavailableException ex)
        {
            throw new InvalidOperationException($"No se pudo obtener el PDF: {ex.Message}");
        }
    }

    public async Task AbandonAsync(Guid emissionKey, string reason, CancellationToken ct = default)
    {
        var row = await FindAsync(emissionKey, ct) ?? throw new InvalidOperationException("La emisión no existe.");
        switch (row.Status)
        {
            case DteEmissionStatuses.Issued:
                throw new InvalidOperationException($"El documento folio {row.Folio} ya está emitido: no se puede desistir.");
            case DteEmissionStatuses.Released or DteEmissionStatuses.Abandoned:
                return;
            case DteEmissionStatuses.FolioReserved when row.Folio is { } folio:
                var (ctx, _, provider) = await RequireConfigurationAsync();
                try
                {
                    await provider.ReleaseAsync(ctx, row.DteType, folio, ct);
                }
                catch (DteProviderUnavailableException ex)
                {
                    throw new InvalidOperationException($"No se pudo liberar el folio {folio}: {ex.Message}");
                }
                row.Status = DteEmissionStatuses.Released;
                break;
            default:
                row.Status = DteEmissionStatuses.Abandoned;
                break;
        }
        row.LastError = Truncate("Desistida: " + reason);
        row.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

    private const string NotConfiguredMessage =
        "La compañía no tiene configurados los documentos electrónicos (proveedor, credenciales y emisor): configúrelos en Administración.";

    private async Task<(DteProviderContext Ctx, DteIssuerData Issuer, IDteProvider Provider)> RequireConfigurationAsync()
    {
        if (!_company.HasCompany) throw new InvalidOperationException("No hay una compañía seleccionada en la sesión.");
        return await _settings.ResolveAsync(_company.CompanyId) ?? throw new InvalidOperationException(NotConfiguredMessage);
    }

    private async Task<DteEmission?> FindAsync(Guid emissionKey, CancellationToken ct)
    {
        if (!_company.HasCompany) return null;
        var companyId = _company.CompanyId;
        return await _db.DteEmissions.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.EmissionKey == emissionKey, ct);
    }

    private async Task SaveErrorAsync(DteEmission row, string message, CancellationToken ct)
    {
        row.LastError = Truncate(message);
        row.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

    private static DteEmissionResult ToResult(DteEmission r) =>
        new(r.EmissionKey, r.Status, r.Folio, r.LastError, r.ProviderCode, r.Environment);

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static string? Truncate(string? s) => s is { Length: > 1000 } ? s[..1000] : s;
}
