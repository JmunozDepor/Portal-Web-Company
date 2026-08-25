using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Licenses;

/// <summary>
/// Alta manual de la fila LOCAL de OnPremiseLicense en una instalación on-premise, a
/// partir del ActivationKey que ya emitió el servidor Central (ver Create.cshtml.cs,
/// que corre en el rol Central). Sin esta fila local, LicenseActivatorBackgroundService
/// nunca encuentra qué licencia usar para hacer heartbeat (ver su comentario "¿falta el
/// alta manual inicial?") -- hueco real encontrado el 20 ago 2026 al migrar Comercial
/// Depor a Postgres, cerrado acá.
///
/// A diferencia de Create (rol Central, genera la clave), acá la clave la pega el
/// operador -- es la misma que Central ya generó y le entregó fuera de banda. Plan y
/// ExpiresAt locales son placeholders: ContractLimitService/OrganizationAccessGateService
/// para organizaciones on_premise SIEMPRE validan contra el SignedStatusToken firmado
/// por Central (ver esos servicios), nunca contra estas columnas crudas -- el primer
/// heartbeat exitoso las vuelve irrelevantes para la lógica real, solo importan para
/// satisfacer las columnas NOT NULL/FK de la tabla y para el nombre de plan mostrado en
/// mensajes de error mientras tanto.
///
/// RIESGO REAL encontrado probando esto (20 ago 2026): CheckLicenseAsync/ContractLimitService
/// siempre usan la licencia con IssuedAt más reciente de la organización -- crear una
/// activación nueva mientras ya existe una vigente (con su propio SignedStatusToken)
/// la reemplaza de inmediato como "la" licencia activa, aunque la nueva todavía no
/// tenga token (queda sin acceso hasta el próximo heartbeat exitoso). Mismo incidente
/// ya documentado en docs/11-ESTADO-PILOTO-DESARROLLO.md §6 del piloto real. Por eso
/// esta página exige una confirmación explícita cuando ya hay una licencia existente.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class ActivateModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public ActivateModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public List<SelectListItem> Plans { get; private set; } = [];
    public OnPremiseLicense? LicenciaExistente { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await CargarPlanesAsync();
        await CargarLicenciaExistenteAsync(organizationId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await CargarPlanesAsync();
        await CargarLicenciaExistenteAsync(organizationId);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var planValido = await _db.Plans.AnyAsync(p => p.Id == Input.PlanId && p.IsActive);
        if (!planValido)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PlanId)}", "Plan inválido.");
            return Page();
        }

        var activationKey = Input.ActivationKey.Trim();
        var yaExiste = await _db.OnPremiseLicenses.AnyAsync(l => l.ActivationKey == activationKey);
        if (yaExiste)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ActivationKey)}", "Ya existe una licencia local con esta clave de activación.");
            return Page();
        }

        // Ver el doc-comment de la clase -- crear una segunda activación reemplaza de
        // inmediato a la vigente (IssuedAt más reciente gana), así que se exige una
        // confirmación explícita cuando ya hay una.
        if (LicenciaExistente is not null && !Input.ConfirmoReemplazo)
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(Input.ConfirmoReemplazo)}",
                "Ya existe una licencia activa para esta organización -- confirma explícitamente que entendés el riesgo de reemplazarla.");
            return Page();
        }

        _db.OnPremiseLicenses.Add(new OnPremiseLicense
        {
            OrganizationId = organizationId,
            PlanId = Input.PlanId,
            ActivationKey = activationKey,
            // Placeholder -- el primer heartbeat exitoso trae el estado/límites reales
            // firmados por Central (ver doc-comment de la clase). Sin esto la columna
            // NOT NULL no se puede dejar vacía.
            ExpiresAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();

        TempData["MensajeExito"] = "Licencia activada localmente -- el próximo heartbeat automático (o el botón \"Validar licencia manualmente\") traerá el estado real firmado por Central.";
        return RedirectToPage("/Admin/Organizations/Licenses/Index", new { organizationId });
    }

    private async Task CargarPlanesAsync()
    {
        Plans = await _db.Plans
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString()))
            .ToListAsync();
    }

    private async Task CargarLicenciaExistenteAsync(Guid organizationId)
    {
        LicenciaExistente = await _db.OnPremiseLicenses
            .Include(l => l.Plan)
            .Where(l => l.OrganizationId == organizationId)
            .OrderByDescending(l => l.IssuedAt)
            .FirstOrDefaultAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa la clave de activación que entregó el servidor central.")]
        [Display(Name = "Clave de activación")]
        public string ActivationKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selecciona el plan que Central te indicó.")]
        [Display(Name = "Plan")]
        public long PlanId { get; set; }

        [Display(Name = "Confirmo que quiero reemplazar la licencia activa")]
        public bool ConfirmoReemplazo { get; set; }
    }
}
