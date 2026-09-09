using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Usuarios;

public class EditarModel : AdminPageModelBase
{
    private readonly ITenantUserAdminService _usuarios;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IEmailSenderService _emailSenderService;
    private readonly ILogger<EditarModel> _logger;

    private readonly ICurrentCompanyAccessor _currentCompany;

    public EditarModel(
        ITenantUserAdminService usuarios,
        ICurrentUserContext currentUser,
        ICurrentCompanyAccessor currentCompany,
        IPasswordResetService passwordResetService,
        IEmailSenderService emailSenderService,
        ILogger<EditarModel> logger)
        : base(currentUser)
    {
        _usuarios = usuarios;
        _currentCompany = currentCompany;
        _passwordResetService = passwordResetService;
        _emailSenderService = emailSenderService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool EsNuevo { get; set; }
    public TenantUserDetailDto? Detalle { get; set; }

    public List<SelectListItem> Companies { get; set; } = [];
    public bool TieneCompanies { get; set; }
    public Guid SelectedCompanyId { get; set; }

    [TempData]
    public string? NuevaPasswordGenerada { get; set; }

    public List<MenuGroupOptionDto> AllMenuGroups { get; set; } = [];
    public List<MenuTreeNodeDto> MenuTree { get; set; } = [];
    public List<SelectListItem> ProfileOptions { get; set; } = [];

    [BindProperty]
    public PermissionsInputModel Permissions { get; set; } = new();

    [BindProperty]
    public Guid? DefaultCompanyId { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el nombre de usuario.")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el correo.")]
        [EmailAddress(ErrorMessage = "Correo inválido.")]
        [Display(Name = "Correo")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Administrador de la organización")]
        public bool IsAdmin { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Bloqueado")]
        public bool IsLocked { get; set; }
    }

    public sealed class PermissionsInputModel
    {
        public Guid CompanyId { get; set; }
        public List<long> SelectedMenuGroupIds { get; set; } = [];
        public Dictionary<long, long?> ProfileByMenu { get; set; } = [];
    }

    public async Task<IActionResult> OnGetAsync(Guid? id, Guid? companyId)
    {
        EsNuevo = id is null;

        if (!EsNuevo)
        {
            Detalle = await _usuarios.GetAsync(id!.Value);
            if (Detalle is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Username = Detalle.Username,
                Email = Detalle.Email,
                IsAdmin = Detalle.IsAdmin,
                IsActive = Detalle.IsActive,
                IsLocked = Detalle.IsLocked,
            };
            DefaultCompanyId = Detalle.DefaultCompanyId;

            await CargarPermisosAsync(id.Value, companyId);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(Guid? id)
    {
        EsNuevo = id is null;

        if (!ModelState.IsValid)
        {
            if (!EsNuevo)
            {
                Detalle = await _usuarios.GetAsync(id!.Value);
                DefaultCompanyId = Detalle?.DefaultCompanyId;
                await CargarPermisosAsync(id.Value, null);
            }

            return Page();
        }

        if (EsNuevo)
        {
            var resultado = await _usuarios.CreateAsync(Input.Username, Input.Email, Input.IsAdmin);
            if (!resultado.IsSuccess)
            {
                ModelState.AddModelError(string.Empty, resultado.Reason!);
                return Page();
            }

            MensajeExito = await EnviarInvitacionAsync(Input.Email)
                ? "Usuario creado correctamente. Se envió un correo de invitación para que cree su contraseña."
                : "Usuario creado correctamente, pero no se pudo enviar el correo de invitación (revisa la configuración de correo de la organización).";
            return RedirectToPage(new { id = resultado.UserId });
        }

        var actualizado = await _usuarios.UpdateAsync(id!.Value, Input.Email, Input.IsAdmin, Input.IsActive, Input.IsLocked);
        if (!actualizado.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, actualizado.Reason!);
            Detalle = await _usuarios.GetAsync(id.Value);
            await CargarPermisosAsync(id.Value, null);
            return Page();
        }

        MensajeExito = "Usuario actualizado correctamente.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGenerarPasswordAsync(Guid id, bool enviarPorCorreo)
    {
        var detalle = await _usuarios.GetAsync(id);
        if (detalle is null)
        {
            return NotFound();
        }

        var resultado = await _usuarios.GenerateAndSetPasswordAsync(id);
        if (!resultado.IsSuccess)
        {
            MensajeError = resultado.Reason;
            return RedirectToPage(new { id });
        }

        var password = resultado.NewPassword!;

        if (!enviarPorCorreo)
        {
            NuevaPasswordGenerada = password;
            MensajeExito = "Se generó una contraseña nueva. Cópiala ahora: no se volverá a mostrar.";
            return RedirectToPage(new { id });
        }

        var organizationId = CurrentUser.OrganizationId;
        try
        {
            await _emailSenderService.SendAsync(organizationId, new EmailMessage(
                detalle.Email,
                "Tu contraseña fue actualizada — Portal SaaS",
                $"""
                <p>Un administrador generó una contraseña nueva para tu cuenta en el Portal SaaS.</p>
                <p>Tu nueva contraseña es: <strong>{password}</strong></p>
                <p>Te recomendamos cambiarla la próxima vez que inicies sesión.</p>
                """));
            MensajeExito = "Se generó una contraseña nueva y se envió por correo al usuario.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el envío del correo de contraseña nueva para el usuario {UserId} de la organización {OrganizationId}", id, organizationId);
            MensajeError = "Se generó la contraseña nueva, pero no se pudo enviar el correo (revisa la configuración de correo de la organización).";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGuardarPermisosAsync(Guid id)
    {
        var resultado = await _usuarios.SavePermissionsAsync(id, Permissions.CompanyId, Permissions.SelectedMenuGroupIds, Permissions.ProfileByMenu);
        if (resultado.IsSuccess)
        {
            MensajeExito = "Permisos guardados correctamente.";
        }
        else
        {
            MensajeError = resultado.Reason;
        }

        return RedirectToPage(new { id, companyId = Permissions.CompanyId });
    }

    public async Task<IActionResult> OnPostGuardarCompaniaDefaultAsync(Guid id)
    {
        var resultado = await _usuarios.SetDefaultCompanyAsync(id, DefaultCompanyId);
        if (resultado.IsSuccess)
        {
            MensajeExito = "Compañía por defecto actualizada.";
        }
        else
        {
            MensajeError = resultado.Reason;
        }

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Junta IPasswordResetService + IEmailSenderService -- mismo patrón exacto que
    /// ForgotPassword.cshtml.cs del Host (el link de "crear tu contraseña" y el de
    /// "recuperarla" son el mismo mecanismo de token). El fallo de envío nunca bloquea
    /// la creación del usuario, solo cambia el mensaje que ve el admin.
    /// </summary>
    private async Task<bool> EnviarInvitacionAsync(string email)
    {
        var organizationId = CurrentUser.OrganizationId;
        var rawToken = await _passwordResetService.RequestResetAsync(organizationId, email);
        if (rawToken is null)
        {
            return false;
        }

        var resetLink = Url.PageLink("/Account/ResetPassword", values: new { token = rawToken });

        try
        {
            await _emailSenderService.SendAsync(organizationId, new EmailMessage(
                email,
                "Creá tu contraseña — Portal SaaS",
                $"""
                <p>Se creó una cuenta para vos en el Portal SaaS.</p>
                <p><a href="{resetLink}">Hacé clic acá para elegir tu contraseña</a> (el link vence en 1 hora).</p>
                """));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el envío del correo de invitación para el usuario {Email} de la organización {OrganizationId}", email, organizationId);
            return false;
        }
    }

    private async Task CargarPermisosAsync(Guid userId, Guid? companyId)
    {
        var companies = await _usuarios.ListCompaniesAsync();
        Companies = companies.Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString())).ToList();
        TieneCompanies = companies.Count > 0;

        if (!TieneCompanies)
        {
            return;
        }

        // Preferencia de compañía para la grilla de permisos: 1) la que vino en el
        // querystring (el selector de la pestaña); 2) la compañía con la que el admin
        // está conectado en esta sesión -- así los <select> arrancan en la empresa que
        // uno está viendo, no en otra, y se reduce el riesgo de asignar permisos en la
        // compañía equivocada; 3) la primera de la lista, como último recurso.
        SelectedCompanyId =
            companyId is { } id && companies.Any(c => c.Id == id) ? id
            : _currentCompany.HasCompany && companies.Any(c => c.Id == _currentCompany.CompanyId) ? _currentCompany.CompanyId
            : companies[0].Id;

        AllMenuGroups = (await _usuarios.ListMenuGroupsAsync()).ToList();
        MenuTree = (await _usuarios.ListMenuTreeAsync()).ToList();
        ProfileOptions = (await _usuarios.ListProfilesAsync()).Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();

        var permisos = await _usuarios.GetPermissionsAsync(userId, SelectedCompanyId);
        Permissions = new PermissionsInputModel
        {
            CompanyId = SelectedCompanyId,
            SelectedMenuGroupIds = permisos?.MenuGroupIds.ToList() ?? [],
            ProfileByMenu = permisos?.ProfileByMenu.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [],
        };
    }
}
