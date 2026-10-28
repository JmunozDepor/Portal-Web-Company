namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>Adaptadores de recepción registrados, por código ("FACELE", ...). Misma forma que DteProviderRegistry.</summary>
public sealed class DteReceptionProviderRegistry
{
    private readonly Dictionary<string, IDteReceptionProvider> _providers;

    public DteReceptionProviderRegistry(IEnumerable<IDteReceptionProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Codes => _providers.Keys.OrderBy(k => k).ToList();

    public bool Contains(string code) => _providers.ContainsKey(code);

    public IDteReceptionProvider Get(string code) =>
        _providers.TryGetValue(code, out var p)
            ? p
            : throw new InvalidOperationException($"El proveedor «{code}» no tiene recepción de documentos disponible en esta instalación.");
}
