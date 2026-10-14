using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;
using QRCoder;

namespace Modulo.AuditoriaInventario.Pages.ConfiguracionCaptura;

/// <summary>
/// Formatos de código de barra que la PWA de captura acepta al escanear -- admin-only,
/// a diferencia de "Validar producto" que es un ajuste por equipo dentro de la propia
/// PWA (ver Mantenedor.tsx). Una fila por compañía en CaptureSettings; si nunca se
/// guardó, se muestran los tres formatos tildados (mismo default permisivo que usa
/// GetAjustesCapturaAsync para la PWA).
///
/// También vive acá "Conectar equipo": la URL pública de la PWA + su QR, para que un
/// equipo nuevo (teclado físico, sin autocompletar) entre escaneando en vez de
/// tipeando la URL a mano -- pedido explícito del dueño del proyecto, 2026-10-14.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public UrlInputModel UrlInput { get; set; } = new();

    /// <summary>PNG en base64 del QR de UrlInput.PwaUrl -- null si todavía no se guardó ninguna URL.</summary>
    public string? QrCodeBase64 { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var fila = await _db.CaptureSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == _currentCompany.CompanyId, ct);

        Input = new InputModel
        {
            AllowEan8 = fila?.AllowEan8 ?? true,
            AllowUpcA = fila?.AllowUpcA ?? true,
            AllowEan13 = fila?.AllowEan13 ?? true,
        };
        UrlInput = new UrlInputModel { PwaUrl = fila?.PwaUrl };
        QrCodeBase64 = GenerarQr(fila?.PwaUrl);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var fila = await _db.CaptureSettings.FirstOrDefaultAsync(s => s.CompanyId == _currentCompany.CompanyId, ct);
        if (fila is null)
        {
            fila = new CaptureSettings { CompanyId = _currentCompany.CompanyId };
            _db.CaptureSettings.Add(fila);
        }

        fila.AllowEan8 = Input.AllowEan8;
        fila.AllowUpcA = Input.AllowUpcA;
        fila.AllowEan13 = Input.AllowEan13;
        fila.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Configuración de captura guardada.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarUrlAsync(CancellationToken ct)
    {
        var url = UrlInput.PwaUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ErrorMessage = "La URL no es válida -- tiene que ser una dirección completa (ej. https://pwa.midominio.cl).";
            return RedirectToPage();
        }

        var fila = await _db.CaptureSettings.FirstOrDefaultAsync(s => s.CompanyId == _currentCompany.CompanyId, ct);
        if (fila is null)
        {
            fila = new CaptureSettings { CompanyId = _currentCompany.CompanyId };
            _db.CaptureSettings.Add(fila);
        }

        fila.PwaUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        fila.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "URL de conexión guardada.";
        return RedirectToPage();
    }

    /// <summary>
    /// Offline a propósito (QRCoder genera la imagen en el server, sin pegarle a
    /// ningún servicio de terceros) -- ECC nivel Q: tolera que el visor del equipo
    /// esté rayado/sucio sin dejar de leerse, a costa de una imagen un poco más
    /// densa (aceptable para una URL corta).
    /// </summary>
    private static string? GenerarQr(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(10);
        return Convert.ToBase64String(png);
    }

    public sealed class InputModel
    {
        public bool AllowEan8 { get; set; }
        public bool AllowUpcA { get; set; }
        public bool AllowEan13 { get; set; }
    }

    public sealed class UrlInputModel
    {
        public string? PwaUrl { get; set; }
    }
}
