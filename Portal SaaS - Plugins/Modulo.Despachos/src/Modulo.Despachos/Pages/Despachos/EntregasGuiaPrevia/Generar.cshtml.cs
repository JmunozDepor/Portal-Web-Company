using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.EntregasGuiaPrevia;

/// <summary>
/// Agrupar Entregas con guía previa: Entregas abiertas y libres del cliente, por OC (nada marcado); las condiciones de la
/// guía (indicador de traslado y tipo de despacho, por defecto 2 y 2) aplican a todo lo que se agrupa. "Agrupar" crea una
/// agrupación EG- por OC (CREATE).
/// </summary>
public sealed class GenerarModel : DespachosPageModelBase
{
    private readonly IDeliveryGroupService _deliveries;

    public GenerarModel(IDeliveryGroupService deliveries, ICurrentUserContext currentUser) : base(currentUser)
    {
        _deliveries = deliveries;
    }

    protected override string MenuCode => "entregas-guia-previa";

    /// <summary>Candidatos del cliente buscado; null mientras no se indique cliente.</summary>
    public DeliveryCandidates? Candidates { get; private set; }

    /// <summary>Cliente: RUT o código SAP.</summary>
    [BindProperty(SupportsGet = true)] public string? Customer { get; set; }
    /// <summary>Entregas desde (yyyy-MM-dd), por defecto hace 7 días.</summary>
    [BindProperty(SupportsGet = true)] public string? FromDate { get; set; }
    /// <summary>Fecha de despacho (yyyy-MM-dd), por defecto hoy.</summary>
    [BindProperty(SupportsGet = true)] public string? DispatchDate { get; set; }
    [BindProperty(SupportsGet = true)] public int TransferIndicator { get; set; } = 2;
    [BindProperty(SupportsGet = true)] public int DispatchType { get; set; } = 2;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        if (string.IsNullOrWhiteSpace(FromDate)) FromDate = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(DispatchDate)) DispatchDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(Customer)) return Page();
        try
        {
            var from = ParseDate(FromDate, "Indique la fecha desde la que se buscan las Entregas.");
            Candidates = await _deliveries.ListCandidatesAsync(Customer, from);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostGenerarAsync(string? customer, string? fromDate, string? dispatchDate,
        int transferIndicator, int dispatchType, int[]? docEntries)
    {
        if (!await HasActionAsync(PortalActions.Create)) return Forbid();
        try
        {
            var dispatch = ParseDate(dispatchDate, "Indique la fecha de despacho.");
            var from = ParseDate(fromDate, "Indique la fecha desde la que se buscan las Entregas.");
            if (docEntries is not { Length: > 0 })
                throw new InvalidOperationException("Seleccione al menos una Entrega para agrupar.");
            var created = await _deliveries.GenerateAsync(
                new DeliveryGroupInput(customer ?? "", from, dispatch, transferIndicator, dispatchType, docEntries));
            SuccessMessage = created.Count == 1 ? "Se generó 1 agrupación." : $"Se generaron {created.Count} agrupaciones (una por OC).";
            return RedirectToPage("Index", new { DispatchDate = dispatch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new
            {
                Customer = customer, FromDate = fromDate, DispatchDate = dispatchDate,
                TransferIndicator = transferIndicator, DispatchType = dispatchType,
            });
        }
    }

    private static DateOnly ParseDate(string? text, string error) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new InvalidOperationException(error);
}
