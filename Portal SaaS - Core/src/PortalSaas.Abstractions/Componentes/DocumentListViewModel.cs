namespace PortalSaas.Abstractions.Componentes;

/// <summary>
/// Modelo del chrome compartido "listado de documentos" estilo SAP B1 Web Client
/// (barra de filtros + tabla + paginación) -- portado de PortalSAP_v2
/// (DocumentListViewModel), consumido vía DocumentListViewComponent
/// (PortalSaas.Host.ViewComponents) para que ningún plugin tenga que reescribir esta UI.
/// </summary>
public sealed class DocumentListViewModel
{
    public required string Title { get; init; }

    public IReadOnlyList<DocumentListColumn> Columns { get; init; } = [];

    public IReadOnlyList<DocumentListRow> Rows { get; init; } = [];

    public IReadOnlyList<DocumentListFilter> Filters { get; init; } = [];

    public string? SearchText { get; init; }

    public bool ShowFreeTextSearch { get; init; } = true;

    /// <summary>Null = oculta el botón "Nuevo".</summary>
    public string? CreateUrl { get; init; }

    /// <summary>No-null = muestra un botón "Nuevo" deshabilitado con este título (PermiteCrear=false).</summary>
    public string? CreateDisabledTitle { get; init; }

    /// <summary>
    /// False = todavía no se ejecutó ninguna búsqueda contra SAP (primera carga del
    /// módulo, sin querystring) -- Rows/TotalRecords quedan vacíos a propósito, el
    /// listado no debe pagar el costo de una consulta remota (HANA/SQL Server del
    /// cliente) hasta que el usuario apriete "Filtrar" explícitamente.
    /// </summary>
    public bool HasSearched { get; init; } = true;

    public bool ShowPaging { get; init; }

    public int CurrentPage { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalRecords { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));

    public static readonly IReadOnlyList<int> AvailablePageSizes = [25, 50, 100];

    /// <summary>Pestañas sobre la grilla (vacío = sin pestañas).</summary>
    public IReadOnlyList<DocumentListTab> Tabs { get; init; } = [];

    /// <summary>Campos ocultos del form de filtros (ej. view=borradores, para no perder la pestaña al filtrar).</summary>
    public IReadOnlyDictionary<string, string> HiddenFilterFields { get; init; } = new Dictionary<string, string>();
}

/// <summary>Pestaña sobre la grilla (ej. "Documentos SAP | Borradores") -- navega por URL.</summary>
public sealed record DocumentListTab(string Label, string Url, bool IsActive, int? Count = null);

public sealed record DocumentListColumn(string Title, bool AlignRight = false);

public sealed record DocumentListRow(IReadOnlyList<string> Cells, string DetailUrl);

/// <param name="AllowEmpty">Select: false = sin la opción vacía "Todos" (el filtro siempre tiene un valor, ej. "Mis borradores").</param>
public sealed record DocumentListFilter(
    string Name,
    string Label,
    FilterFieldType FieldType,
    string? Value = null,
    IReadOnlyList<DocumentListFilterOption>? Options = null,
    bool AllowEmpty = true);

public sealed record DocumentListFilterOption(string Value, string Label);

public enum FilterFieldType
{
    Text,
    Date,
    Number,
    Select,
}
