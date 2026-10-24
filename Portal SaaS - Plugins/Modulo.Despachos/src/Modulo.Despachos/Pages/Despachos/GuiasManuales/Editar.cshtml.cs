using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.GuiasManuales;

/// <summary>Línea del formulario (números como texto: el JS los envía en formato invariante).</summary>
public sealed class LineForm
{
    public string? ItemCode { get; set; }
    public string? Description { get; set; }
    public string? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? UnitPrice { get; set; }
}

public sealed class ReferenceForm
{
    public string? DocumentType { get; set; }
    public long? Folio { get; set; }
    public string? Date { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Formulario de la guía manual (spec §6.1). Transporte: ids de maestros o datos digitados (particular).</summary>
public sealed class GuiaForm
{
    public Guid? Id { get; set; }
    public string? IssueDate { get; set; }
    public int TransferIndicator { get; set; }
    public int DispatchType { get; set; }
    public long OriginId { get; set; }
    public string? ReceiverTaxId { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverActivity { get; set; }
    public string? ReceiverCardCode { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? ReceiverCommune { get; set; }
    public string? ReceiverCity { get; set; }
    public string? DestinationAddress { get; set; }
    public string? DestinationCommune { get; set; }
    public string? DestinationCity { get; set; }
    public string? Notes { get; set; }
    public string TransportMode { get; set; } = ManualTransportModes.Master;
    public long? VehicleId { get; set; }
    public long? TrailerId { get; set; }
    public long? DriverId { get; set; }
    public long? CarrierId { get; set; }
    public string? Plate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverTaxId { get; set; }
    public string? DriverName { get; set; }
    public string? CarrierTaxId { get; set; }
    public string? CarrierName { get; set; }
    public List<LineForm> Lines { get; set; } = new();
    public List<ReferenceForm> References { get; set; } = new();
}

/// <summary>
/// Crear o editar una guía manual. CREATE para una nueva, EDIT para una existente (borrador, en manifiesto o
/// rechazada). Incluye la búsqueda del receptor en SAP (JSON) y la importación del detalle (JSON).
/// </summary>
public sealed class EditarModel : DespachosPageModelBase
{
    private readonly IManualGuideService _guides;
    private readonly IMasterDataService _data;

    public EditarModel(IManualGuideService guides, IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _guides = guides;
        _data = data;
    }

    protected override string MenuCode => "guias-manuales";

    [BindProperty] public GuiaForm Form { get; set; } = new();

    public TransportGuide? Guide { get; private set; }
    public bool InManifest => Guide?.Status == TransportGuideStatuses.InManifest;
    public string Title => Guide is null ? "Nueva guía manual" : $"Editar guía {ManualGuideCode.Format(Guide.Number ?? 0)}";
    public IReadOnlyList<TransferMotive> Motives { get; private set; } = Array.Empty<TransferMotive>();
    public IReadOnlyList<Origin> Origins { get; private set; } = Array.Empty<Origin>();
    public IReadOnlyList<Vehicle> Vehicles { get; private set; } = Array.Empty<Vehicle>();
    public IReadOnlyList<Driver> Drivers { get; private set; } = Array.Empty<Driver>();
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();
    public IReadOnlyList<BranchWarehouse> Branches { get; private set; } = Array.Empty<BranchWarehouse>();
    public string? IssuerTaxId { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (!await HasActionAsync(id is null ? PortalActions.Create : PortalActions.Edit)) return Forbid();
        try
        {
            await LoadLookupsAsync();
            if (id is { } gid)
            {
                Guide = await _guides.GetAsync(gid);
                if (Guide is null) return NotFound();
                Form = FromInput(gid, await _guides.GetInputAsync(gid));
            }
            else
            {
                Form = new GuiaForm
                {
                    IssueDate = ChileClock.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TransferIndicator = Motives.FirstOrDefault()?.Code ?? 0,
                    OriginId = Origins.FirstOrDefault()?.Id ?? 0,
                    Lines = { new LineForm() },
                };
            }
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        if (!await HasActionAsync(Form.Id is null ? PortalActions.Create : PortalActions.Edit)) return Forbid();
        try
        {
            await LoadLookupsAsync();
            var input = ToInput();
            Guid id;
            if (Form.Id is { } existing)
            {
                await _guides.UpdateAsync(existing, input);
                id = existing;
            }
            else id = await _guides.CreateAsync(input);
            SuccessMessage = "Guía guardada.";
            return RedirectToPage("Detalle", new { id });
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            if (Form.Id is { } gid) Guide = await _guides.GetAsync(gid);
            return Page();
        }
    }

    /// <summary>Receptor por RUT en los socios de negocio de SAP (JSON para el formulario).</summary>
    public async Task<IActionResult> OnGetReceptorAsync(string? taxId)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var p = await _guides.FindReceiverAsync(taxId ?? "");
            return new JsonResult(p is null
                ? new { found = false, error = (string?)null, partner = (object?)null }
                : new
                {
                    found = true, error = (string?)null,
                    partner = (object?)new
                    {
                        cardCode = p.CardCode, name = p.Name, activity = p.Activity, active = p.IsActive,
                        addresses = p.Addresses.Select(a => new { name = a.Name, address = a.Address, commune = a.Commune, city = a.City }),
                    },
                });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { found = false, error = (string?)GetErrorMessage(ex), partner = (object?)null });
        }
    }

    /// <summary>Detalle importado (texto pegado o planilla .xlsx): líneas y errores por fila, sin guardar nada.</summary>
    public async Task<IActionResult> OnPostImportarAsync(string? text, IFormFile? file)
    {
        if (!await HasActionAsync(PortalActions.Create) && !await HasActionAsync(PortalActions.Edit)) return Forbid();
        ImportedLinesResult r;
        if (file is { Length: > 0 })
        {
            await using var s = file.OpenReadStream();
            r = ManualGuideLineImporter.FromXlsx(s);
        }
        else r = ManualGuideLineImporter.FromText(text ?? "");
        return new JsonResult(new
        {
            lines = r.Lines.Select(l => new
            {
                itemCode = l.ItemCode, description = l.Description, unit = l.Unit,
                quantity = l.Quantity.ToString(CultureInfo.InvariantCulture), unitPrice = l.UnitPrice.ToString(CultureInfo.InvariantCulture),
            }).ToList(),
            errors = r.Errors,
        });
    }

    public async Task<IActionResult> OnGetPlantillaAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        return File(ManualGuideLineImporter.TemplateXlsx(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "detalle-guia-manual.xlsx");
    }

    private async Task LoadLookupsAsync()
    {
        Motives = await _guides.ListMotivesAsync();
        Origins = (await _data.ListOriginsAsync()).Where(o => !string.IsNullOrWhiteSpace(o.Address) && o.CommuneId != null).ToList();
        Vehicles = await _data.ListVehiclesAsync();
        Drivers = await _data.ListDriversAsync();
        Carriers = await _data.ListCarriersAsync();
        Communes = await _data.ListCommunesAsync();
        Branches = await _data.ListBranchWarehousesAsync();
        IssuerTaxId = await _data.GetIssuerTaxIdAsync();
    }

    /// <summary>Formulario → entrada del servicio; con transporte de maestros resuelve patente, chofer y transportista.</summary>
    private ManualGuideInput ToInput()
    {
        var f = Form;
        var errors = new List<string>();
        if (!DateOnly.TryParseExact(f.IssueDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            errors.Add("Indique la fecha de emisión.");
        var lines = new List<ManualGuideLineInput>();
        var rows = f.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Description) || !string.IsNullOrWhiteSpace(l.Quantity)
                                      || !string.IsNullOrWhiteSpace(l.ItemCode)).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var l = rows[i];
            if (!TryNumber(l.Quantity, out var qty)) errors.Add($"Línea {i + 1}: la cantidad no es un número.");
            var price = 0m;
            if (!string.IsNullOrWhiteSpace(l.UnitPrice) && !TryNumber(l.UnitPrice, out price)) errors.Add($"Línea {i + 1}: el precio no es un número.");
            lines.Add(new ManualGuideLineInput(Clean(l.ItemCode), l.Description?.Trim() ?? "", qty, Clean(l.Unit), price));
        }
        var refs = new List<ManualGuideReferenceInput>();
        foreach (var r in f.References.Where(r => !string.IsNullOrWhiteSpace(r.DocumentType) || r.Folio is > 0))
        {
            DateOnly.TryParseExact(r.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rd);
            refs.Add(new ManualGuideReferenceInput(r.DocumentType?.Trim() ?? "", r.Folio ?? 0, rd == default ? date : rd, Clean(r.Reason)));
        }

        ManualGuideTransportInput transport;
        if (f.TransportMode == ManualTransportModes.Master)
        {
            var v = Vehicles.FirstOrDefault(x => x.Id == f.VehicleId);
            var tr = Vehicles.FirstOrDefault(x => x.Id == f.TrailerId);
            var d = Drivers.FirstOrDefault(x => x.Id == f.DriverId);
            var c = Carriers.FirstOrDefault(x => x.Id == f.CarrierId);
            transport = new ManualGuideTransportInput(v?.Plate, tr?.Plate, d?.TaxId, d?.FullName, c?.TaxId, c?.Name);
        }
        else transport = new ManualGuideTransportInput(Clean(f.Plate), Clean(f.TrailerPlate), Clean(f.DriverTaxId), Clean(f.DriverName),
            Clean(f.CarrierTaxId), Clean(f.CarrierName));
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));

        return new ManualGuideInput(date, f.TransferIndicator, f.DispatchType, f.OriginId,
            f.ReceiverTaxId?.Trim() ?? "", f.ReceiverName?.Trim() ?? "", Clean(f.ReceiverActivity), Clean(f.ReceiverCardCode),
            f.ReceiverAddress?.Trim() ?? "", f.ReceiverCommune?.Trim() ?? "", Clean(f.ReceiverCity),
            f.DestinationAddress?.Trim() ?? "", f.DestinationCommune?.Trim() ?? "", Clean(f.DestinationCity),
            lines, refs, Clean(f.Notes), f.TransportMode, transport);
    }

    /// <summary>Entrada guardada → formulario; con transporte de maestros vuelve a elegir vehículo, chofer y transportista.</summary>
    private GuiaForm FromInput(Guid id, ManualGuideInput i)
    {
        var t = i.Transport;
        var form = new GuiaForm
        {
            Id = id, IssueDate = i.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), TransferIndicator = i.TransferIndicator,
            DispatchType = i.DispatchType, OriginId = i.OriginId, ReceiverTaxId = i.ReceiverTaxId, ReceiverName = i.ReceiverName,
            ReceiverActivity = i.ReceiverActivity, ReceiverCardCode = i.ReceiverCardCode, ReceiverAddress = i.ReceiverAddress,
            ReceiverCommune = i.ReceiverCommune, ReceiverCity = i.ReceiverCity, DestinationAddress = i.DestinationAddress,
            DestinationCommune = i.DestinationCommune, DestinationCity = i.DestinationCity, Notes = i.Notes,
            TransportMode = i.TransportMode == ManualTransportModes.Manifest ? ManualTransportModes.Manifest : ManualTransportModes.Manual,
            Plate = t?.Plate, TrailerPlate = t?.TrailerPlate, DriverTaxId = t?.DriverTaxId, DriverName = t?.DriverName,
            CarrierTaxId = t?.CarrierTaxId, CarrierName = t?.CarrierName,
            Lines = i.Lines.Select(l => new LineForm
            {
                ItemCode = l.ItemCode, Description = l.Description, Unit = l.Unit,
                Quantity = l.Quantity.ToString(CultureInfo.InvariantCulture), UnitPrice = l.UnitPrice.ToString(CultureInfo.InvariantCulture),
            }).ToList(),
            References = i.References.Select(r => new ReferenceForm
            {
                DocumentType = r.DocumentType, Folio = r.Folio, Date = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Reason = r.Reason,
            }).ToList(),
        };
        // Si los datos coinciden con los maestros, se muestra el modo "maestros" ya elegido.
        if (form.TransportMode == ManualTransportModes.Manual && t?.Plate is { } plate)
        {
            var v = Vehicles.FirstOrDefault(x => x.Plate == plate && !x.IsTrailer);
            var d = Drivers.FirstOrDefault(x => x.TaxId == t.DriverTaxId);
            var c = Carriers.FirstOrDefault(x => x.TaxId == t.CarrierTaxId);
            if (v is not null && d is not null && c is not null)
            {
                form.TransportMode = ManualTransportModes.Master;
                form.VehicleId = v.Id;
                form.DriverId = d.Id;
                form.CarrierId = c.Id;
                form.TrailerId = Vehicles.FirstOrDefault(x => x.IsTrailer && x.Plate == t.TrailerPlate)?.Id;
            }
        }
        return form;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool TryNumber(string? raw, out decimal value)
    {
        value = 0;
        var s = raw?.Trim() ?? "";
        return s.Length > 0 && (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
                                || decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CL"), out value));
    }
}
