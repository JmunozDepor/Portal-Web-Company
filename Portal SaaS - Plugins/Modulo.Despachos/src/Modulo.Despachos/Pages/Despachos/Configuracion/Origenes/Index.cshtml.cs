using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Origenes;

/// <summary>Mantenedor de orígenes. Menú "origenes" (permisos: Despachos.origenes).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "origenes";

    public IReadOnlyList<Origin> Items { get; private set; } = Array.Empty<Origin>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();

    public string CommuneName(long? id) => Communes.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    /// <summary>RUT de la compañía como emisor de DTE (valida el timbre al escanear documentos).</summary>
    public string? IssuerTaxId { get; private set; }

    [BindProperty]
    public string? IssuerInput { get; set; }

    [BindProperty]
    public OrigenInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListOriginsAsync(includeInactive: true);
        Communes = await _data.ListCommunesAsync();
        IssuerTaxId = await _data.GetIssuerTaxIdAsync();
    });

    /// <summary>Guarda el RUT emisor de la compañía. Exige EDIT sobre "origenes".</summary>
    public Task<IActionResult> OnPostSaveIssuerAsync() => SaveCoreAsync(1, () => _data.SaveIssuerTaxIdAsync(IssuerInput),
        "RUT emisor actualizado.");

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveOriginAsync(new Origin
            {
                Id = Input.Id,
                Code = Input.Code ?? "",
                Name = Input.Name ?? "",
                Address = NullIfBlank(Input.Address),
                CommuneId = Input.CommuneId,
                SapWarehouseCode = NullIfBlank(Input.SapWarehouseCode),
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Origen creado." : "Origen actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteOriginAsync(id), "Origen eliminado.");

    public sealed class OrigenInput
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public long? CommuneId { get; set; }
        public string? SapWarehouseCode { get; set; }
        public bool IsActive { get; set; }
    }
}
