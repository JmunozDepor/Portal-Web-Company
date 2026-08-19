namespace Servicios.TipoCambioBancoCentral.Configuracion;

/// <summary>
/// Bindea la sección "BancoCentral" de appsettings.json. Credenciales globales del
/// servicio (no por compañía) porque la API del Banco Central de Chile es una única fuente
/// externa compartida por todas las compañías que este servicio sincroniza -- a diferencia
/// de Service Layer, que sí es por compañía (ver TipoCambioCompanySetting). El serie
/// "F073.TCO.PRE.Z.D" (Dólar Observado) queda fija en BancoCentralClient porque es un
/// identificador público del Banco Central, no un dato de configuración por cliente.
/// </summary>
public sealed class BancoCentralOptions
{
    public const string SeccionConfiguracion = "BancoCentral";

    public required string Url { get; set; }

    public required string Usuario { get; set; }

    /// <summary>Cifrado con ISecretoCifradoService -- nunca texto plano en appsettings.json.</summary>
    public required string SecretoCifrado { get; set; }
}
