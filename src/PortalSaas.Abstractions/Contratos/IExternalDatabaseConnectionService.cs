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
    /// Resuelve la conexión para (moduleCode, organizationId, companyId opcional):
    /// busca primero una fila puntual para companyId, si no hay reintenta con la fila
    /// global de la organización para ese módulo (CompanyId IS NULL -- caso de un
    /// módulo sin concepto de compañía). Si ninguna existe, lanza
    /// InvalidOperationException con un mensaje presentable indicando que hay que
    /// configurar la conexión desde Administración.
    /// </summary>
    Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid organizationId,
        Guid? companyId,
        CancellationToken ct = default);
}
