using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Retorno;

/// <summary>
/// Registro del retorno: se escanea el Code128 del PDF (número de manifiesto), se cargan sus documentos
/// (todos "Entregado" por defecto) y se corrigen los que no se entregaron, con motivo obligatorio.
/// Menú "retorno" (VIEW ver, EDIT registrar).
/// </summary>
public sealed class IndexModel : DespachosPageModelBase
{
    private readonly IManifestService _manifests;
    private readonly IMasterDataService _data;

    public IndexModel(IManifestService manifests, IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _manifests = manifests;
        _data = data;
    }

    protected override string MenuCode => "retorno";

    public bool CanEdit { get; private set; }
    public int? Number { get; private set; }
    public Manifest? M { get; private set; }
    public IReadOnlyList<RejectionReason> Reasons { get; private set; } = Array.Empty<RejectionReason>();
    public IReadOnlyList<RowInput> Rows { get; private set; } = Array.Empty<RowInput>();
    public string? OdometerEndText { get; private set; }

    /// <summary>Resultado informado para un documento (llega de la grilla).</summary>
    public sealed class RowInput
    {
        public Guid DocumentId { get; set; }
        public string? Status { get; set; }
        public long? ReasonId { get; set; }
        public string? Comments { get; set; }
    }

    /// <summary>Estados que el operador puede elegir y su etiqueta.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> StatusChoices = new[]
    {
        (DeliveryStatuses.Delivered, "Entregado"),
        (DeliveryStatuses.PartiallyDelivered, "Entrega parcial"),
        (DeliveryStatuses.Rejected, "Rechazado"),
        (DeliveryStatuses.NotDelivered, "No entregado"),
    };

    /// <summary>Tipo de motivo que aplica a cada estado (null = no pide motivo).</summary>
    public static string? AppliesTo(string? status) => status switch
    {
        DeliveryStatuses.PartiallyDelivered or DeliveryStatuses.Rejected => RejectionAppliesTo.Rejection,
        DeliveryStatuses.NotDelivered => RejectionAppliesTo.NotDelivered,
        _ => null,
    };

    public async Task<IActionResult> OnGetAsync(int? number)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanEdit = await HasActionAsync(PortalActions.Edit);
        Number = number;
        if (number is { } n)
        {
            var m = await _manifests.GetByNumberAsync(n);
            if (m is null) ErrorMessage = $"No existe el manifiesto N° {n}.";
            else await PrepareAsync(m, null);
        }
        return Page();
    }

    private async Task PrepareAsync(Manifest m, IReadOnlyList<RowInput>? posted)
    {
        if (m.Status is not (ManifestStatuses.Dispatched or ManifestStatuses.Returned))
        {
            ErrorMessage = $"El manifiesto N° {m.Number} está en estado «{DespachosUi.StatusLabel(DespachosUi.StatusKey(m.Status))}»: " +
                           "solo se registra el retorno de un manifiesto despachado o retornado.";
            return;
        }
        M = m;
        Number = m.Number;
        Reasons = (await _data.ListRejectionReasonsAsync()).ToList();
        OdometerEndText = m.OdometerEnd?.ToString(CultureInfo.InvariantCulture);
        Rows = m.Documents.OrderBy(d => d.AddedAt).Select(d =>
        {
            var p = posted?.FirstOrDefault(r => r.DocumentId == d.Id);
            if (p is not null) return p;
            // Sin resultado todavía -> Entregado; si ya hay uno (corrección) se conserva.
            var status = d.DeliveryStatus is DeliveryStatuses.Pending or DeliveryStatuses.Dispatched
                ? DeliveryStatuses.Delivered : d.DeliveryStatus;
            return new RowInput { DocumentId = d.Id, Status = status, ReasonId = d.RejectionReasonId, Comments = d.Comments };
        }).ToList();
    }

    public async Task<IActionResult> OnPostRegistrarAsync(Guid id, string? odometerEnd, List<RowInput> rows)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        CanEdit = true;
        try
        {
            int? km = null;
            if (!string.IsNullOrWhiteSpace(odometerEnd))
            {
                if (!int.TryParse(odometerEnd.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    throw new InvalidOperationException("El odómetro final debe ser un número entero.");
                km = parsed;
            }
            var inputs = rows.Select(r => new DocumentReturnInput(
                r.DocumentId, r.Status ?? DeliveryStatuses.Delivered, r.ReasonId,
                string.IsNullOrWhiteSpace(r.Comments) ? null : r.Comments.Trim())).ToList();
            await _manifests.RegisterReturnAsync(id, inputs, km);
            SuccessMessage = "Retorno registrado.";
            return RedirectToPage("/Despachos/Manifiestos/Detalle", new { id, tab = "retorno" });
        }
        catch (Exception ex)
        {
            // Se vuelve a mostrar la grilla con lo que el operador había ingresado.
            ErrorMessage = GetErrorMessage(ex);
            var m = await _manifests.GetAsync(id);
            if (m is not null)
            {
                var message = ErrorMessage;
                await PrepareAsync(m, rows);
                ErrorMessage = message;
                OdometerEndText = odometerEnd;
            }
            return Page();
        }
    }
}
