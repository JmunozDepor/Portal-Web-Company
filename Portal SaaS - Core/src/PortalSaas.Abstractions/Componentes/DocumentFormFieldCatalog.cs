namespace PortalSaas.Abstractions.Componentes;

/// <summary>Campo ocultable de un formulario genérico. Key solo [a-z0-9-] -- se usa tal cual en data-form-field y en un selector CSS.</summary>
public sealed record FormFieldDefinition(string Key, string Label, string Section);

/// <summary>Formulario personalizable -- uno por motor genérico de documento.</summary>
public sealed record FormDefinition(string Key, string Name, IReadOnlyList<FormFieldDefinition> Fields);

/// <summary>
/// Catálogo ÚNICO de campos que el usuario (o el admin, como base de la compañía) puede
/// ocultar en los formularios genéricos de documento -- "formulario personalizado"
/// (2026-10-19). Por motor, no por tipo de documento: los 7 tipos de Venta comparten
/// el mismo formulario (decisión del dueño del proyecto). Vive en Abstractions porque lo
/// consumen el servicio (valida que nunca se guarde una clave fuera de acá), los 3
/// plugins de documento (marcan cada campo con data-form-field) y la pantalla de admin
/// de Modulo.Administracion (que no puede referenciar a otros plugins).
///
/// REGLA: un campo que SAP exige o que el formulario necesita para funcionar NUNCA va
/// acá (Cliente/Proveedor, Fecha de documento, Tipo de línea, Artículo, Cantidad,
/// Almacén, Cuenta mayor, Centro de costos). Ocultar es solo visual: un campo oculto
/// sigue posteando su valor (ej. un precio autocompletado o el valor por defecto).
/// </summary>
public static class DocumentFormFieldCatalog
{
    public const string Sales = "ventas.documento";
    public const string Purchase = "compras.documento";
    public const string Inventory = "inventario.documento";

    public const string SectionHeader = "Cabecera";
    public const string SectionLines = "Líneas";
    public const string SectionLogistics = "Logística";
    public const string SectionFinance = "Finanzas";

    public static readonly IReadOnlyList<FormDefinition> Forms =
    [
        new(Sales, "Documentos de venta",
        [
            new("ref-socio", "N° referencia cliente", SectionHeader),
            new("vendedor", "Vendedor", SectionHeader),
            new("lista-precio", "Lista de precio", SectionHeader),
            new("serie", "Serie", SectionHeader),
            new("moneda", "Moneda", SectionHeader),
            new("fecha-vencimiento", "Fecha de vencimiento", SectionHeader),
            new("fecha-contabilizacion", "Fecha de contabilización", SectionHeader),
            new("comentarios", "Comentarios", SectionHeader),
            new("linea-descripcion", "Descripción (Artículo)", SectionLines),
            new("linea-precio", "Precio unitario", SectionLines),
            new("linea-descuento", "% Desc.", SectionLines),
            new("linea-marca", "Marca (Servicio)", SectionLines),
            new("linea-tipo-gasto", "Tipo de gasto (Servicio)", SectionLines),
            new("sucursal-destino", "Sucursal de destino", SectionLogistics),
            new("condicion-pago", "Condición de pago", SectionFinance),
        ]),
        new(Purchase, "Documentos de compra",
        [
            new("ref-socio", "N° referencia proveedor", SectionHeader),
            new("comprador", "Comprador", SectionHeader),
            new("lista-precio", "Lista de precio", SectionHeader),
            new("serie", "Serie", SectionHeader),
            new("moneda", "Moneda", SectionHeader),
            new("fecha-vencimiento", "Fecha de vencimiento", SectionHeader),
            new("fecha-contabilizacion", "Fecha de contabilización", SectionHeader),
            new("comentarios", "Comentarios", SectionHeader),
            new("linea-descripcion", "Descripción (Artículo)", SectionLines),
            new("linea-precio", "Precio unitario", SectionLines),
            new("linea-descuento", "% Desc.", SectionLines),
            new("linea-marca", "Marca (Servicio)", SectionLines),
            new("linea-tipo-gasto", "Tipo de gasto (Servicio)", SectionLines),
        ]),
        new(Inventory, "Documentos de inventario",
        [
            new("socio", "Socio de negocio", SectionHeader),
            new("ref-socio", "N° referencia", SectionHeader),
            new("serie", "Serie", SectionHeader),
            new("comentarios", "Comentarios", SectionHeader),
            new("linea-descripcion", "Descripción", SectionLines),
        ]),
    ];

    public static FormDefinition? Find(string formKey) =>
        Forms.FirstOrDefault(f => string.Equals(f.Key, formKey, StringComparison.Ordinal));

    /// <summary>Deja solo claves que existen en el formulario, sin repetir -- nunca confiar en lo que llega de la UI.</summary>
    public static IReadOnlyList<string> Sanitize(string formKey, IEnumerable<string>? keys)
    {
        var form = Find(formKey);
        if (form is null || keys is null)
        {
            return [];
        }

        var valid = form.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        return keys.Where(k => k is not null && valid.Contains(k)).Distinct(StringComparer.Ordinal).ToList();
    }
}
