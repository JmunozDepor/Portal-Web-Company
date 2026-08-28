namespace PortalSaas.Abstractions.Contratos.Integraciones;

/// <summary>
/// Resultado de intentar enviar UN registro al sistema externo -- permite al llamador
/// hacer ack individual por registro sin perder los que sí funcionaron aunque otros
/// del mismo lote hayan fallado.
/// </summary>
public sealed record IntegrationPushResult(IntegrationRecord Registro, bool Exito, string? MensajeError);
