using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using PortalSaas.Host.Infraestructura;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Segundo paso del login de tenant -- fija la compañía SAP activa de la sesión (ver
/// ICurrentCompanyAccessor), solo cuando la organización tiene al menos una. Una vez
/// fijada, no se puede volver a elegir sin logout salvo por /Account/SwitchCompany (ver
/// Login.cshtml.cs) -- si el usuario ya tiene el claim "CompanyId", esta página redirige
/// directo, nunca deja re-elegir. Si el usuario marcó "Recordar esta compañía" en una
/// visita anterior (UserPreference.DefaultCompanyId), y esa compañía sigue siendo válida
/// y accesible, este paso se salta directo -- sin eso, el usuario tendría que re-elegir
/// en cada login, que es justo la fricción que "Recordar" existe para evitar.
/// </summary>
[Authorize]
public class SelectCompanyModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanySessionActivator _activator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SelectCompanyModel> _logger;

    public SelectCompanyModel(PortalSaasDbContext db, ICompanySessionActivator activator, IConfiguration configuration, ILogger<SelectCompanyModel> logger)
    {
        _db = db;
        _activator = activator;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Companies { get; private set; } = [];

    private List<Company> _companyEntities = [];

    /// <summary>
    /// True cuando Tenant:DefaultCompanyCode (appsettings, ver el comentario ahí) resolvió
    /// a una compañía real de esta organización -- el selector se muestra bloqueado con esa
    /// compañía ya elegida (Input.CompanyId), en vez de dejar elegir. Distinto de
    /// UserPreference.DefaultCompanyId (arriba, OnGetAsync): ese auto-activa y NUNCA
    /// muestra esta página; esto sí la muestra, solo que sin dejar cambiar la compañía --
    /// pensado para un perfil OnPremise de una sola compañía real (ej. Comercial Depor),
    /// donde igual conviene que el paso quede visible (confirma sesión/company antes de
    /// entrar) pero no tiene sentido dejar "elegir" entre opciones que no existen.
    /// </summary>
    public bool CompanyLocked { get; private set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.FindFirst("CompanyId") is not null)
        {
            return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var defaultCompanyId = await _db.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => p.DefaultCompanyId)
            .FirstOrDefaultAsync();

        if (defaultCompanyId is { } companyId)
        {
            var activado = await _activator.TryActivateAsync(HttpContext, companyId);
            if (activado is not null)
            {
                return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
            }

            // Default configurado (UserPreference.DefaultCompanyId) pero sin acceso real
            // (compañía desactivada/borrada, o sin fila en UserMenuGroups/UserMenuProfiles
            // para el usuario -- ver CompanySessionActivator.TryActivateAsync). Antes esto
            // caía en silencio al selector normal con un mensaje genérico recién al
            // postear -- ahora se explica la causa acá mismo, apenas se detecta.
            var organizationId = Guid.Parse(User.FindFirstValue("OrganizationId")!);
            var companiaDefault = await _db.Companies
                .FirstOrDefaultAsync(c => c.Id == companyId && c.OrganizationId == organizationId);
            var nombreDefault = companiaDefault is not null ? $"{companiaDefault.Code} — {companiaDefault.Name}" : "configurada";

            _logger.LogWarning(
                "Usuario {UserId} no pudo activar su compañía por defecto {CompanyId} al loguearse.",
                userId, companyId);

            // TryActivateAsync devuelve null tanto por falta de acceso (sin fila en
            // UserMenuGroups/UserMenuProfiles) como porque la compañía está inactiva o fue
            // borrada -- distinguimos acá para no atribuir siempre "falta de permisos" a un
            // fallo que puede deberse a otra causa.
            if (companiaDefault is null || !companiaDefault.IsActive)
            {
                ErrorMessage = $"Tu compañía por defecto ({nombreDefault}) ya no está disponible. Contacta a tu administrador.";
            }
            else
            {
                ErrorMessage = $"Tu compañía por defecto ({nombreDefault}) no tiene permisos configurados. Contacta a tu administrador.";
            }
        }

        Input.ReturnUrl = returnUrl;
        await CargarCompaniasConAccesoAsync(userId);
        AplicarCompaniaBloqueadaSiCorresponde();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.FindFirst("CompanyId") is not null)
        {
            return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : Url.Content("~/Home/Index"));
        }

        var userIdPost = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await CargarCompaniasConAccesoAsync(userIdPost);

        // Con el selector bloqueado, el valor real NUNCA se toma de lo que mandó el POST
        // (un <select disabled> no viaja en el body, pero igual no hay que confiar en un
        // Input.CompanyId manipulado a mano) -- se vuelve a resolver server-side desde
        // Tenant:DefaultCompanyCode, pisando cualquier valor posteado.
        AplicarCompaniaBloqueadaSiCorresponde();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var company = await _activator.TryActivateAsync(HttpContext, Input.CompanyId);
        if (company is null)
        {
            ErrorMessage = "Compañía inválida o sin acceso.";
            return Page();
        }

        await GuardarPreferenciaDefaultAsync(userId, Input.Recordar ? company.Id : null);

        return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : Url.Content("~/Home/Index"));
    }

    private async Task GuardarPreferenciaDefaultAsync(Guid userId, Guid? defaultCompanyId)
    {
        var preference = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (preference is null)
        {
            preference = new UserPreference { UserId = userId };
            _db.UserPreferences.Add(preference);
        }

        preference.DefaultCompanyId = defaultCompanyId;
        await _db.SaveChangesAsync();
    }

    /// <summary>Ver el doc-comment de CompanyLocked. Sin match (código vacío, no configurado,
    /// o no corresponde a ninguna compañía activa de ESTA organización) no hace nada -- el
    /// selector queda como siempre, libre.</summary>
    private void AplicarCompaniaBloqueadaSiCorresponde()
    {
        var defaultCompanyCode = _configuration["Tenant:DefaultCompanyCode"];
        if (string.IsNullOrWhiteSpace(defaultCompanyCode))
        {
            return;
        }

        var match = _companyEntities.FirstOrDefault(c => string.Equals(c.Code, defaultCompanyCode, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        Input.CompanyId = match.Id;
        CompanyLocked = true;
    }

    private async Task CargarCompaniasConAccesoAsync(Guid userId)
    {
        var organizationId = Guid.Parse(User.FindFirstValue("OrganizationId")!);
        var isAdmin = bool.Parse(User.FindFirstValue("IsAdmin")!);

        var query = _db.Companies.Where(c => c.OrganizationId == organizationId && c.IsActive);

        // Mismo criterio que CompanySessionActivator/CompanySwitcherViewComponent -- un
        // admin ve todas, un usuario normal solo las que tiene acceso real vía
        // UserMenuGroups/UserMenuProfiles. Sin esto, el selector mostraría compañías que
        // igual rechazaría OnPostAsync al intentar activarlas.
        if (!isAdmin)
        {
            query = query.Where(c =>
                _db.UserMenuGroups.Any(g => g.UserId == userId && g.CompanyId == c.Id) ||
                _db.UserMenuProfiles.Any(p => p.UserId == userId && p.CompanyId == c.Id));
        }

        _companyEntities = await query.OrderBy(c => c.Code).ToListAsync();
        Companies = _companyEntities
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString()))
            .ToList();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Selecciona una compañía.")]
        [Display(Name = "Compañía")]
        public Guid CompanyId { get; set; }

        [Display(Name = "Recordar esta compañía")]
        public bool Recordar { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
