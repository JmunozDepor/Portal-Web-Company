namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Adaptadores DTE registrados en DI, por código (ej. "FACELE").</summary>
public sealed class DteProviderRegistry
{
    private readonly Dictionary<string, IDteProvider> _providers;

    public DteProviderRegistry(IEnumerable<IDteProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Codes => _providers.Keys.OrderBy(k => k).ToList();

    public bool Contains(string code) => _providers.ContainsKey(code);

    public IDteProvider Get(string code) =>
        _providers.TryGetValue(code, out var p)
            ? p
            : throw new InvalidOperationException($"El proveedor de documentos electrónicos «{code}» no está disponible en esta instalación.");
}
