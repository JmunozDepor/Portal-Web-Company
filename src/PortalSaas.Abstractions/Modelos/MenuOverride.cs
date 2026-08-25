namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de la pantalla de personalización de menús -- un nodo de Menu con su override actual (si existe) para la organización actual.</summary>
public sealed class MenuOverrideRowDto
{
    public required long MenuId { get; init; }
    public required int Level { get; init; }
    public required string OriginModule { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? CustomLabel { get; init; }
    public int? CustomOrder { get; init; }
    public required bool IsHidden { get; init; }
}

/// <summary>Valor a guardar para un nodo puntual -- CustomLabel/CustomOrder vacíos + IsHidden=false borra el override (vuelve a heredar).</summary>
public sealed record MenuOverrideInput(string? CustomLabel, int? CustomOrder, bool IsHidden);
