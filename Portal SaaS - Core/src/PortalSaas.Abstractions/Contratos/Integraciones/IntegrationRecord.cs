namespace PortalSaas.Abstractions.Contratos.Integraciones;

public sealed class IntegrationRecord
{
    public IntegrationRecord(IReadOnlyDictionary<string, object?> fields)
    {
        Fields = fields;
    }

    public IReadOnlyDictionary<string, object?> Fields { get; }

    public object? this[string campo] => Fields.TryGetValue(campo, out var valor) ? valor : null;
}
