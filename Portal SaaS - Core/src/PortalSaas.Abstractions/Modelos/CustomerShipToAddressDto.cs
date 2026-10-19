namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de CRD1 (AdresType = 'S') -- non-positional, ver el doc-comment de CustomerDto.</summary>
public sealed record CustomerShipToAddressDto
{
    /// <summary>CRD1.Address -- el código que se postea como ShipToCode.</summary>
    public string Code { get; init; } = null!;
    public string? Street { get; init; }
    public string? Block { get; init; }
    public string? City { get; init; }
    public string? County { get; init; }

    /// <summary>Calle, Bloque, Comuna, Ciudad -- solo los no vacíos.</summary>
    public string FullAddress => string.Join(", ", new[] { Street, Block, County, City }
        .Where(part => !string.IsNullOrWhiteSpace(part))
        .Select(part => part!.Trim()));
}
