using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Aprobaciones;

/// <summary>
/// Bandeja del aprobador: informes Pending cuyo CurrentLevel le corresponde al usuario
/// logueado. Reutiliza Pages/Informes/Detalle para Aprobar/Rechazar, sin duplicar la
/// pantalla.
/// </summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpenseReportService _reports;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseReportService reports, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _reports = reports;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReport> Pending { get; private set; } = Array.Empty<ExpenseReport>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Pending = await _reports.ListPendingForApproverAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
    }
}
