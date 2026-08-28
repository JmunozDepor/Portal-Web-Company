using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.EstadoServicio;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly WmsDbContext _contexto;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(WmsDbContext contexto, ICurrentCompanyAccessor currentCompany)
    {
        _contexto = contexto;
        _currentCompany = currentCompany;
    }

    public List<WmsServiceHeartbeat> Heartbeats { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Heartbeats = await _contexto.ServiceHeartbeats
            .Where(h => h.CompanyId == _currentCompany.CompanyId)
            .OrderBy(h => h.ProcessorKey)
            .ToListAsync(ct);
    }
}
