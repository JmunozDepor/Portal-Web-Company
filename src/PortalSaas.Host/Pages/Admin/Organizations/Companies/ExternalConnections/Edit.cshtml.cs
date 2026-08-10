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
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;
    private readonly PluginManager _pluginManager;

    public EditModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService, PluginManager pluginManager)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
        _pluginManager = pluginManager;
    }

    public Company Company { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<string> EngineTypes => ModuleExternalConnectionEngineType.All;

    /// <summary>Ver el doc-comment equivalente en CreateModel -- acá siempre incluye el ModuleCode actual de la conexión, aunque ya esté "en uso" (por esta misma fila).</summary>
    public List<IModuloPortal> AvailablePlugins { get; private set; } = [];

    /// <summary>Nombre real del plugin cargado para Input.ModuleCode -- null si el código guardado no corresponde a ningún plugin cargado en este proceso (ej. plugin desinstalado/renombrado).</summary>
    public string? CurrentModuleName { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var connection = await _db.ModuleExternalConnections.FindAsync(id);
        if (connection is null)
        {
            return NotFound();
        }

        Company = (await _db.Companies.FindAsync(connection.CompanyId))!;

        Input = new InputModel
        {
            Id = connection.Id,
            ModuleCode = connection.ModuleCode,
            EngineType = connection.EngineType,
            Host = connection.Host,
            Port = connection.Port,
            DatabaseName = connection.DatabaseName,
            TechnicalUsername = connection.TechnicalUsername,
            IsActive = connection.IsActive,
        };

        await LoadAvailablePluginsAsync(connection.CompanyId, connection.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var connection = await _db.ModuleExternalConnections.FindAsync(Input.Id);
        if (connection is null)
        {
            return NotFound();
        }

        Company = (await _db.Companies.FindAsync(connection.CompanyId))!;
        await LoadAvailablePluginsAsync(connection.CompanyId, connection.Id);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var moduleCode = Input.ModuleCode.Trim();
        var enUso = await _db.ModuleExternalConnections.AnyAsync(c => c.CompanyId == connection.CompanyId && c.ModuleCode == moduleCode && c.Id != Input.Id);
        if (enUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ModuleCode)}", "Ya existe otra conexión para ese módulo en esta compañía.");
            return Page();
        }

        connection.ModuleCode = moduleCode;
        connection.EngineType = Input.EngineType;
        connection.Host = Input.Host.Trim();
        connection.Port = Input.Port;
        connection.DatabaseName = Input.DatabaseName.Trim();
        connection.TechnicalUsername = Input.TechnicalUsername.Trim();
        connection.IsActive = Input.IsActive;
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(Input.TechnicalSecretKey))
        {
            connection.TechnicalSecretKey = _secretoCifradoService.Encrypt(Input.TechnicalSecretKey);
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Companies/ExternalConnections/Index", new { companyId = connection.CompanyId });
    }

    private async Task LoadAvailablePluginsAsync(Guid companyId, long currentConnectionId)
    {
        var codesEnUso = await _db.ModuleExternalConnections
            .Where(c => c.CompanyId == companyId && c.Id != currentConnectionId)
            .Select(c => c.ModuleCode)
            .ToListAsync();
        AvailablePlugins = _pluginManager.ModulosCargados
            .Where(p => !codesEnUso.Contains(p.ModuleCode, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name)
            .ToList();

        CurrentModuleName = _pluginManager.ModulosCargados
            .FirstOrDefault(p => string.Equals(p.ModuleCode, Input.ModuleCode, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

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

        [DataType(DataType.Password)]
        [Display(Name = "Nueva clave técnica (dejar en blanco para no cambiarla)")]
        public string? TechnicalSecretKey { get; set; }

        [Display(Name = "Activa")]
        public bool IsActive { get; set; } = true;
    }
}
