using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;
    private readonly PluginManager _pluginManager;

    public CreateModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService, PluginManager pluginManager)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
        _pluginManager = pluginManager;
    }

    public Company Company { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<string> EngineTypes => ModuleExternalConnectionEngineType.All;

    /// <summary>
    /// Plugins REALMENTE cargados en este proceso (PluginManager.ModulosCargados, incluye
    /// externos tipo Modulo.Rendiciones/Modulo.GestionDistribucionGastos) que todavía no
    /// tienen conexión externa configurada para esta Company -- antes ModuleCode era
    /// texto libre ("Ingresa el código de módulo (IModuloPortal.ModuleCode)"), mismo
    /// riesgo de typo ya corregido en Pages/Admin/PlatformModules/Create.
    /// </summary>
    public List<IModuloPortal> AvailablePlugins { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        await LoadAvailablePluginsAsync(companyId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        await LoadAvailablePluginsAsync(companyId);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var moduleCode = Input.ModuleCode.Trim();
        var enUso = await _db.ModuleExternalConnections.AnyAsync(c => c.CompanyId == companyId && c.ModuleCode == moduleCode);
        if (enUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ModuleCode)}", "Ya existe una conexión para ese módulo en esta compañía.");
            return Page();
        }

        _db.ModuleExternalConnections.Add(new ModuleExternalConnection
        {
            CompanyId = companyId,
            ModuleCode = moduleCode,
            EngineType = Input.EngineType,
            Host = Input.Host.Trim(),
            Port = Input.Port,
            DatabaseName = Input.DatabaseName.Trim(),
            TechnicalUsername = Input.TechnicalUsername.Trim(),
            TechnicalSecretKey = _secretoCifradoService.Encrypt(Input.TechnicalSecretKey),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Companies/ExternalConnections/Index", new { companyId });
    }

    private async Task LoadAvailablePluginsAsync(Guid companyId)
    {
        var codesEnUso = await _db.ModuleExternalConnections.Where(c => c.CompanyId == companyId).Select(c => c.ModuleCode).ToListAsync();
        AvailablePlugins = _pluginManager.ModulosCargados
            .Where(p => !codesEnUso.Contains(p.ModuleCode, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name)
            .ToList();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí el plugin.")]
        [Display(Name = "Código de módulo")]
        public string ModuleCode { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Motor")]
        public string EngineType { get; set; } = ModuleExternalConnectionEngineType.SqlServer;

        [Required(ErrorMessage = "Ingresa el host.")]
        [Display(Name = "Host")]
        public string Host { get; set; } = string.Empty;

        [Required]
        [Range(1, 65535, ErrorMessage = "Puerto inválido.")]
        [Display(Name = "Puerto")]
        public int Port { get; set; } = 1433;

        [Required(ErrorMessage = "Ingresa el nombre de la base de datos.")]
        [Display(Name = "Base de datos")]
        public string DatabaseName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el usuario técnico.")]
        [Display(Name = "Usuario técnico")]
        public string TechnicalUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa la clave técnica.")]
        [DataType(DataType.Password)]
        [Display(Name = "Clave técnica")]
        public string TechnicalSecretKey { get; set; } = string.Empty;
    }
}
