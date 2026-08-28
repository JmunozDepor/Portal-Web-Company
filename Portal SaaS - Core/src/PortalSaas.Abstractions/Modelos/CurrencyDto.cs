namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OCRN (moneda) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record CurrencyDto
{
    public string CurrencyCode { get; init; } = null!;
    public string CurrencyName { get; init; } = null!;
}
