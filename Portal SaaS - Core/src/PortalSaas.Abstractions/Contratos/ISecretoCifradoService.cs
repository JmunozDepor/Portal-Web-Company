namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Cifra/descifra secretos (contraseñas de conexión, claves de instancia/compañía)
/// que se guardan directo en la base propia de la plataforma -- self-service desde el
/// módulo de Administración: el admin tipea el secreto real una sola vez en la UI y el
/// portal lo guarda cifrado, sin volver a mostrarlo (write-only). Portado tal cual de
/// PortalSAP_v2 (`ISecretoCifradoService`). La clave maestra que hace posible el
/// cifrado vive fuera de la base (dotnet user-secrets en desarrollo, vault en
/// producción) -- nunca en appsettings.json.
/// </summary>
public interface ISecretoCifradoService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
