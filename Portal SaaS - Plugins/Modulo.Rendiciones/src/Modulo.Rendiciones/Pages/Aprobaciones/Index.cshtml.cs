using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Aprobaciones;

/// <summary>
/// Bandeja del aprobador: informes Pending cuyo CurrentLevel le corresponde al usuario
/// logueado, más una pestaña de historial con los informes sobre los que ya tomó
/// alguna decisión (pedido explícito -- antes solo se veía lo pendiente, sin forma de
/// repasar lo ya resuelto). Reutiliza Pages/Informes/Detalle para Aprobar/Rechazar,
/// sin duplicar la pantalla.
/// </summary>
public sealed class IndexModel : RendicionesAprobadorPageModelBase
{
    private readonly IExpenseReportService _reports;
    private readonly ITenantUserAdminService _users;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseReportService reports, ITenantUserAdminService users, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _reports = reports;
        _users = users;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReport> Pending { get; private set; } = Array.Empty<ExpenseReport>();
    public IReadOnlyList<ResolvedReportForApprover> Resolved { get; private set; } = Array.Empty<ResolvedReportForApprover>();

    private IReadOnlyDictionary<Guid, TenantUserDto> UserById { get; set; } = new Dictionary<Guid, TenantUserDto>();

    /// <summary>"Juan Pérez (jmunoz)" -- nombre completo + usuario, para identificar sin
    /// ambigüedad quién envió el informe cuando hay varios usuarios (pedido explícito,
    /// 2026-10-26). Cae a Username si todavía no tiene nombre completo cargado.</summary>
    public string SubmitterDisplayName(ExpenseReport report)
    {
        if (!UserById.TryGetValue(report.UserId, out var u))
            return report.UserId.ToString();

        return string.IsNullOrWhiteSpace(u.FullName) ? u.Username : $"{u.FullName} ({u.Username})";
    }

    /// <summary>Texto en minúsculas (nombre + usuario) para el buscador client-side de la vista.</summary>
    public string SubmitterSearchText(ExpenseReport report)
    {
        if (!UserById.TryGetValue(report.UserId, out var u))
            return report.UserId.ToString();

        return $"{u.FullName} {u.Username}".ToLowerInvariant();
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Pending = await _reports.ListPendingForApproverAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        Resolved = await _reports.ListResolvedByApproverAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);

        // ITenantUserAdminService.ListAsync() ya está acotado a la organización actual
        // (ver mismo patrón en Servicios/ClosingReportService), se resuelve una sola vez.
        var users = await _users.ListAsync(ct);
        UserById = users.DistinctBy(u => u.Id).ToDictionary(u => u.Id, u => u);
    }
}
