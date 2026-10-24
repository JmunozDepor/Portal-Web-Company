using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Configuración DTE de una compañía para mostrar en /Admin. Nunca trae la contraseña.</summary>
public sealed record DteSettingsDto(
    string ProviderCode, string Environment, string BaseUrl, string Username, bool HasSecret, bool IsEnabled,
    string IssuerTaxId, string IssuerName, string IssuerActivity, int IssuerActivityCode, string IssuerAddress,
    string IssuerCommune, string IssuerCity, long? IssuerSiiBranchCode, DateTimeOffset UpdatedAt);

/// <summary>Formulario de /Admin. Password vacío o null = conservar la actual.</summary>
public sealed class DteSettingsInput
{
    public string ProviderCode { get; set; } = "";
    public string Environment { get; set; } = DteEnvironments.Certification;
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public bool IsEnabled { get; set; }
    public string IssuerTaxId { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public string IssuerActivity { get; set; } = "";
    public int IssuerActivityCode { get; set; }
    public string IssuerAddress { get; set; } = "";
    public string IssuerCommune { get; set; } = "";
    public string IssuerCity { get; set; } = "";
    public long? IssuerSiiBranchCode { get; set; }
}

/// <summary>
/// Alta/edición de company_dte_settings (/Admin) y resolución del proveedor de una compañía para la
/// pasarela. Las credenciales se cifran con ISecretoCifradoService y solo se descifran en ResolveAsync.
/// </summary>
public sealed class DteSettingsService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _crypto;
    private readonly DteProviderRegistry _providers;

    public DteSettingsService(PortalSaasDbContext db, ISecretoCifradoService crypto, DteProviderRegistry providers)
    {
        _db = db;
        _crypto = crypto;
        _providers = providers;
    }

    public IReadOnlyList<string> ProviderCodes => _providers.Codes;

    public async Task<DteSettingsDto?> GetAsync(Guid companyId)
    {
        var s = await _db.CompanyDteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId);
        return s is null ? null : new DteSettingsDto(
            s.ProviderCode, s.Environment, s.BaseUrl, s.Username, !string.IsNullOrEmpty(s.SecretKey), s.IsEnabled,
            s.IssuerTaxId, s.IssuerName, s.IssuerActivity, s.IssuerActivityCode, s.IssuerAddress, s.IssuerCommune,
            s.IssuerCity, s.IssuerSiiBranchCode, s.UpdatedAt);
    }

    public async Task SaveAsync(Guid companyId, DteSettingsInput input)
    {
        if (!_providers.Contains(input.ProviderCode?.Trim() ?? ""))
            throw new InvalidOperationException($"El proveedor «{input.ProviderCode}» no está disponible en esta instalación.");
        if (!DteEnvironments.All.Contains(input.Environment))
            throw new InvalidOperationException("Ambiente inválido: use certificación o producción.");
        var url = ValidateBaseUrl(input.BaseUrl);
        if (string.IsNullOrWhiteSpace(input.Username))
            throw new InvalidOperationException("Ingrese el usuario del proveedor.");
        if (!Rut.TryNormalize(input.IssuerTaxId, out var issuerRut))
            throw new InvalidOperationException($"El RUT emisor «{input.IssuerTaxId}» no es válido.");
        foreach (var (value, label) in new[]
                 {
                     (input.IssuerName, "la razón social"), (input.IssuerActivity, "el giro"), (input.IssuerAddress, "la dirección"),
                     (input.IssuerCommune, "la comuna"), (input.IssuerCity, "la ciudad"),
                 })
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Ingrese {label} del emisor.");
        }
        if (input.IssuerActivityCode <= 0) throw new InvalidOperationException("Ingrese el código de actividad (Acteco) del emisor.");

        var s = await _db.CompanyDteSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId);
        var isNew = s is null;
        var newProvider = _providers.Get(input.ProviderCode.Trim()).Code;
        if (s is not null && (s.Environment != input.Environment || s.IssuerTaxId != issuerRut
                              || !string.Equals(s.ProviderCode, newProvider, StringComparison.OrdinalIgnoreCase)))
        {
            var inFlight = await CountInFlightAsync(companyId);
            if (inFlight > 0)
                throw new InvalidOperationException(
                    $"Hay {inFlight} emisión(es) en curso: no se puede cambiar el proveedor, el ambiente ni el RUT emisor hasta que terminen.");
        }
        s ??= new CompanyDteSetting { CompanyId = companyId };

        if (!string.IsNullOrEmpty(input.Password)) s.SecretKey = _crypto.Encrypt(input.Password);
        if (input.IsEnabled && string.IsNullOrEmpty(s.SecretKey))
            throw new InvalidOperationException("No se puede habilitar sin cargar la contraseña del proveedor.");

        s.ProviderCode = newProvider;
        s.Environment = input.Environment;
        s.BaseUrl = url.ToString().TrimEnd('/');
        s.Username = input.Username.Trim();
        s.IsEnabled = input.IsEnabled;
        s.IssuerTaxId = issuerRut;
        s.IssuerName = input.IssuerName.Trim();
        s.IssuerActivity = input.IssuerActivity.Trim();
        s.IssuerActivityCode = input.IssuerActivityCode;
        s.IssuerAddress = input.IssuerAddress.Trim();
        s.IssuerCommune = input.IssuerCommune.Trim();
        s.IssuerCity = input.IssuerCity.Trim();
        s.IssuerSiiBranchCode = input.IssuerSiiBranchCode;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        if (isNew) _db.CompanyDteSettings.Add(s);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid companyId)
    {
        var s = await _db.CompanyDteSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId);
        if (s is null) return;
        var pending = await CountInFlightAsync(companyId);
        if (pending > 0)
            throw new InvalidOperationException(
                $"Hay {pending} emisión(es) en curso con este proveedor: espere a que terminen antes de eliminar la configuración.");
        _db.CompanyDteSettings.Remove(s);
        await _db.SaveChangesAsync();
    }

    private Task<int> CountInFlightAsync(Guid companyId) =>
        _db.DteEmissions.CountAsync(x => x.CompanyId == companyId
            && (x.Status == DteEmissionStatuses.Pending || x.Status == DteEmissionStatuses.FolioReserved));

    /// <summary>Prueba de conexión de solo lectura. Los errores salen sin credenciales.</summary>
    public async Task TestAsync(Guid companyId)
    {
        var resolved = await ResolveAsync(companyId, requireEnabled: false)
            ?? throw new InvalidOperationException("Guarde primero la configuración con su contraseña.");
        try
        {
            await resolved.Provider.TestAsync(resolved.Ctx, CancellationToken.None);
        }
        catch (DteProviderUnavailableException ex)
        {
            throw new InvalidOperationException($"No se pudo conectar con {resolved.Provider.Code}: {ex.Message}");
        }
    }

    /// <summary>
    /// URL base del proveedor (el adaptador le agrega "/{servicio}"). Exige https, salvo un servidor de la red interna
    /// (loopback / localhost o IP privada 10/8, 172.16/12, 192.168/16), como el FACELE instalado en el servidor de la
    /// empresa: ahí las credenciales no salen de la red local. Rechaza la URL del WSDL o de un servicio indicando la base.
    /// </summary>
    internal static Uri ValidateBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("La URL del proveedor debe ser una dirección https completa.");
        var path = url.AbsolutePath.TrimEnd('/');
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        if (url.Query.Length > 0 || lastSegment.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            var basePath = lastSegment.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ? path[..path.LastIndexOf('/')] : path;
            throw new InvalidOperationException(
                $"Ingrese la URL base del servicio, sin el nombre del servicio ni \"?wsdl\": {url.GetLeftPart(UriPartial.Authority)}{basePath}");
        }
        if (url.Scheme == Uri.UriSchemeHttp && !IsInternalHost(url))
            throw new InvalidOperationException(
                "La URL del proveedor debe ser una dirección https completa (http solo se acepta para un servidor de la red interna).");
        return url;
    }

    private static bool IsInternalHost(Uri url)
    {
        if (url.IsLoopback) return true;
        if (!System.Net.IPAddress.TryParse(url.Host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Proveedor, credenciales descifradas y emisor de la compañía; null si no hay configuración habilitada.</summary>
    internal Task<(DteProviderContext Ctx, DteIssuerData Issuer, IDteProvider Provider)?> ResolveAsync(Guid companyId) =>
        ResolveAsync(companyId, requireEnabled: true);

    private async Task<(DteProviderContext Ctx, DteIssuerData Issuer, IDteProvider Provider)?> ResolveAsync(Guid companyId, bool requireEnabled)
    {
        var s = await _db.CompanyDteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId);
        if (s is null || (requireEnabled && !s.IsEnabled) || string.IsNullOrEmpty(s.SecretKey) || !_providers.Contains(s.ProviderCode))
            return null;
        var ctx = new DteProviderContext(s.BaseUrl, s.Username, _crypto.Decrypt(s.SecretKey), s.IssuerTaxId, s.Environment);
        var issuer = new DteIssuerData(s.IssuerTaxId, s.IssuerName, s.IssuerActivity, s.IssuerActivityCode, s.IssuerAddress,
            s.IssuerCommune, s.IssuerCity, s.IssuerSiiBranchCode);
        return (ctx, issuer, _providers.Get(s.ProviderCode));
    }
}
