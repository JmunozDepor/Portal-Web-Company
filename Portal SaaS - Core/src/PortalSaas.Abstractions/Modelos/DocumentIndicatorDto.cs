namespace PortalSaas.Abstractions.Modelos;

/// <summary>Indicador de documento de SAP (OIDC) -- ej. "Factura Electrónica". Non-positional: HanaService lo instancia vía reflection.</summary>
public sealed record DocumentIndicatorDto
{
    public string Code { get; init; } = string.Empty;
    public string? Name { get; init; }
}
