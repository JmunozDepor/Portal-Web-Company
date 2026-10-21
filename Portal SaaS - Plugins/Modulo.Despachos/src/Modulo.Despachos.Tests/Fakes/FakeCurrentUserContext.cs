using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>
/// Usuario de prueba. Los permisos se configuran con <see cref="Grant"/> (código de menú
/// calificado, ej. "Despachos.manifiestos", + acciones); IsAdmin no otorga permisos por sí
/// solo (igual que el contexto real: el gate pasa por HasActionAsync).
/// </summary>
public sealed class FakeCurrentUserContext : ICurrentUserContext
{
    private readonly HashSet<(string Menu, string Action)> _granted = new();

    public Guid UserId { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "tester";
    public bool IsAdmin { get; set; }
    public Guid OrganizationId { get; set; } = Guid.NewGuid();

    /// <summary>Consultas recibidas, útil para verificar que se califica con "Despachos.".</summary>
    public List<(string Menu, string Action)> Queried { get; } = new();

    public FakeCurrentUserContext Grant(string menuCode, params string[] actions)
    {
        foreach (var a in actions) _granted.Add((menuCode, a));
        return this;
    }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default)
    {
        Queried.Add((menuCode, actionCode));
        return Task.FromResult(_granted.Contains((menuCode, actionCode)));
    }
}
