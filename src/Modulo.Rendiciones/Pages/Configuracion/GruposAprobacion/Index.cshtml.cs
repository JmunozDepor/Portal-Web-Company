using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.GruposAprobacion;

/// <summary>
/// Maestro-detalle: grupos a la izquierda, equipo + cadena de 2 niveles del grupo
/// seleccionado a la derecha. A diferencia de Compras no hay un intermediario --
/// miembros y niveles apuntan directo al USUARIO del portal.
/// </summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpenseApprovalGroupService _groups;
    private readonly ITenantUserAdminService _users;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseApprovalGroupService groups, ITenantUserAdminService users, ICurrentCompanyAccessor currentCompany)
    {
        _groups = groups;
        _users = users;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public long? GroupId { get; set; }

    [BindProperty]
    public GroupInput Group { get; set; } = new();

    [BindProperty]
    public string? SelectedUserId { get; set; }

    public IReadOnlyList<ExpenseApprovalGroup> Groups { get; set; } = Array.Empty<ExpenseApprovalGroup>();
    public IReadOnlyDictionary<long, int> MemberCountByGroupId { get; set; } = new Dictionary<long, int>();
    public IReadOnlyDictionary<long, int> LevelCountByGroupId { get; set; } = new Dictionary<long, int>();
    public List<SelectListItem> AvailableUsers { get; set; } = new();
    public Dictionary<Guid, string> NameByUserId { get; set; } = new();

    public IReadOnlyList<Guid> GroupMembers { get; set; } = Array.Empty<Guid>();
    public IReadOnlyDictionary<int, Guid> GroupLevels { get; set; } = new Dictionary<int, Guid>();

    public sealed class GroupInput
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);

        if (GroupId is { } groupId)
        {
            var group = await _groups.GetAsync(groupId, _currentCompany.CompanyId, ct);
            if (group is null)
                return NotFound();

            Group = new GroupInput { Name = group.Name };
            await LoadDetailAsync(groupId, ct);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCrearGrupoAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadListsAsync(ct);
            return Page();
        }

        var id = await _groups.CreateAsync(_currentCompany.CompanyId, Group.Name, ct);
        SuccessMessage = "Grupo de aprobación creado. Ahora podés asignarle su equipo y cadena.";
        return RedirectToPage(new { groupId = id });
    }

    public async Task<IActionResult> OnPostAgregarMiembroAsync(long groupId, CancellationToken ct)
    {
        try
        {
            if (Guid.TryParse(SelectedUserId, out var userId))
                await _groups.AddMemberAsync(groupId, _currentCompany.CompanyId, userId, ct);
            SuccessMessage = "Colaborador agregado al equipo.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { groupId });
    }

    public async Task<IActionResult> OnPostQuitarMiembroAsync(long groupId, Guid userId, CancellationToken ct)
    {
        try
        {
            await _groups.RemoveMemberAsync(groupId, _currentCompany.CompanyId, userId, ct);
            SuccessMessage = "Colaborador quitado del equipo.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { groupId });
    }

    public async Task<IActionResult> OnPostAsignarNivelAsync(long groupId, int nivel, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(SelectedUserId, out var userId))
                throw new InvalidOperationException("Elegí un usuario para el nivel.");

            await _groups.SetLevelAsync(groupId, _currentCompany.CompanyId, nivel, userId, ct);
            SuccessMessage = $"Aprobador de nivel {nivel} asignado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { groupId });
    }

    public async Task<IActionResult> OnPostQuitarNivelAsync(long groupId, int nivel, CancellationToken ct)
    {
        try
        {
            await _groups.RemoveLevelAsync(groupId, _currentCompany.CompanyId, nivel, ct);
            SuccessMessage = $"Aprobador de nivel {nivel} quitado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { groupId });
    }

    private async Task LoadDetailAsync(long groupId, CancellationToken ct)
    {
        GroupMembers = (await _groups.ListMembersAsync(groupId, ct)).Select(m => m.UserId).ToList();
        GroupLevels = await _groups.GetLevelsAsync(groupId, ct);
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        Groups = await _groups.ListAsync(_currentCompany.CompanyId, ct);

        // N pequeño (grupos de aprobación de una compañía) -- un conteo por grupo
        // alcanza para la columna del listado, no justifica un método "bulk" nuevo.
        var memberCounts = new Dictionary<long, int>();
        var levelCounts = new Dictionary<long, int>();
        foreach (var g in Groups)
        {
            memberCounts[g.Id] = (await _groups.ListMembersAsync(g.Id, ct)).Count;
            levelCounts[g.Id] = (await _groups.GetLevelsAsync(g.Id, ct)).Count;
        }
        MemberCountByGroupId = memberCounts;
        LevelCountByGroupId = levelCounts;

        var users = await _users.ListAsync(ct);
        AvailableUsers = users.Select(u => new SelectListItem(u.Username, u.Id.ToString())).ToList();
        NameByUserId = users.ToDictionary(u => u.Id, u => u.Username);
    }
}
