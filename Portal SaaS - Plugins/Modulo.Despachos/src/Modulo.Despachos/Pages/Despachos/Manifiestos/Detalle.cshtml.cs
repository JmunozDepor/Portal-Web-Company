using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>
/// Detalle del manifiesto con pestañas Cabecera / Documentos / Retorno / Costo / Historial. En borrador la
/// pestaña Documentos hospeda el escaneo continuo (wwwroot/js/despachos-scan.js). Permisos (menú
/// "manifiestos"): VIEW ver, EDIT editar borrador y cambiar chofer/vehículo, DELETE anular, APPROVE despachar
/// y cerrar, EXPORT PDF; el ajuste de costo exige EDIT en "cierre-mensual".
/// </summary>
public sealed class DetalleModel : DespachosPageModelBase
{
    private readonly IManifestService _manifests;
    private readonly IManifestPdfService _pdf;
    private readonly IMasterDataService _data;
    private readonly IUserContactLookupService _users;
    private readonly IManifestCostService _cost;

    public DetalleModel(
        IManifestService manifests, IManifestPdfService pdf, IMasterDataService data,
        ICurrentUserContext currentUser, IUserContactLookupService users, IManifestCostService cost) : base(currentUser)
    {
        _cost = cost;
        _manifests = manifests;
        _pdf = pdf;
        _data = data;
        _users = users;
    }

    protected override string MenuCode => "manifiestos";

    public Manifest M { get; private set; } = null!;
    public ManifestLookups Lookups { get; private set; } = null!;
    public IReadOnlyList<RejectionReason> Reasons { get; private set; } = Array.Empty<RejectionReason>();
    public IReadOnlyDictionary<Guid, string> UserLabels { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyList<Commune> Communes { get; private set; } = Array.Empty<Commune>();

    public bool CanEdit { get; private set; }
    public bool CanApprove { get; private set; }
    public bool CanDelete { get; private set; }
    public bool CanExport { get; private set; }
    public bool CanAdjustCost { get; private set; }
    public bool CanReturn { get; private set; }
    public IReadOnlyList<ManifestExtraChargeRow> ExtraCharges { get; private set; } = Array.Empty<ManifestExtraChargeRow>();

    /// <summary>Costo estimado del borrador (no se guarda; el definitivo se calcula al despachar).</summary>
    public CostResult? Estimate { get; private set; }
    public string? EstimateError { get; private set; }

    /// <summary>Pestaña inicial (la fija el servidor tras cada acción).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public string ActiveTab => Tab is "cabecera" or "documentos" or "retorno" or "costo" or "historial"
        ? Tab : (M.Status == ManifestStatuses.Draft ? "documentos" : "cabecera");

    [BindProperty] public ManifestForm Header { get; set; } = new();
    [BindProperty] public ChangeVehicleInput Change { get; set; } = new();

    public bool IsDraft => M.Status == ManifestStatuses.Draft;
    public int DocCount => M.Documents.Count;
    public decimal DocUnits => M.Documents.Sum(d => d.Units);
    public int DocPackages => M.Documents.Sum(d => d.Packages);
    public decimal DocAmount => M.Documents.Sum(d => d.TotalAmount);

    /// <summary>Datos del escáner: texto del lector / cámara, o tipo + folio (+ tipo SII si el folio es ambiguo).</summary>
    public sealed class ScanInput
    {
        public string? RawScan { get; set; }
        public string? DocumentKind { get; set; }
        public long? Folio { get; set; }
        public int? SiiDocumentType { get; set; }
    }

    public sealed class ChangeVehicleInput
    {
        public long VehicleId { get; set; }
        public long? TrailerId { get; set; }
        public long DriverId { get; set; }
        public string? Reason { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        return await LoadAsync(id) ? Page() : NotFound();
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        var m = await _manifests.GetAsync(id);
        if (m is null) return false;
        M = m;
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanApprove = await HasActionAsync(PortalActions.Approve);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        CanExport = await HasActionAsync(PortalActions.Export);
        CanAdjustCost = await HasActionOnAsync("cierre-mensual", PortalActions.Edit);
        CanReturn = await HasActionOnAsync("retorno", PortalActions.Edit);
        Lookups = await ManifestLookups.LoadAsync(_data);
        Reasons = await _data.ListRejectionReasonsAsync(includeInactive: true);
        ExtraCharges = await _manifests.ListExtraChargesAsync(m.Id);
        if (m.Status == ManifestStatuses.Draft)
        {
            try { Estimate = await _cost.EstimateAsync(m); }
            catch (Exception ex) { EstimateError = GetErrorMessage(ex); }
        }
        Communes = await _data.ListCommunesAsync();
        UserLabels = await ResolveUsersAsync(m);
        if (Header.DispatchDate is null) Header = ManifestForm.From(m);
        if (Change.VehicleId == 0)
            Change = new ChangeVehicleInput { VehicleId = m.VehicleId, TrailerId = m.TrailerId, DriverId = m.DriverId };
        return true;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveUsersAsync(Manifest m)
    {
        var labels = new Dictionary<Guid, string>();
        foreach (var userId in m.Events.Where(e => e.UserId != null).Select(e => e.UserId!.Value).Distinct())
        {
            try
            {
                var contact = await _users.GetContactAsync(userId);
                labels[userId] = contact?.Email ?? userId.ToString("N")[..8];
            }
            catch
            {
                labels[userId] = userId.ToString("N")[..8];
            }
        }
        return labels;
    }

    public string UserLabel(Guid? userId) =>
        userId is { } id && UserLabels.TryGetValue(id, out var label) ? label : "—";

    public string ReasonName(long? id) => Reasons.FirstOrDefault(r => r.Id == id)?.Name ?? "—";

    public static string ChileTime(DateTime utc) =>
        Dominio.ChileClock.ToLocal(utc).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string EventLabel(string type) => type switch
    {
        ManifestEventTypes.Created => "Borrador creado",
        ManifestEventTypes.DocumentAdded => "Documento agregado",
        ManifestEventTypes.DocumentRemoved => "Documento quitado",
        ManifestEventTypes.Dispatched => "Despachado",
        ManifestEventTypes.VehicleChanged => "Vehículo / chofer cambiado",
        ManifestEventTypes.ReturnRegistered => "Retorno registrado",
        ManifestEventTypes.Closed => "Cerrado",
        ManifestEventTypes.Annulled => "Anulado",
        ManifestEventTypes.CostOverridden => "Costo ajustado",
        ManifestEventTypes.Printed => "PDF impreso",
        ManifestEventTypes.Reprinted => "PDF reimpreso (copia)",
        ManifestEventTypes.PeriodClosed => "Período cerrado",
        ManifestEventTypes.PeriodReopened => "Período reabierto",
        ManifestEventTypes.CostRecalculated => "Costo recalculado",
        ManifestEventTypes.DocumentCommuneChanged => "Comuna de destino corregida",
        ManifestEventTypes.ExtraChargesChanged => "Adicionales modificados",
        _ => type,
    };

    // ------------------------------------------------------------------ escaneo (AJAX)

    /// <summary>Agrega un documento al borrador. Responde JSON para el escaneo continuo.</summary>
    public async Task<IActionResult> OnPostAgregarDocumentoAsync(Guid id, ScanInput input)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var result = await _manifests.AddDocumentAsync(id,
                new DocumentCapture(input.RawScan, input.DocumentKind, input.Folio, input.SiiDocumentType));
            var m = await _manifests.GetAsync(id);
            var doc = result.ManifestDocumentId is { } docId ? m?.Documents.FirstOrDefault(d => d.Id == docId) : null;
            return new JsonResult(new
            {
                outcome = result.Outcome,
                message = OutcomeMessage(result, doc),
                warnings = result.Warnings,
                otherManifestNumber = result.OtherManifestNumber,
                document = doc is null ? null : new
                {
                    id = doc.Id,
                    kind = DocumentKinds.Label(doc.DocumentKind),
                    folio = doc.Folio,
                    customer = doc.CustomerName,
                    commune = doc.DestinationCommuneName,
                    units = doc.Units,
                    packages = doc.Packages,
                    amount = doc.TotalAmount,
                },
                totalDocuments = m?.Documents.Count ?? 0,
                totalUnits = m?.Documents.Sum(d => d.Units) ?? 0m,
                totalPackages = m?.Documents.Sum(d => d.Packages) ?? 0,
                totalAmount = m?.Documents.Sum(d => d.TotalAmount) ?? 0m,
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new
            {
                outcome = "error",
                message = GetErrorMessage(ex),
                warnings = Array.Empty<string>(),
                otherManifestNumber = (int?)null,
                document = (object?)null,
                totalDocuments = 0,
                totalUnits = 0m,
                totalPackages = 0,
                totalAmount = 0m,
            });
        }
    }

    private static string OutcomeMessage(AddDocumentResult r, ManifestDocument? doc) => r.Outcome switch
    {
        AddDocumentOutcomes.Added => doc is null
            ? "Documento agregado."
            : $"{DocumentKinds.Label(doc.DocumentKind)} {doc.Folio} — {doc.CustomerName}",
        AddDocumentOutcomes.DuplicateInThis => "Ese documento ya está en este manifiesto.",
        AddDocumentOutcomes.InOtherManifest => $"Ese documento ya está en el manifiesto N° {r.OtherManifestNumber}.",
        AddDocumentOutcomes.NotFound => "No se encontró el documento en SAP.",
        AddDocumentOutcomes.Canceled => "El documento está anulado en SAP.",
        AddDocumentOutcomes.InvalidScan => "Lectura no válida: no es el timbre (PDF417) de un documento.",
        AddDocumentOutcomes.OtherIssuer => "El documento pertenece a otro emisor.",
        AddDocumentOutcomes.Ambiguous => "El folio existe como factura (33) y factura exenta (34): indique el tipo.",
        _ => r.Outcome,
    };

    // ------------------------------------------------------------------ acciones

    public async Task<IActionResult> OnPostGuardarCabeceraAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _manifests.UpdateDraftHeaderAsync(id, Header.ToInput());
            SuccessMessage = "Manifiesto guardado como borrador. Los documentos ya agregados quedan guardados al escanearlos.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "cabecera" });
    }

    public async Task<IActionResult> OnPostQuitarDocumentoAsync(Guid id, Guid documentId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _manifests.RemoveDocumentAsync(id, documentId);
            SuccessMessage = "Documento quitado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "documentos" });
    }

    public async Task<IActionResult> OnPostDespacharAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Approve)) return Forbid();
        try
        {
            var result = await _manifests.DispatchAsync(id);
            if (result.Warnings.Count > 0) WarningMessage = string.Join(" ", result.Warnings);
            if (!result.Success)
            {
                ErrorMessage = string.Join(" ", result.Errors);
                return RedirectToPage(new { id, tab = "documentos" });
            }
            SuccessMessage = "Manifiesto despachado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAnularAsync(Guid id, string? reason)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            await _manifests.AnnulAsync(id, reason ?? "");
            SuccessMessage = "Manifiesto anulado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCambiarVehiculoAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var warnings = await _manifests.ChangeVehicleOrDriverAsync(
                id, Change.VehicleId, Change.TrailerId, Change.DriverId, Change.Reason ?? "");
            SuccessMessage = "Vehículo / chofer actualizado.";
            if (warnings.Count > 0) WarningMessage = string.Join(" ", warnings);
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "cabecera" });
    }

    public async Task<IActionResult> OnPostAjustarCostoAsync(Guid id, string? amount, string? reason)
    {
        if (!await HasActionOnAsync("cierre-mensual", PortalActions.Edit)) return Forbid();
        try
        {
            if (!decimal.TryParse(amount?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new InvalidOperationException("El monto del costo no es un número válido.");
            await _manifests.OverrideCostAsync(id, value, reason ?? "");
            SuccessMessage = "Costo ajustado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "costo" });
    }

    public async Task<IActionResult> OnPostRecalcularCostoAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var result = await _manifests.RecalculateCostAsync(id);
            if (result.Status == CostStatuses.Ok) SuccessMessage = "Costo recalculado.";
            else WarningMessage = result.Status == CostStatuses.Ambiguous
                ? "Costo recalculado: sigue habiendo una tarifa ambigua."
                : "Costo recalculado: sigue sin tarifa para este viaje.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "costo" });
    }

    /// <summary>Cantidades de adicionales (ayudantes, puntos adicionales...): cantidades[idRecargo] = n.</summary>
    public async Task<IActionResult> OnPostGuardarAdicionalesAsync(Guid id, Dictionary<long, string>? cantidades)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var quantities = new Dictionary<long, int>();
            foreach (var (surchargeId, raw) in cantidades ?? new())
            {
                var text = raw?.Trim();
                if (string.IsNullOrEmpty(text)) { quantities[surchargeId] = 0; continue; }
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    throw new InvalidOperationException("La cantidad de cada adicional debe ser un número entero (0 o más).");
                quantities[surchargeId] = n;
            }
            await _manifests.SetExtraChargesAsync(id, quantities);
            SuccessMessage = "Adicionales guardados.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "costo" });
    }

    public async Task<IActionResult> OnPostCorregirComunaAsync(Guid id, Guid documentId, long communeId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _manifests.SetDocumentCommuneAsync(id, documentId, communeId);
            SuccessMessage = "Comuna corregida.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "documentos" });
    }

    public async Task<IActionResult> OnPostCerrarAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Approve)) return Forbid();
        try
        {
            await _manifests.CloseAsync(id);
            SuccessMessage = "Manifiesto cerrado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// PDF inline. Si el manifiesto ya se imprimió antes (evento printed/reprinted) esta impresión
    /// sale como COPIA y queda registrada como reimpresión.
    /// </summary>
    public async Task<IActionResult> OnGetPdfAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Export)) return Forbid();
        try
        {
            var m = await _manifests.GetAsync(id);
            if (m is null) return NotFound();
            var isCopy = m.Events.Any(e => e.EventType is ManifestEventTypes.Printed or ManifestEventTypes.Reprinted);
            var bytes = await _pdf.RenderAsync(id, isCopy);
            return new InlinePdfResult(bytes, $"manifiesto-{m.Number}.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }
    }
}
