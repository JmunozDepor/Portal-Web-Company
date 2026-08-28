using Microsoft.Extensions.Configuration;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Comercial.Licenciamiento;

/// <summary>
/// Implementación real de IInstallationFingerprintProvider -- ver su doc-comment para
/// el porqué de guardarlo en un archivo y no en la base. Ruta configurable vía
/// Licensing:FingerprintFilePath, igual criterio que Plugins:ArtifactsFolder
/// (relativa a AppContext.BaseDirectory si no es absoluta); por defecto
/// "licensing/fingerprint.txt".
/// </summary>
public sealed class InstallationFingerprintProvider : IInstallationFingerprintProvider
{
    private readonly string _filePath;
    private string? _cached;

    public InstallationFingerprintProvider(IConfiguration configuration)
    {
        var configuredPath = configuration["Licensing:FingerprintFilePath"];
        _filePath = configuredPath is { Length: > 0 }
            ? (Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath))
            : Path.Combine(AppContext.BaseDirectory, "licensing", "fingerprint.txt");
    }

    public string GetOrCreate()
    {
        if (_cached is { Length: > 0 })
        {
            return _cached;
        }

        if (File.Exists(_filePath))
        {
            var existing = File.ReadAllText(_filePath).Trim();
            if (existing.Length > 0)
            {
                _cached = existing;
                return _cached;
            }
        }

        var fingerprint = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, fingerprint);
        _cached = fingerprint;
        return fingerprint;
    }
}
