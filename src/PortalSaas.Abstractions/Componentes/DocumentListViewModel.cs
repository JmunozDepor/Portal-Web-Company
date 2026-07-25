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

    public bool ShowPaging { get; init; }

    public int CurrentPage { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalRecords { get; init; }

    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));

    public static readonly IReadOnlyList<int> AvailablePageSizes = [25, 50, 100];
}

public sealed record DocumentListColumn(string Title, bool AlignRight = false);

public sealed record DocumentListRow(IReadOnlyList<string> Cells, string DetailUrl);

public sealed record DocumentListFilter(
    string Name,
    string Label,
    FilterFieldType FieldType,
    string? Value = null,
    IReadOnlyList<DocumentListFilterOption>? Options = null);

public sealed record DocumentListFilterOption(string Value, string Label);

public enum FilterFieldType
{
    Text,
    Date,
    Number,
    Select,
}
