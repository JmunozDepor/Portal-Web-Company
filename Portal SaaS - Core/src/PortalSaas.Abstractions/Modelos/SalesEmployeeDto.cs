namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OSLP ("vendedor") de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record SalesEmployeeDto
{
    public int SalesEmployeeCode { get; init; }
    public string SalesEmployeeName { get; init; } = null!;
}
