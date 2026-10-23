using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.EquivalenciasComuna;

/// <summary>
/// Mantenedor de equivalencias de comuna (texto de SAP → comuna del catálogo). Se llenan solas al corregir la
/// comuna de un documento en el manifiesto. Menú "equivalencias-comuna" (permisos: Despachos.equivalencias-comuna).
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "equivalencias-comuna";

    public IReadOnlyList<CommuneAlias> Items { get; private set; } = Array.Empty<CommuneAlias>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();

    [BindProperty]
    public EquivalenciaInput Input { get; set; } = new();

    public string CommuneName(long id) => Communes.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListCommuneAliasesAsync();
        Communes = await _data.ListCommunesAsync();
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
    {
        var commune = string.IsNullOrWhiteSpace(Input.CommuneName) ? null : await _data.FindCommuneByNameAsync(Input.CommuneName);
        if (commune is null)
            throw new InvalidOperationException($"La comuna '{Input.CommuneName?.Trim()}' no existe en el catálogo. Elija una comuna de la lista.");
        await _data.SaveCommuneAliasAsync(new CommuneAlias { Id = Input.Id, Text = Input.Text ?? "", CommuneId = commune.Id });
    },
        Input.Id == 0 ? "Equivalencia creada." : "Equivalencia actualizada.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteCommuneAliasAsync(id), "Equivalencia eliminada.");

    public sealed class EquivalenciaInput
    {
        public long Id { get; set; }
        public string? Text { get; set; }
        public string? CommuneName { get; set; }
    }
}
