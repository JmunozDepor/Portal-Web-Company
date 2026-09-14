namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Token opaco de sesión para la PWA de captura (Api/V1) -- deliberadamente NO usa
/// JWT/ASP.NET Core Identity: no está confirmado que PluginManager (AssemblyLoadContext
/// aislado por plugin) permita registrar un esquema de autenticación propio del lado
/// del Host, así que este scaffold usa el patrón más simple y auditable (token
/// aleatorio guardado en la base propia del plugin, validado a mano en cada
/// controller) -- ver PENDIENTE.md, "confirmar con el Host antes de invertir en JWT".
/// </summary>
public class CaptureAuthToken
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required long CaptureUserId { get; set; }

    public required string Token { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public required DateTimeOffset ExpiresAt { get; set; }
}
