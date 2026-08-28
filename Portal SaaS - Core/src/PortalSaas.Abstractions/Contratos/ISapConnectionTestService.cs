using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Prueba la conexión real a SAP de una Company puntual (base directa + Service Layer),
/// sin depender de ICurrentCompanyAccessor -- a diferencia de HanaService/
/// SapConnectionProvider (scoped a la compañía activa de una sesión de tenant), esto lo
/// usa el backoffice de administrador de plataforma (otra sesión, sin compañía activa)
/// para verificar que una Company recién configurada realmente llega al SAP real antes
/// de dársela a un cliente.
/// </summary>
public interface ISapConnectionTestService
{
    Task<SapConnectionTestResult> TestAsync(Guid companyId, CancellationToken ct = default);
}
