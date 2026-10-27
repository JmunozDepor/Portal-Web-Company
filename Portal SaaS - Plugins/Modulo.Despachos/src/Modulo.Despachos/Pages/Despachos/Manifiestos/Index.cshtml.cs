using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>Listado de manifiestos con filtros. Menú "manifiestos" (permisos: Despachos.manifiestos).</summary>
public sealed class IndexModel : DespachosPageModelBase
{
    private readonly IManifestService _manifests;
    private readonly IMasterDataService _data;
    private readonly ITenantUserAdminService? _users;

    public IndexModel(IManifestService manifests, IMasterDataService data, ICurrentUserContext currentUser,
        ITenantUserAdminService? users = null) : base(currentUser)
    {
        _manifests = manifests;
        _data = data;
        _users = users;
    }

    /// <summary>Usuarios de la organización por Id (columna "Actualizado por").</summary>
    private Dictionary<Guid, string> _userNames = new();

    /// <summary>Nombre de usuario; "—" sin usuario y "(usuario no disponible)" si ya no existe en la organización.</summary>
    public string UserName(Guid? id) =>
        id is not { } u ? "—" : _userNames.TryGetValue(u, out var name) ? name : "(usuario no disponible)";

    protected override string MenuCode => "manifiestos";

    public bool CanCreate { get; private set; }
    public IReadOnlyList<ManifestListItem> Items { get; private set; } = Array.Empty<ManifestListItem>();
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public long? CarrierId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Folio { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        Carriers = await _data.ListCarriersAsync(includeInactive: true);
        try
        {
            Items = await _manifests.ListAsync(new ManifestFilter(
                ParseDate(From), ParseDate(To),
                string.IsNullOrWhiteSpace(Status) ? null : Status, CarrierId,
                string.IsNullOrWhiteSpace(Folio) ? null : Folio.Trim()));
            if (_users is not null) _userNames = (await _users.ListAsync()).ToDictionary(u => u.Id, u => u.Username);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public string CarrierName(long id) => Carriers.FirstOrDefault(c => c.Id == id)?.Name ?? "—";
}
