namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de OCRD (CardType='S') de la compañía SAP activa -- lado proveedor de OCRD,
/// mismo patrón que CustomerDto (CardType='C'). Non-positional a propósito -- ver el
/// doc-comment de CustomerDto (RowReflectionMapper no acepta records posicionales).
/// </summary>
public sealed record SupplierDto
{
    public string CardCode { get; init; } = null!;
    public string CardName { get; init; } = null!;
}

public sealed record SupplierFilter(string? SearchText = null);
