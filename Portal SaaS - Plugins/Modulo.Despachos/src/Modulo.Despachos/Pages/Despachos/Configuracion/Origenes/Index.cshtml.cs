using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Origenes;

/// <summary>
/// Mantenedor de orígenes, RUT emisor y ajustes de guías de despacho (Res. 154). Menú "origenes"
/// (permisos: Despachos.origenes). El proveedor DTE y sus credenciales se configuran en Administración (plataforma).
/// </summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;
    private readonly IDteEmissionService _dte;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser, IDteEmissionService dte) : base(currentUser)
    {
        _data = data;
        _dte = dte;
    }

    protected override string MenuCode => "origenes";

    public IReadOnlyList<Origin> Items { get; private set; } = Array.Empty<Origin>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();

    public string CommuneName(long? id) => Communes.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    /// <summary>RUT de la compañía como emisor de DTE (valida el timbre al escanear documentos).</summary>
    public string? IssuerTaxId { get; private set; }

    [BindProperty]
    public string? IssuerInput { get; set; }

    /// <summary>Ajustes de guías de despacho (Res. 154).</summary>
    [BindProperty]
    public GuideSettingsInput GuidesInput { get; set; } = new();

    /// <summary>Estado de la configuración DTE de la compañía (proveedor, ambiente, RUT emisor), de solo lectura.</summary>
    public DteConfigurationStatus? DteStatus { get; private set; }

    /// <summary>Valores SII de IndTraslado para el selector.</summary>
    public static readonly IReadOnlyList<(int Value, string Label)> TransferIndicators = new[]
    {
        (1, "1 - Operación constituye venta"), (2, "2 - Ventas por efectuar"), (3, "3 - Consignaciones"),
        (4, "4 - Entrega gratuita"), (5, "5 - Traslados internos"), (6, "6 - Otros traslados no venta"),
        (7, "7 - Devolución de mercaderías"), (8, "8 - Traslado para exportación"), (9, "9 - Venta para exportación"),
    };

    /// <summary>Valores SII de TipoDespacho para el selector.</summary>
    public static readonly IReadOnlyList<(int Value, string Label)> DispatchTypes = new[]
    {
        (1, "1 - Por cuenta del receptor"), (2, "2 - Por cuenta del emisor a instalaciones del cliente"),
        (3, "3 - Por cuenta del emisor a otras instalaciones"),
    };

    [BindProperty]
    public OrigenInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListOriginsAsync(includeInactive: true);
        Communes = await _data.ListCommunesAsync();
        IssuerTaxId = await _data.GetIssuerTaxIdAsync();
        var s = await _data.GetSettingsAsync();
        GuidesInput = new GuideSettingsInput
        {
            GuidesEnabled = s?.GuidesEnabled ?? false, TransferIndicator = s?.TransferIndicator ?? 1, DispatchType = s is null ? 2 : s.DispatchType,
            LineGrouping = s?.GuideLineGrouping ?? GuideLineGroupings.Sku,
            ItemCodePlacement = s?.GuideItemCodePlacement ?? GuideItemCodePlacements.Column,
            MaxLines = s?.GuideMaxLines ?? 60,
            Valued = s?.GuideValued ?? false,
        };
        try { DteStatus = await _dte.GetConfigurationAsync(); }
        catch (Exception ex)
        {
            DteStatus = new DteConfigurationStatus(false, null, null, null, Array.Empty<int>(), GetErrorMessage(ex));
        }
    });

    /// <summary>Guarda los ajustes de guías de despacho. Exige EDIT sobre "origenes".</summary>
    public Task<IActionResult> OnPostSaveGuidesAsync() => SaveCoreAsync(1,
        () => _data.SaveGuideSettingsAsync(GuidesInput.GuidesEnabled, GuidesInput.TransferIndicator, GuidesInput.DispatchType,
            GuidesInput.LineGrouping ?? GuideLineGroupings.Sku, GuidesInput.ItemCodePlacement ?? GuideItemCodePlacements.Column,
            GuidesInput.MaxLines, GuidesInput.Valued),
        "Ajustes de guías de despacho actualizados.");

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

    public sealed class GuideSettingsInput
    {
        public bool GuidesEnabled { get; set; }
        public int TransferIndicator { get; set; } = 1;
        public int? DispatchType { get; set; } = 2;
        public string? LineGrouping { get; set; } = GuideLineGroupings.Sku;
        public string? ItemCodePlacement { get; set; } = GuideItemCodePlacements.Column;
        public int MaxLines { get; set; } = 60;
        public bool Valued { get; set; }
    }

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
