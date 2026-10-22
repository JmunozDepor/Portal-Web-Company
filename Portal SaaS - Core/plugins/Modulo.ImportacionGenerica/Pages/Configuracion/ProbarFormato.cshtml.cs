using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Configuracion;

/// <summary>
/// "Probar formato" -- valida un formato de importación (GenericImportConfig) contra el
/// SAP de la compañía activa sin crear documentos (pedido del dueño del proyecto,
/// 2026-10-09). Dos pruebas, ver IGenericImportFormatValidator:
/// - Validar esquema: sin archivo (campos obligatorios/ignorados, valores fijos, UDF en CUFD).
/// - Probar con archivo: Excel de ejemplo procesado con ESTE formato + envío simulado del
///   JSON del primer documento válido (Service Layer lo valida y lo revierte).
/// Página aparte de Configuracion/Index a propósito -- ese formulario ya es grande y
/// rehace su estado en cada POST; acá cada prueba es un POST simple sobre el formato
/// YA guardado.
/// </summary>
public sealed class ProbarFormatoModel : PageModelBaseAdmin
{
    protected override string MenuCode => "ImportacionGenerica.configuracion";

    private readonly IGenericImportConfigService _configs;
    private readonly IGenericImportFormatValidator _validator;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;
    private readonly ILogger<ProbarFormatoModel> _logger;

    public ProbarFormatoModel(ICurrentUserContext currentUser, IGenericImportConfigService configs, IGenericImportFormatValidator validator,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers, ILogger<ProbarFormatoModel> logger) : base(currentUser)
    {
        _configs = configs;
        _validator = validator;
        _customers = customers;
        _suppliers = suppliers;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    /// <summary>Socio de prueba -- solo para formatos estándar (sin socio propio) de Venta/Compra, que lo exigen para simular el envío.</summary>
    [BindProperty]
    public string? BusinessPartnerCardCode { get; set; }

    public GenericImportConfigDto? Config { get; private set; }

    /// <summary>Qué prueba se corrió en este POST -- "esquema" o "archivo"; null en el GET.</summary>
    public string? TestKind { get; private set; }

    public IReadOnlyList<GenericImportFormatCheckDto> Checks { get; private set; } = [];
    public GenericImportFormatTestResultDto? FileResult { get; private set; }

    public bool NeedsTestPartner => Config is not null && Config.BusinessPartnerCardCode is null
        && !Config.BusinessPartnerFromFile && Config.Module != GenericImportModule.Inventory;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Config = await _configs.GetAsync(Id, ct);
        return Config is null ? RedirectToPage("/Configuracion/Index") : Page();
    }

    public async Task<IActionResult> OnPostSchemaAsync(CancellationToken ct)
    {
        Config = await _configs.GetAsync(Id, ct);
        if (Config is null)
        {
            return RedirectToPage("/Configuracion/Index");
        }

        TestKind = "esquema";
        try
        {
            Checks = await _validator.ValidateSchemaAsync(Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validando el esquema del formato {ConfigId} contra SAP.", Id);
            ErrorMessage = GetErrorMessage(ex);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostFileAsync(IFormFile? file, CancellationToken ct)
    {
        Config = await _configs.GetAsync(Id, ct);
        if (Config is null)
        {
            return RedirectToPage("/Configuracion/Index");
        }

        TestKind = "archivo";
        if (file is null || file.Length == 0)
        {
            ErrorMessage = "Elegí un archivo Excel de ejemplo antes de probar.";
            return Page();
        }

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            stream.Position = 0;
            FileResult = await _validator.TestWithFileAsync(Id, stream, CurrentUser.Username, BusinessPartnerCardCode, ct);
            Checks = FileResult.Checks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error probando el formato {ConfigId} con archivo.", Id);
            ErrorMessage = GetErrorMessage(ex);
        }

        return Page();
    }

    /// <summary>Socio de prueba -- mismo criterio que Importar (Compra -> Proveedor, Venta/Inventario -> Cliente).</summary>
    public async Task<JsonResult> OnGetSearchBusinessPartnersAsync(string text, GenericImportModule module, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        if (module == GenericImportModule.Purchase)
        {
            var suppliers = await _suppliers.ListAsync(new SupplierFilter(text, Limit: 30), ct);
            return new JsonResult(suppliers.Select(s => new { s.CardCode, s.CardName }));
        }

        var customers = await _customers.ListAsync(new CustomerFilter(text, Limit: 30), ct);
        return new JsonResult(customers.Select(c => new { c.CardCode, c.CardName }));
    }
}
