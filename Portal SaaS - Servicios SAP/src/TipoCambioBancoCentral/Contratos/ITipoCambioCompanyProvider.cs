namespace Servicios.TipoCambioBancoCentral.Contratos;

/// <summary>
/// Único punto de entrada de configuración de compañías para este servicio -- mismo
/// espíritu que <c>Servicios.Common.Contratos.ICompanyProvider</c> ("ningún servicio nuevo
/// lee compañías directo de appsettings.json a mano", ver CLAUDE.md), pero con un tipo de
/// retorno propio (<see cref="TipoCambioCompanyConfig"/>) en vez de reutilizar
/// <c>CompanyConnectionConfig</c>, porque ese contrato exige campos de conexión HANA/SQL
/// Server (Host/Port/Schema/DbSecretCifrado) y de negocio de bodegas
/// (HeaderQuerySource/WarehouseAssignmentProcedure/CompletionUdfFieldName) que son propios
/// de TransferenciaAutomatica y no aplican acá -- el servicio legado de tipo de cambio
/// nunca conectó a la base, solo a Service Layer. Forzar esos campos habría significado
/// rellenarlos con datos falsos solo para satisfacer el shape.
/// </summary>
public interface ITipoCambioCompanyProvider
{
    Task<IReadOnlyList<TipoCambioCompanyConfig>> GetActiveCompaniesAsync(CancellationToken ct);
}
