namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Permite fijar temporalmente qué CompanyId debe usar ICurrentCompanyAccessor cuando
/// no hay HttpContext -- ej. un BackgroundService procesando el ciclo de una
/// integración de una compañía puntual. Scoped: vive y muere con el scope de ese
/// ciclo de trabajo, nunca se comparte entre requests/ciclos distintos.
/// </summary>
public interface ICurrentCompanyOverride
{
    Guid? CompanyId { get; }
    void Set(Guid companyId);
    void Clear();
}
