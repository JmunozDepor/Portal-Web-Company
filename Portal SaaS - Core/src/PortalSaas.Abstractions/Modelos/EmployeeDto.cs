namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de OHEM (empleado) de la compañía SAP activa -- universo completo de
/// empleados, a diferencia de ISalesEmployeeCatalogService (OSLP, solo vendedores).
/// Prerrequisito de Modulo.Rendiciones. Non-positional -- ver CustomerDto.
/// </summary>
public sealed record EmployeeDto
{
    public int EmployeeCode { get; init; }
    public string EmployeeName { get; init; } = null!;
}
