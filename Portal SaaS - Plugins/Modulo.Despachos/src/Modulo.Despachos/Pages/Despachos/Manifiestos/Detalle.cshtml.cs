using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Manifiestos;

/// <summary>
/// Detalle del manifiesto con pestañas Cabecera / Documentos / Guías / Retorno / Costo / Historial. En borrador la
/// pestaña Documentos hospeda el escaneo continuo (wwwroot/js/despachos-scan.js). Permisos (menú
/// "manifiestos"): VIEW ver (incluye borrador y PDF de las guías), EDIT editar borrador y cambiar chofer/vehículo,
/// DELETE anular, APPROVE despachar, reintentar guías y cerrar, EXPORT PDF del manifiesto; el ajuste de costo exige
/// EDIT en "cierre-mensual". La pestaña Guías (Res. 154) solo aparece si la compañía emite guías al despachar.
/// </summary>
public sealed class DetalleModel : DespachosPageModelBase
{
    private readonly IManifestService _manifests;
    private readonly IManifestPdfService _pdf;
    private readonly IMasterDataService _data;
    private readonly IUserContactLookupService _users;
    private readonly IManifestCostService _cost;
    private readonly ITransportGuideService _guides;
    private readonly IDispatchGroupService? _dispatchGroups;
    private readonly IManualGuideService? _manualGuides;

    public DetalleModel(
        IManifestService manifests, IManifestPdfService pdf, IMasterDataService data,
        ICurrentUserContext currentUser, IUserContactLookupService users, IManifestCostService cost,
        ITransportGuideService guides, IDispatchGroupService? dispatchGroups = null, IManualGuideService? manualGuides = null)
        : base(currentUser)
    {
        _dispatchGroups = dispatchGroups;
        _manualGuides = manualGuides;
        _cost = cost;
        _guides = guides;
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

    /// <summary>Código AT de cada agrupación presente en el manifiesto (por id), para la tabla y "Quitar agrupación".</summary>
    public IReadOnlyDictionary<long, string> GroupCodes { get; private set; } = new Dictionary<long, string>();

    /// <summary>Código GM- de cada guía manual del manifiesto (por id de guía), para la tabla de documentos.</summary>
    public IReadOnlyDictionary<Guid, string> ManualGuideCodes { get; private set; } = new Dictionary<Guid, string>();

    /// <summary>Guías manuales en borrador que se pueden agregar al manifiesto (selector).</summary>
    public IReadOnlyList<GuideListItem> DraftManualGuides { get; private set; } = Array.Empty<GuideListItem>();

    public bool HasManualGuides => M.Documents.Any(d => d.DocumentKind == DocumentKinds.ManualGuide);

    /// <summary>Bodegas sucursal activas (formulario "Agregar pendientes" del borrador).</summary>
    public IReadOnlyList<BranchWarehouse> BranchWarehouses { get; private set; } = Array.Empty<BranchWarehouse>();

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

    /// <summary>La compañía emite guías de despacho (Res. 154) al despachar.</summary>
    public bool GuidesEnabled { get; private set; }
    /// <summary>Vista previa de las guías (solo en borrador).</summary>
    public GuidePreview? GuidePreview { get; private set; }
    /// <summary>Guías planificadas/emitidas (después de despachar).</summary>
    public IReadOnlyList<TransportGuide> Guides { get; private set; } = Array.Empty<TransportGuide>();
    public string? GuidesError { get; private set; }

    /// <summary>Pestaña inicial (la fija el servidor tras cada acción).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public string ActiveTab => Tab is "cabecera" or "documentos" or "retorno" or "costo" or "historial"
                                   || (Tab == "guias" && ShowGuides)
        ? Tab : M.Status == ManifestStatuses.Draft ? "documentos"
        : M.Status == ManifestStatuses.PendingGuides && ShowGuides ? "guias" : "cabecera";

    public bool IsPendingGuides => M.Status == ManifestStatuses.PendingGuides;

    /// <summary>Pestaña Guías visible: la compañía emite guías o el manifiesto ya tiene guías (aunque luego se desactivaran).</summary>
    public bool ShowGuides => GuidesEnabled || Guides.Count > 0 || HasManualGuides;

    /// <summary>Anular disponible (botón y drawer usan la misma condición).</summary>
    public bool CanAnnulNow => CanDelete && (M.Status == ManifestStatuses.Draft || M.Status == ManifestStatuses.PendingGuides
                                             || M.Status == ManifestStatuses.Dispatched);

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
        GuidesEnabled = await _guides.IsEnabledAsync();
        try
        {
            if (m.Status == ManifestStatuses.Draft)
            {
                if (GuidesEnabled || m.Documents.Any(d => d.DocumentKind == DocumentKinds.ManualGuide))
                    GuidePreview = await _guides.PreviewAsync(m.Id);
            }
            else Guides = await _guides.ListAsync(m.Id);
        }
        catch (Exception ex) { GuidesError = GetErrorMessage(ex); }
        Communes = await _data.ListCommunesAsync();
        if (m.Status == ManifestStatuses.Draft) BranchWarehouses = await _data.ListBranchWarehousesAsync();
        if (_dispatchGroups is not null)
        {
            var codes = new Dictionary<long, string>();
            foreach (var gid in m.Documents.Where(d => d.DispatchGroupId != null).Select(d => d.DispatchGroupId!.Value).Distinct())
                if (await _dispatchGroups.GetAsync(gid) is { } g) codes[gid] = Dominio.DispatchGroupCode.Format(g.Number);
            GroupCodes = codes;
        }
        if (_manualGuides is not null)
        {
            var codes = new Dictionary<Guid, string>();
            foreach (var guideId in m.Documents.Where(d => d.TransportGuideId != null).Select(d => d.TransportGuideId!.Value).Distinct())
                if (await _manualGuides.GetAsync(guideId) is { } g) codes[guideId] = Dominio.ManualGuideCode.Format(g.Number ?? 0);
            ManualGuideCodes = codes;
            if (m.Status == ManifestStatuses.Draft)
                DraftManualGuides = await _manualGuides.ListAsync(
                    new GuideFilter(TransportGuideSources.Manual, TransportGuideStatuses.Draft, null, null, null));
        }
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
        ManifestEventTypes.GuidesPlanned => "Guías de despacho planificadas",
        ManifestEventTypes.GuideIssued => "Guía de despacho emitida",
        ManifestEventTypes.GuideFailed => "Guías de despacho sin emitir",
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
                // Hoja de agrupación: entraron varias filas de una vez; la página se recarga para mostrarlas.
                reload = result.Outcome == AddDocumentOutcomes.GroupAdded,
                document = doc is null ? null : new
                {
                    id = doc.Id,
                    kind = DocumentKinds.Label(doc.DocumentKind),
                    folio = doc.Folio,
                    date = doc.DocumentDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                    customer = doc.CustomerName,
                    commune = doc.DestinationCommuneName,
                    communeResolved = doc.DestinationCommuneId != null,
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
                reload = false,
                document = (object?)null,
                totalDocuments = 0,
                totalUnits = 0m,
                totalPackages = 0,
                totalAmount = 0m,
            });
        }
    }

    /// <summary>Mensaje del escáner: el motivo específico del servicio si lo hay (ej. bodega no sucursal), si no uno genérico.</summary>
    private static string OutcomeMessage(AddDocumentResult r, ManifestDocument? doc) =>
        r.Outcome != AddDocumentOutcomes.Added && r.Warnings.FirstOrDefault() is { Length: > 0 } reason
            ? reason
            : GenericOutcomeMessage(r, doc);

    private static string GenericOutcomeMessage(AddDocumentResult r, ManifestDocument? doc) => r.Outcome switch
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
        AddDocumentOutcomes.NotBranch => "La transferencia va a una bodega que no es sucursal.",
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

    /// <summary>Agrega las transferencias pendientes de una bodega sucursal desde la fecha (yyyy-MM-dd). Exige EDIT.</summary>
    public async Task<IActionResult> OnPostAgregarPendientesAsync(Guid id, string? warehouseCode, string? fromDate)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            if (!DateOnly.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                throw new InvalidOperationException("Indique la fecha desde la que se buscan las transferencias.");
            var r = await _manifests.AddPendingTransfersAsync(id, warehouseCode ?? "", from);
            SuccessMessage = $"Se agregaron {r.Added} transferencia(s).";
            if (r.Skipped.Count > 0)
                ErrorMessage = $"No se agregaron {r.Skipped.Count}: " + string.Join(" · ", r.Skipped);
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "documentos" });
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

    /// <summary>Vacía el borrador para volver a cargarlo. Exige EDIT.</summary>
    public async Task<IActionResult> OnPostQuitarTodosAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var n = await _manifests.RemoveAllDocumentsAsync(id);
            SuccessMessage = n == 1 ? "Se quitó 1 documento." : $"Se quitaron {n} documentos.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "documentos" });
    }

    /// <summary>Quita del borrador todas las transferencias de una agrupación (vuelve a "armada"). Exige EDIT.</summary>
    /// <summary>Agrega una guía manual en borrador elegida en el selector (también entra escaneando su código GM-).</summary>
    public async Task<IActionResult> OnPostAgregarGuiaManualAsync(Guid id, Guid guideId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var r = await _manifests.AddManualGuideByIdAsync(id, guideId);
            if (r.Outcome == AddDocumentOutcomes.Added)
            {
                SuccessMessage = "Guía manual agregada al manifiesto.";
                if (r.Warnings.Count > 0) WarningMessage = string.Join(" ", r.Warnings);
            }
            else ErrorMessage = string.Join(" ", r.Warnings);
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "documentos" });
    }

    public async Task<IActionResult> OnPostQuitarAgrupacionAsync(Guid id, long groupId)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _manifests.RemoveDispatchGroupAsync(id, groupId);
            SuccessMessage = "Agrupación quitada del manifiesto.";
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
                // Si quedó en "guías pendientes" la acción sigue en la pestaña Guías (reintentar).
                var m = await _manifests.GetAsync(id);
                return RedirectToPage(new { id, tab = m?.Status == ManifestStatuses.PendingGuides ? "guias" : "documentos" });
            }
            SuccessMessage = "Manifiesto despachado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id });
    }

    /// <summary>Reintenta escribir en SAP el folio de las guías de traslado en las transferencias pendientes. Exige EDIT.</summary>
    public async Task<IActionResult> OnPostReintentarSapAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var pending = await _guides.RetrySapWritebackAsync(id);
            if (pending == 0) SuccessMessage = "Folios escritos en SAP.";
            else ErrorMessage = $"SAP sigue rechazando la escritura en {pending} transferencia(s); revise el detalle en la pestaña Guías.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "guias" });
    }

    public async Task<IActionResult> OnPostReintentarGuiasAsync(Guid id)
    {
        if (!await HasActionAsync(PortalActions.Approve)) return Forbid();
        try
        {
            var result = await _manifests.RetryGuidesAsync(id);
            if (result.Warnings.Count > 0) WarningMessage = string.Join(" ", result.Warnings);
            if (!result.Success) ErrorMessage = string.Join(" ", result.Errors);
            else SuccessMessage = "Guías emitidas: manifiesto despachado.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { id, tab = "guias" });
    }

    /// <summary>PDF borrador (sin folio) de una guía de la vista previa.</summary>
    public async Task<IActionResult> OnGetGuiaBorradorAsync(Guid id, int sequence)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var bytes = await _guides.PreviewPdfAsync(id, sequence);
            return new InlinePdfResult(bytes, $"guia-borrador-{sequence}.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id, tab = "guias" });
        }
    }

    /// <summary>PDF de una guía emitida o de todas unidas (sequence vacío).</summary>
    public async Task<IActionResult> OnGetGuiasPdfAsync(Guid id, int? sequence)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var bytes = await _guides.GetPdfAsync(id, sequence);
            return new InlinePdfResult(bytes, sequence is { } s ? $"guia-{s}.pdf" : "guias.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id, tab = "guias" });
        }
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

    public async Task<IActionResult> OnPostCorregirComunaAsync(Guid id, Guid documentId, string? communeName)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _manifests.SetDocumentCommuneByNameAsync(id, documentId, communeName ?? "");
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
