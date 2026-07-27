namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de NNM1 (serie de numeración de documento) de la compañía SAP activa. Non-positional -- ver CustomerDto.</summary>
public sealed record SeriesDto
{
    public int SeriesCode { get; init; }
    public string SeriesName { get; init; } = null!;
}
