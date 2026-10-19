namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Formulario personalizado de los documentos genéricos: qué campos del
/// DocumentFormFieldCatalog se ocultan, por compañía. El admin de la organización define
/// la base de cada compañía; cada usuario puede guardar su propia versión encima (que
/// reemplaza a la base entera) o restablecerla. Toda clave se filtra contra el catálogo.
/// </summary>
public interface IFormLayoutService
{
    /// <summary>Lo que ve el usuario actual en la compañía activa (sin compañía: nada oculto).</summary>
    Task<FormLayoutDto> GetForCurrentUserAsync(string formKey, CancellationToken ct = default);

    Task SaveForCurrentUserAsync(string formKey, IReadOnlyCollection<string> hiddenFields, CancellationToken ct = default);

    /// <summary>Borra la versión propia del usuario -- vuelve a ver la base de la compañía.</summary>
    Task ResetForCurrentUserAsync(string formKey, CancellationToken ct = default);

    /// <summary>Base de una compañía de la organización actual -- solo admin.</summary>
    Task<IReadOnlyList<string>> GetCompanyBaseAsync(Guid companyId, string formKey, CancellationToken ct = default);

    /// <summary>Guarda la base de una compañía de la organización actual -- solo admin.</summary>
    Task SaveCompanyBaseAsync(Guid companyId, string formKey, IReadOnlyCollection<string> hiddenFields, CancellationToken ct = default);
}

/// <param name="HiddenFields">Efectivo: la versión propia si existe, si no la base.</param>
/// <param name="BaseHiddenFields">Base de la compañía (definida por el admin).</param>
/// <param name="HasUserOverride">True si el usuario tiene versión propia guardada.</param>
public sealed record FormLayoutDto(IReadOnlyList<string> HiddenFields, IReadOnlyList<string> BaseHiddenFields, bool HasUserOverride)
{
    public static readonly FormLayoutDto Empty = new([], [], false);
}
