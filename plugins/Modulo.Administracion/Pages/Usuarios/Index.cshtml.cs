using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Usuarios;

public class IndexModel : AdminPageModelBase
{
    private readonly ITenantUserAdminService _usuarios;

    public IndexModel(ITenantUserAdminService usuarios, ICurrentUserContext currentUser) : base(currentUser)
    {
        _usuarios = usuarios;
    }

    [BindProperty(SupportsGet = true)]
    public string? Texto { get; set; }

    public IReadOnlyList<TenantUserDto> Usuarios { get; set; } = Array.Empty<TenantUserDto>();

    public async Task OnGetAsync()
    {
        var todos = await _usuarios.ListAsync();

        Usuarios = string.IsNullOrWhiteSpace(Texto)
            ? todos
            : todos.Where(u => u.Username.Contains(Texto, StringComparison.OrdinalIgnoreCase)
                             || u.Email.Contains(Texto, StringComparison.OrdinalIgnoreCase))
                   .ToList();
    }

    public async Task<IActionResult> OnPostEliminarAsync(Guid id)
    {
        var resultado = await _usuarios.DeleteAsync(id);
        if (resultado.IsSuccess)
        {
            MensajeExito = "Usuario eliminado.";
        }
        else
        {
            MensajeError = resultado.Reason;
        }

        return RedirectToPage();
    }
}
