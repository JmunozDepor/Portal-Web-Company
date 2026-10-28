using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.Aprobadores;

public sealed class GroupForm
{
    public long? Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsFinance { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Guid> Members { get; set; } = new();
}

/// <summary>Grupos de aprobadores (usuarios del portal) y el grupo de Finanzas (Rendiciones › Grupos de aprobación como referencia).</summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly IApproverGroupService _groups;
    private readonly ITenantUserAdminService _users;

    public IndexModel(IApproverGroupService groups, ITenantUserAdminService users, ICurrentUserContext currentUser) : base(currentUser)
    {
        _groups = groups;
        _users = users;
    }

    protected override string MenuCode => "aprobadores";

    public IReadOnlyList<ApproverGroupDto> Items { get; private set; } = Array.Empty<ApproverGroupDto>();
    public IReadOnlyList<(Guid Id, string Label)> Users { get; private set; } = Array.Empty<(Guid, string)>();
    public ApproverGroupDto? Selected { get; private set; }
    public bool OpenNew { get; private set; }
    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    [BindProperty] public GroupForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long? editar, bool nuevo = false, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        Items = await _groups.ListAsync(ct);
        Users = (await _users.ListAsync(ct)).Where(u => u.IsActive)
            .Select(u => (u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.Username : $"{u.FullName} ({u.Username})"))
            .OrderBy(u => u.Item2).ToList();
        Selected = editar is { } id ? Items.FirstOrDefault(g => g.Id == id) : null;
        OpenNew = nuevo && CanCreate;
        return Page();
    }

    public string UserLabel(Guid id) => Users.FirstOrDefault(u => u.Id == id).Label ?? id.ToString();

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(Input.Id is null ? PortalActions.Create : PortalActions.Edit)) return Forbid();
        try
        {
            await _groups.SaveAsync(new ApproverGroupInput(Input.Id, Input.Name, Input.IsFinance, Input.IsActive, Input.Members), ct);
            SuccessMessage = "Grupo guardado.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return Input.Id is { } id ? RedirectToPage(new { editar = id }) : RedirectToPage(new { nuevo = true });
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            await _groups.DeleteAsync(id, ct);
            SuccessMessage = "Grupo eliminado.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage();
    }
}
