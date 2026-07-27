namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OACT (Plan de cuentas) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record GeneralLedgerAccountDto
{
    public string AccountCode { get; init; } = null!;
    public string AccountName { get; init; } = null!;
}
