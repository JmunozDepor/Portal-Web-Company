using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Zonas;

/// <summary>Mantenedor de zonas. Menú "zonas" (permisos: Despachos.zonas).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "zonas";

    public IReadOnlyList<Zone> Items { get; private set; } = Array.Empty<Zone>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();

    /// <summary>Nombres de las comunas de la zona (hasta <paramref name="max"/>, luego "…").</summary>
    public string CommuneNames(Zone zone, int max)
    {
        var names = zone.Communes
            .Select(zc => Communes.FirstOrDefault(c => c.Id == zc.CommuneId)?.Name)
            .Where(n => n is not null).OrderBy(n => n).ToList();
        return string.Join(", ", names.Take(max)) + (names.Count > max ? ", …" : "");
    }

    [BindProperty]
    public ZonaInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListZonesAsync(includeInactive: true);
        Communes = await _data.ListCommunesAsync();
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveZoneAsync(new Zone
            {
                Id = Input.Id,
                Code = Input.Code ?? "",
                Name = Input.Name ?? "",
                IsActive = Input.IsActive,
                Communes = Input.CommuneIds.Select(cid => new ZoneCommune { CommuneId = cid }).ToList(),
            }),
        Input.Id == 0 ? "Zona creado." : "Zona actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteZoneAsync(id), "Zona eliminado.");

    public sealed class ZonaInput
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
        public List<long> CommuneIds { get; set; } = new();
    }
}
