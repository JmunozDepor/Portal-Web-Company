using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Firma y verifica el estado de una licencia on-premise (ECDSA P-256) -- mismo
/// criterio de "criptografía nativa sin dependencias externas" que ISecretoCifradoService
/// (AES-256-GCM), pero asimétrico porque acá el que firma (servidor central,
/// Licensing:SigningPrivateKey) y el que verifica (instalación on-premise,
/// Licensing:CentralPublicKey) son procesos físicamente distintos.
/// </summary>
public interface ILicenseTokenService
{
    /// <summary>Solo funciona si hay clave privada configurada (rol Central). Lanza si no.</summary>
    string Sign(LicenseStatusPayload payload);

    /// <summary>Verifica la firma con la clave pública (disponible en ambos roles). False si el token está corrupto, fue editado a mano, o la firma no corresponde a la clave pública configurada.</summary>
    bool TryVerify(string token, out LicenseStatusPayload? payload);
}
