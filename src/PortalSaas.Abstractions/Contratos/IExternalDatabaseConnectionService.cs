using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve la conexión a una base de datos EXTERNA propia de un plugin -- ajena al SAP
/// de la organización (eso es ICurrentCompanyAccessor/IHanaService) y ajena a la base
/// propia de la plataforma. Equivalente a ISqlServerService.ResolverConnectionStringAsync
/// de PortalSAP_v2, pero (a) motor dual desde el día uno (postgres/sqlserver, ver
/// ExternalDatabaseEngineType -- el original solo soportaba SQL Server) y (b) sin los
/// métodos QueryAsync/ExecuteAsync genéricos del original: acá el consumidor típico es
/// un DbContext de EF Core propio del plugin (ej. Modulo.Rendiciones), no SQL crudo.
///
/// moduleCode SIEMPRE debe ser IModuloPortal.ModuleCode del plugin que llama, nunca un
/// literal a mano -- un moduleCode incorrecto puede resolver en silencio a la base de
/// datos de OTRO módulo si esa fila existe (mismo riesgo documentado en el original).
/// </summary>
public interface IExternalDatabaseConnectionService
{
    /// <summary>
    /// Resuelve la conexión para (moduleCode, companyId) -- companyId SIEMPRE
    /// obligatorio, sin fallback a una fila global de la organización (regla dura del
    /// proyecto: todo plugin personaliza su persistencia por Company). Si el llamador no
    /// tiene una Company activa en sesión (ICurrentCompanyAccessor.HasCompany false), es
    /// responsabilidad del llamador rechazar la operación antes de llegar acá -- este
    /// método no adivina ni degrada a un alcance más amplio. Lanza
    /// InvalidOperationException con un mensaje presentable si no hay ninguna fila
    /// configurada para esa Company, indicando que hay que configurarla desde
    /// Administración.
    /// </summary>
    Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        CancellationToken ct = default);

    /// <summary>
    /// Lista (CompanyId, OrganizationId) de toda compañía con una conexión ACTIVA
    /// configurada para moduleCode -- para procesos sin sesión HTTP (background jobs)
    /// que necesitan recorrer todas las compañías de un módulo, algo que
    /// ResolveConnectionAsync no puede hacer porque ya exige conocer el companyId.
    /// </summary>
    Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default);
}
