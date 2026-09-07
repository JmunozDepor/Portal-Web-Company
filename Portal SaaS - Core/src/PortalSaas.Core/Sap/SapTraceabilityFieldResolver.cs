using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Resuelve el nombre del UDF de cabecera donde los 3 motores genéricos de documento
/// (Venta/Compra/Inventario) graban el usuario del portal que creó el documento
/// (trazabilidad). El nombre lo define el SAP de cada cliente -- p. ej. Comercial Depor
/// usa "U_DEP_PortalUsuario", no el default "U_PortalUser" -- por eso se configura por
/// compañía (regla dura del proyecto: personalización de SAP por CompanyId, nunca
/// hardcodeada, ver CLAUDE.md). Sin compañía en la sesión (o sin valor configurado) se
/// usa el default.
/// </summary>
public interface ISapTraceabilityFieldResolver
{
    Task<string> ResolveUserFieldNameAsync(CancellationToken ct = default);
}

public sealed class SapTraceabilityFieldResolver : ISapTraceabilityFieldResolver
{
    /// <summary>Nombre por defecto -- el mismo que usaba el proyecto antes de que fuera configurable.</summary>
    public const string DefaultUserFieldName = "U_PortalUser";

    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly PortalSaasDbContext _db;

    public SapTraceabilityFieldResolver(ICurrentCompanyAccessor currentCompany, PortalSaasDbContext db)
    {
        _currentCompany = currentCompany;
        _db = db;
    }

    public async Task<string> ResolveUserFieldNameAsync(CancellationToken ct = default)
    {
        if (!_currentCompany.HasCompany)
        {
            return DefaultUserFieldName;
        }

        var companyId = _currentCompany.CompanyId;
        var configured = await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.TraceabilityUserUdfName)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(configured) ? DefaultUserFieldName : configured.Trim();
    }
}
