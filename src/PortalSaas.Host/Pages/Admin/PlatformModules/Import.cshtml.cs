using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.PlatformModules;

/// <summary>
/// Importador de plugins -- sube un .zip con la carpeta de versión de un módulo
/// (ej. "Modulo.Rendiciones/1.0.0/*.dll", el mismo layout que ya genera el target
/// PublicarComoPlugin de cada plugin) y lo extrae directo a
/// artifacts/plugins/{Codigo}/{Version}/, la carpeta que PluginManager.DiscoverAndLoad
/// escanea al arrancar el Host (ver Program.cs). PluginManager NO soporta hot-reload
/// (carga una sola vez, en Build()) -- esta pantalla deja el .dll listo en disco y de
/// paso sincroniza el catálogo comercial (PlatformModule), pero el módulo recién queda
/// activo después de reiniciar el Host, mismo criterio ya documentado para
/// Modulo.Administracion/Modulo.Rendiciones en CLAUDE.md.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class ImportModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public ImportModel(PortalSaasDbContext db, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _db = db;
        _configuration = configuration;
        _environment = environment;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? MensajeExito { get; private set; }
    public string? MensajeError { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = Input.Code.Trim();
        var version = Input.Version.Trim();

        if (!System.Version.TryParse(version, out var parsedVersion))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Version)}", "La versión debe tener formato numérico, ej. 1.0.0.");
            return Page();
        }

        var artifactsFolder = ResolveArtifactsFolder();
        var moduleFolder = Path.Combine(artifactsFolder, code);
        var versionFolder = Path.Combine(moduleFolder, version);
        var mainDllExpectedName = $"{code}.dll";

        // Duplicidad: ya existe esta MISMA versión en disco -- exige confirmación
        // explícita (checkbox) antes de sobrescribir, para no pisar en silencio un
        // build que otra persona subió.
        if (Directory.Exists(versionFolder) && Directory.EnumerateFileSystemEntries(versionFolder).Any() && !Input.Overwrite)
        {
            MensajeError = $"Ya existe contenido en {versionFolder} -- marcá \"Sobrescribir\" si querés reemplazarlo a propósito.";
            return Page();
        }

        // Versión: si ya hay una versión igual o mayor cargada para este módulo (otra
        // carpeta de versión, no esta misma), PluginManager.DiscoverAndLoad siempre
        // carga la MÁS ALTA (Version.TryParse + OrderByDescending) -- importar una
        // versión menor o igual no tendría ningún efecto visible aunque la extracción
        // sea exitosa. Se avisa, no se bloquea (puede ser intencional, ej. reinstalar
        // la misma versión con un fix que no cambió el número).
        string? advertenciaVersion = null;
        if (Directory.Exists(moduleFolder))
        {
            var maxVersionExistente = Directory.GetDirectories(moduleFolder)
                .Select(p => new DirectoryInfo(p).Name)
                .Where(name => !string.Equals(name, version, StringComparison.OrdinalIgnoreCase))
                .Select(name => System.Version.TryParse(name, out var v) ? v : null)
                .Where(v => v is not null)
                .OrderByDescending(v => v)
                .FirstOrDefault();

            if (maxVersionExistente is not null && maxVersionExistente >= parsedVersion)
            {
                advertenciaVersion = $"Ya existe la versión {maxVersionExistente} de \"{code}\" -- PluginManager siempre carga la más " +
                    $"alta, así que esta importación ({parsedVersion}) no quedará activa hasta que subas una versión mayor a {maxVersionExistente}.";
            }
        }

        try
        {
            using var archive = new ZipArchive(Input.Package.OpenReadStream(), ZipArchiveMode.Read);

            var containsMainDll = archive.Entries.Any(e =>
                string.Equals(Path.GetFileName(e.Name), mainDllExpectedName, StringComparison.OrdinalIgnoreCase));
            if (!containsMainDll)
            {
                MensajeError = $"El .zip no contiene ningún archivo llamado \"{mainDllExpectedName}\" -- " +
                    "PluginManager espera que el DLL principal se llame igual que el código del módulo.";
                return Page();
            }

            Directory.CreateDirectory(versionFolder);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // carpeta, no archivo
                }

                var destino = Path.Combine(versionFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var destinoCarpeta = Path.GetDirectoryName(destino);
                if (destinoCarpeta is not null)
                {
                    Directory.CreateDirectory(destinoCarpeta);
                }

                entry.ExtractToFile(destino, overwrite: true);
            }
        }
        catch (InvalidDataException)
        {
            MensajeError = "El archivo subido no es un .zip válido.";
            return Page();
        }

        // Upsert del catálogo comercial -- si el código ya existía (ej. actualizar
        // versión de un módulo ya vendible), no se toca IsCore/ExclusiveOrganizationId.
        var existing = await _db.PlatformModules.FirstOrDefaultAsync(m => m.Code == code);
        if (existing is null)
        {
            _db.PlatformModules.Add(new PlatformModule { Code = code, Name = Input.Name.Trim(), IsCore = false });
            await _db.SaveChangesAsync();
        }

        MensajeExito = $"Plugin extraído en {versionFolder}. El módulo queda activo recién después de reiniciar el Host " +
            "(PluginManager solo carga plugins al arrancar)." + (advertenciaVersion is null ? "" : $" ⚠ {advertenciaVersion}");
        return Page();
    }

    private string ResolveArtifactsFolder()
    {
        var configured = _configuration["Plugins:ArtifactsFolder"];
        if (configured is { Length: > 0 })
        {
            return Path.IsPathRooted(configured) ? configured : Path.Combine(_environment.ContentRootPath, configured);
        }

        return Path.Combine(_environment.ContentRootPath, "artifacts", "plugins");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el código del módulo (debe coincidir con IModuloPortal.ModuleCode).")]
        [Display(Name = "Código del módulo (ej. \"Modulo.Rendiciones\")")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el nombre a mostrar si el módulo es nuevo.")]
        [Display(Name = "Nombre (solo se usa si el módulo es nuevo en el catálogo)")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa la versión.")]
        [Display(Name = "Versión (ej. 1.0.0)")]
        public string Version { get; set; } = string.Empty;

        [Required(ErrorMessage = "Subí el .zip del plugin.")]
        [Display(Name = "Paquete (.zip)")]
        public IFormFile Package { get; set; } = null!;

        [Display(Name = "Sobrescribir si ya existe esta misma versión en disco")]
        public bool Overwrite { get; set; }
    }
}
