namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Identificador estable de ESTA instalación física on-premise, persistido fuera de la
/// base de datos (un archivo local) a propósito: si viviera solo en la BD, un
/// backup/restore de esa base en otro servidor "traería consigo" el fingerprint y el
/// servidor central no podría distinguir la copia del original. Se genera una única
/// vez y no cambia mientras el archivo exista.
/// </summary>
public interface IInstallationFingerprintProvider
{
    string GetOrCreate();
}
