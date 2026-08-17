using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Host.Pages.Admin.Integraciones;

/// <summary>
/// Alta y edición de IntegrationDefinition -- hasta esta entrega, Admin/Integraciones
/// solo permitía listar y ejecutar filas ya existentes en la base (ver Index.cshtml.cs),
/// sin ninguna forma de crearlas sin tocar la base a mano. La config del conector
/// (ConectorConfigCifrado) cambia de forma según ConectorTipo -- ver
/// docs/superpowers/specs/2026-08-16-admin-integraciones-alta-design.md.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class NuevoModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public NuevoModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
    }

    public bool EsEdicion { get; private set; }
    public List<Company> Companies { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        await LoadCompaniesAsync();

        if (id is null)
        {
            return Page();
        }

        var definicion = await _db.IntegrationDefinitions.FindAsync(id.Value);
        if (definicion is null)
        {
            return NotFound();
        }

        EsEdicion = true;
        Input = new InputModel
        {
            Id = definicion.Id,
            Nombre = definicion.Nombre,
            CompanyId = definicion.CompanyId,
            ModuloOrigen = definicion.ModuloOrigen,
            EntidadNegocio = definicion.EntidadNegocio,
            ConectorTipo = definicion.ConectorTipo,
            Direccion = definicion.Direccion,
            Activo = definicion.Activo,
            ProgramacionCron = definicion.ProgramacionCron,
        };

        // Precarga los campos no-secretos del bloque de config -- la Clave/password
        // queda deliberadamente vacía (write-only, ver ISecretoCifradoService): si el
        // admin no la toca, OnPostAsync conserva la que ya estaba guardada.
        if (definicion.ConectorTipo == IntegrationConectorTipo.WmsCloud && !string.IsNullOrEmpty(definicion.ConectorConfigCifrado))
        {
            var configActual = JsonSerializer.Deserialize<WmsCloudConfigInput>(_secretoCifradoService.Decrypt(definicion.ConectorConfigCifrado));
            if (configActual is not null)
            {
                Input.ApiUrl = configActual.ApiUrl;
                Input.Usuario = configActual.Usuario;
                Input.ClientEnvCode = configActual.ClientEnvCode;
                Input.ParentCompanyCode = configActual.ParentCompanyCode;
            }
        }
        else if (definicion.ConectorTipo == IntegrationConectorTipo.Sap && !string.IsNullOrEmpty(definicion.ConectorConfigCifrado))
        {
            var configActual = JsonSerializer.Deserialize<SapConfigInput>(_secretoCifradoService.Decrypt(definicion.ConectorConfigCifrado));
            if (configActual is not null)
            {
                Input.TipoEntidad = configActual.TipoEntidad;
                Input.Filtro = configActual.Filtro;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadCompaniesAsync();
        EsEdicion = Input.Id is not null;

        if (Input.ConectorTipo == IntegrationConectorTipo.Sap && string.IsNullOrWhiteSpace(Input.TipoEntidad))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TipoEntidad)}", "Ingresá el tipo de entidad SAP.");
        }
        if (Input.ConectorTipo == IntegrationConectorTipo.WmsCloud)
        {
            if (string.IsNullOrWhiteSpace(Input.ApiUrl))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ApiUrl)}", "Ingresá la URL de la API.");
            if (string.IsNullOrWhiteSpace(Input.Usuario))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Usuario)}", "Ingresá el usuario.");
            if (string.IsNullOrWhiteSpace(Input.ClientEnvCode))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ClientEnvCode)}", "Ingresá el ClientEnvCode.");
            if (string.IsNullOrWhiteSpace(Input.ParentCompanyCode))
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ParentCompanyCode)}", "Ingresá el ParentCompanyCode.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        IntegrationDefinition definicion;
        if (Input.Id is { } id)
        {
            var existente = await _db.IntegrationDefinitions.FindAsync(id);
            if (existente is null)
            {
                return NotFound();
            }
            definicion = existente;
        }
        else
        {
            definicion = new IntegrationDefinition();
            _db.IntegrationDefinitions.Add(definicion);
        }

        definicion.Nombre = Input.Nombre.Trim();
        definicion.CompanyId = Input.CompanyId;
        definicion.ModuloOrigen = Input.ModuloOrigen.Trim();
        definicion.EntidadNegocio = Input.EntidadNegocio.Trim();
        definicion.ConectorTipo = Input.ConectorTipo;
        definicion.Direccion = Input.Direccion;
        definicion.Activo = Input.Activo;
        definicion.ProgramacionCron = string.IsNullOrWhiteSpace(Input.ProgramacionCron) ? null : Input.ProgramacionCron.Trim();

        definicion.ConectorConfigCifrado = await ArmarConfigCifradaAsync(definicion);

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Integraciones/Index");
    }

    /// <summary>
    /// Arma el JSON de config según ConectorTipo y lo cifra. Para WmsCloud, si Input.Clave
    /// vino vacía (el admin no la tocó al editar), conserva la clave ya cifrada de la fila
    /// existente en vez de sobreescribirla con vacío -- mismo criterio write-only que
    /// ExternalConnections/Edit.cshtml.cs usa para TechnicalSecretKey.
    /// </summary>
    private async Task<string> ArmarConfigCifradaAsync(IntegrationDefinition definicion)
    {
        if (Input.ConectorTipo == IntegrationConectorTipo.Sap)
        {
            var filtro = string.IsNullOrWhiteSpace(Input.Filtro) ? null : Input.Filtro.Trim();
            var json = JsonSerializer.Serialize(new SapConfigInput(Input.TipoEntidad!.Trim(), filtro));
            return _secretoCifradoService.Encrypt(json);
        }

        if (Input.ConectorTipo == IntegrationConectorTipo.WmsCloud)
        {
            var clave = Input.Clave;
            if (string.IsNullOrWhiteSpace(clave))
            {
                var filaActual = Input.Id is { } id
                    ? await _db.IntegrationDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id)
                    : null;
                if (filaActual is { ConectorTipo: IntegrationConectorTipo.WmsCloud } && !string.IsNullOrEmpty(filaActual.ConectorConfigCifrado))
                {
                    var previo = JsonSerializer.Deserialize<WmsCloudConfigInput>(_secretoCifradoService.Decrypt(filaActual.ConectorConfigCifrado));
                    clave = previo?.Clave ?? string.Empty;
                }
            }

            var json = JsonSerializer.Serialize(new WmsCloudConfigInput(
                Input.ApiUrl!.Trim(), Input.Usuario!.Trim(), clave ?? string.Empty, Input.ClientEnvCode!.Trim(), Input.ParentCompanyCode!.Trim()));
            return _secretoCifradoService.Encrypt(json);
        }

        // Rest/Archivo: sin conector implementado todavía (ver spec, "fuera de alcance") --
        // se guarda vacío en vez de forzar un formulario para un tipo que el motor no sabe
        // ejecutar.
        return string.Empty;
    }

    private async Task LoadCompaniesAsync()
    {
        Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
    }

    /// <summary>Espejo de WmsCloudConnector.WmsCloudConfig (record privado en Modulo.Wms) -- no se referencia el tipo del plugin desde Core, se serializa/deserializa por forma.</summary>
    private sealed record WmsCloudConfigInput(string ApiUrl, string Usuario, string Clave, string ClientEnvCode, string ParentCompanyCode);

    /// <summary>Espejo de SapDocumentConnector.SapWmsOutboundConfig (record privado). Filtro null/vacío = usa el filtro por defecto de la entidad (ver SapDocumentConnector.FiltroPorDefecto*).</summary>
    private sealed record SapConfigInput(string TipoEntidad, string? Filtro = null);

    public sealed class InputModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Ingresá el nombre.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Elegí la compañía.")]
        [Display(Name = "Compañía")]
        public Guid CompanyId { get; set; }

        [Required(ErrorMessage = "Ingresá el módulo origen.")]
        [Display(Name = "Módulo origen")]
        public string ModuloOrigen { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresá la entidad de negocio.")]
        [Display(Name = "Entidad de negocio")]
        public string EntidadNegocio { get; set; } = string.Empty;

        [Display(Name = "Tipo de conector")]
        public IntegrationConectorTipo ConectorTipo { get; set; } = IntegrationConectorTipo.Sap;

        [Display(Name = "Dirección")]
        public IntegrationDireccion Direccion { get; set; } = IntegrationDireccion.Bajada;

        [Display(Name = "Activa")]
        public bool Activo { get; set; } = true;

        [Display(Name = "Programación cron")]
        public string? ProgramacionCron { get; set; }

        // Bloque Sap
        [Display(Name = "Tipo de entidad SAP")]
        public string? TipoEntidad { get; set; }

        [Display(Name = "Filtro OData (vacío = usar el filtro por defecto)")]
        public string? Filtro { get; set; }

        // Bloque WmsCloud
        [Display(Name = "URL de la API")]
        public string? ApiUrl { get; set; }

        [Display(Name = "Usuario")]
        public string? Usuario { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Clave (dejar en blanco para no cambiarla)")]
        public string? Clave { get; set; }

        [Display(Name = "ClientEnvCode")]
        public string? ClientEnvCode { get; set; }

        [Display(Name = "ParentCompanyCode")]
        public string? ParentCompanyCode { get; set; }
    }
}
