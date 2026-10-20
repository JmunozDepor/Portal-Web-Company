using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Formularios;

/// <summary>
/// Formulario base de cada compañía: qué campos de los documentos genéricos (Venta/
/// Compra/Inventario) se ocultan para todos los usuarios. Cada usuario puede después
/// ajustar su propia versión desde "Personalizar" en el documento (ver IFormLayoutService).
/// Campos ocultables = DocumentFormFieldCatalog (nunca los obligatorios). Selector de
/// compañía de solo lectura, mismo patrón que ConexionesExternas.
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IFormLayoutService _layouts;
    private readonly ITenantUserAdminService _tenant;
    private readonly ICompanyDocumentSettingsService _documentSettings;

    public IndexModel(IFormLayoutService layouts, ITenantUserAdminService tenant, ICompanyDocumentSettingsService documentSettings,
        ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _layouts = layouts;
        _tenant = tenant;
        _documentSettings = documentSettings;
    }

    /// <summary>Días de retención de borradores de la compañía elegida (null = no vencen).</summary>
    public int? DraftRetentionDays { get; private set; }

    /// <summary>UDF de cabecera para la marca de origen de los documentos (null = Comentarios).</summary>
    public string? DraftMarkerUdfName { get; private set; }

    /// <summary>
    /// Parámetros de documentos por compañía: vencimiento/limpieza de borradores (ver
    /// DocumentDraftCleanupWorker) y UDF de la marca de origen anti-duplicados (ver
    /// IDocumentSapMarkerService) -- comunes a documentos digitados e Importación Genérica.
    /// </summary>
    public async Task<IActionResult> OnPostRetentionAsync(Guid companyId, int? draftRetentionDays, string? draftMarkerUdfName, CancellationToken ct)
    {
        if (!(await _tenant.ListCompaniesAsync()).Any(c => c.Id == companyId))
        {
            return Forbid();
        }

        try
        {
            await _documentSettings.SaveDraftMarkerUdfNameAsync(companyId, draftMarkerUdfName, ct);
            await _documentSettings.SaveDraftRetentionDaysAsync(companyId, draftRetentionDays, ct);
            MensajeExito = "Parámetros de documentos guardados. "
                + (draftRetentionDays is > 0
                    ? $"Los borradores sin modificar por más de {draftRetentionDays} día(s) se eliminarán automáticamente. "
                    : "Los borradores no vencen. ")
                + (string.IsNullOrWhiteSpace(draftMarkerUdfName)
                    ? "La marca de origen se escribe en Comentarios."
                    : $"La marca de origen se escribe en {draftMarkerUdfName.Trim()} (debe existir en SAP).");
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { companyId });
    }

    public IReadOnlyList<CompanyOptionDto> Companies { get; private set; } = [];
    public Guid? CompanyId { get; private set; }

    /// <summary>Clave del formulario -> campos ocultos en la base de la compañía elegida.</summary>
    public Dictionary<string, IReadOnlyList<string>> BaseHidden { get; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? companyId, CancellationToken ct)
    {
        Companies = await _tenant.ListCompaniesAsync();
        if (Companies.Count == 0)
        {
            return Page();
        }

        CompanyId = companyId ?? Companies[0].Id;
        if (!Companies.Any(c => c.Id == CompanyId))
        {
            MensajeError = "Compañía inválida.";
            CompanyId = Companies[0].Id;
        }

        foreach (var form in DocumentFormFieldCatalog.Forms)
        {
            BaseHidden[form.Key] = await _layouts.GetCompanyBaseAsync(CompanyId.Value, form.Key, ct);
        }

        DraftRetentionDays = await _documentSettings.GetDraftRetentionDaysAsync(CompanyId.Value, ct);
        DraftMarkerUdfName = await _documentSettings.GetDraftMarkerUdfNameAsync(CompanyId.Value, ct);

        return Page();
    }

    /// <summary>visible = casillas marcadas; se guardan como ocultos los campos del formulario que NO vinieron.</summary>
    public async Task<IActionResult> OnPostAsync(Guid companyId, string formKey, string[]? visible, CancellationToken ct)
    {
        if (!(await _tenant.ListCompaniesAsync()).Any(c => c.Id == companyId))
        {
            return Forbid();
        }

        var form = DocumentFormFieldCatalog.Find(formKey);
        if (form is null)
        {
            MensajeError = "Formulario desconocido.";
            return RedirectToPage(new { companyId });
        }

        var visibleSet = (visible ?? []).ToHashSet(StringComparer.Ordinal);
        var hidden = form.Fields.Select(f => f.Key).Where(k => !visibleSet.Contains(k)).ToList();
        try
        {
            await _layouts.SaveCompanyBaseAsync(companyId, formKey, hidden, ct);
            MensajeExito = $"Formulario base de \"{form.Name}\" guardado.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { companyId });
    }
}
