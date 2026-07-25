namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Datos de la compañía SAP activa para el usuario logueado en este request. La
/// compañía se fija en el login (ver Pages/Account/Login.cshtml.cs) y no cambia dentro
/// de la misma sesión -- para operar otra compañía el usuario debe cerrar sesión y
/// volver a entrar. Portado de PortalSAP_v2 (ICurrentEmpresaAccessor), renombrado
/// "Empresa" -> "Company" para alinear con el modelo de este proyecto (Entities/Company.cs).
/// </summary>
public interface ICurrentCompanyAccessor
{
    Guid CompanyId { get; }
    string Code { get; }
    string Database { get; }
    string ServiceLayerUrl { get; }
    string Country { get; }

    /// <summary>
    /// True si la sesión actual tiene una compañía seleccionada -- las demás
    /// propiedades tiran InvalidOperationException si esto es false. Necesario para
    /// código que debe comportarse distinto sin compañía (ej. IMenuNavigationService)
    /// en vez de depender de una excepción para control de flujo.
    /// </summary>
    bool HasCompany { get; }
}
