namespace Servicios.TipoCambioBancoCentral.Contratos;

/// <summary>
/// Todo lo que este servicio necesita para sincronizar el Tipo de Cambio USD de UNA
/// compañía contra su Service Layer. A diferencia de <c>Servicios.Common.Contratos.
/// CompanyConnectionConfig</c> (usado por TransferenciaAutomatica) NO incluye conexión
/// directa a la base HANA/SQL Server -- el servicio legado (Service1.cs) nunca tocó la
/// base, solo Service Layer vía B1SLayer, así que forzar campos de DB acá sería dato falso
/// sin uso. Es un contrato propio de este servicio, no una reutilización forzada de
/// ICompanyProvider -- ver ITipoCambioCompanyProvider para el detalle de por qué se
/// diverge del contrato compartido.
/// </summary>
public sealed class TipoCambioCompanyConfig
{
    /// <summary>Código corto de la compañía, usado solo para logging/diagnóstico.</summary>
    public required string CompanyCode { get; init; }

    public required string ServiceLayerUrl { get; init; }

    public required string ServiceLayerDb { get; init; }

    public required string ServiceLayerUsername { get; init; }

    /// <summary>Cifrado con ISecretoCifradoService -- se descifra recién al momento de conectar.</summary>
    public required string ServiceLayerSecretCifrado { get; init; }
}
