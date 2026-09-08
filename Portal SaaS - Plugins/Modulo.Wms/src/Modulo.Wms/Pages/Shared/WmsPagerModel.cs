namespace Modulo.Wms.Pages.Shared;

/// <summary>
/// Modelo del partial <c>_WmsPager</c>: un paginador compacto con ventana
/// deslizante (página actual ± <see cref="Window"/>), accesos a primera/última y
/// salto directo. Reemplaza al listado plano de un botón por página, que con
/// cientos de páginas volvía la barra inusable.
/// </summary>
public sealed class WmsPagerModel
{
    public int CurrentPage { get; init; } = 1;

    public int TotalPages { get; init; }

    public int TotalCount { get; init; }

    /// <summary>Nombre del parámetro de query que transporta la página (ej. "Page", "Pagina").</summary>
    public string PageParam { get; init; } = "Pagina";

    /// <summary>Filtros vigentes a preservar en cada enlace; los valores nulos/vacíos se omiten.</summary>
    public IReadOnlyDictionary<string, string?> RouteValues { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>Páginas visibles a cada lado de la actual.</summary>
    public int Window { get; init; } = 2;

    public static WmsPagerModel Desde<T>(
        Models.WmsPagedResult<T> resultado,
        string pageParam,
        IReadOnlyDictionary<string, string?> routeValues) => new()
    {
        CurrentPage = resultado.Page,
        TotalPages = resultado.TotalPages,
        TotalCount = resultado.TotalCount,
        PageParam = pageParam,
        RouteValues = routeValues,
    };
}
