namespace PortalSaas.Abstractions.Modelos;

/// <summary>Bloqueante = va a Errors (tumba IsValid). Alerta = va a Warnings (nunca bloquea).</summary>
public enum GenericImportValidationSeverity
{
    Block,
    Warning,
}
