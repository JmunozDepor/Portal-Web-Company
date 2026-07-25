namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de OCRD (CardType='C') de la compañía SAP activa. Non-positional (constructor
/// sin parámetros + propiedades con setter) a propósito -- IHanaService.QueryAsync
/// mapea filas vía RowReflectionMapper, que instancia con Activator.CreateInstance y
/// asigna por reflection, no acepta records posicionales.
/// </summary>
public sealed record CustomerDto
{
    public string CardCode { get; init; } = null!;
    public string CardName { get; init; } = null!;
    public string? PriceListCode { get; init; }
    public string? PaymentTermsCode { get; init; }
}

public sealed record CustomerFilter(string? SearchText = null);
