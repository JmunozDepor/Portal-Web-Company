namespace Servicios.Common.Contratos;

/// <summary>
/// Copia deliberada del contrato de PortalSAP_v2/Proyecto Saas Portal (ver
/// docs/00-VINCULO-CON-PORTAL-SAAS.md) -- no una referencia de proyecto cruzada entre
/// soluciones. Si el contrato original cambia allá, este no se actualiza solo; portar el
/// cambio es una decisión explícita.
/// </summary>
public interface ISecretoCifradoService
{
    string Cifrar(string textoPlano);

    string Descifrar(string textoCifrado);
}
