using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Comercial;

/// <summary>
/// Implementación real de IOrganizationDocumentPermissionService -- resuelve la
/// organización activa vía ICurrentUserContext.OrganizationId (nunca recibido desde la
/// UI). Sin fila de override para (organización, engine, tipo), cae al defaultValue del
/// catálogo estático del motor que llama -- falla hacia lo más estricto solo cuando el
/// default mismo ya es false, nunca inventa una restricción nueva sin configuración
/// explícita.
/// </summary>
public sealed class OrganizationDocumentPermissionService : IOrganizationDocumentPermissionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public OrganizationDocumentPermissionService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<bool> IsCreateAllowedAsync(string engine, string documentType, bool defaultValue, CancellationToken ct = default)
    {
        var overrideRow = await _db.OrganizationDocumentPermissions
            .Where(p => p.OrganizationId == _currentUser.OrganizationId && p.Engine == engine && p.DocumentType == documentType)
            .Select(p => (bool?)p.CanCreate)
            .FirstOrDefaultAsync(ct);

        return overrideRow ?? defaultValue;
    }
}
