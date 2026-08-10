using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Usuarios;

public class EditarModel : AdminPageModelBase
{
    private readonly ITenantUserAdminService _usuarios;

    public EditarModel(ITenantUserAdminService usuarios, ICurrentUserContext currentUser) : base(currentUser)
    {
        _usuarios = usuarios;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool EsNuevo { get; set; }
    public TenantUserDetailDto? Detalle { get; set; }

    public List<SelectListItem> Companies { get; set; } = [];
    public bool TieneCompanies { get; set; }
    public Guid SelectedCompanyId { get; set; }

    public List<MenuGroupOptionDto> AllMenuGroups { get; set; } = [];
    public List<LeafMenuDto> LeafMenus { get; set; } = [];
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

        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        public string? ConfirmPassword { get; set; }

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

        if (EsNuevo && string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Password)}", "La contraseña es obligatoria para un usuario nuevo.");
        }

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
            var resultado = await _usuarios.CreateAsync(Input.Username, Input.Email, Input.Password!, Input.IsAdmin);
            if (!resultado.IsSuccess)
            {
                ModelState.AddModelError(string.Empty, resultado.Reason!);
                return Page();
            }

            MensajeExito = "Usuario creado correctamente.";
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

    private async Task CargarPermisosAsync(Guid userId, Guid? companyId)
    {
        var companies = await _usuarios.ListCompaniesAsync();
        Companies = companies.Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString())).ToList();
        TieneCompanies = companies.Count > 0;

        if (!TieneCompanies)
        {
            return;
        }

        SelectedCompanyId = companyId is { } id && companies.Any(c => c.Id == id) ? id : companies[0].Id;

        AllMenuGroups = (await _usuarios.ListMenuGroupsAsync()).ToList();
        LeafMenus = (await _usuarios.ListLeafMenusAsync()).ToList();
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
