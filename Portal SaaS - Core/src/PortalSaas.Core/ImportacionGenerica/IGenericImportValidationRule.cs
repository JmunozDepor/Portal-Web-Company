using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Una regla de negocio adicional sobre una fila ya resuelta (artículo/almacén/cuenta/
/// socio ya cruzados contra SAP) -- GenericImportService.BuiltInRules arranca con un set
/// mínimo (Cantidad positiva, Descuento en rango); agregar una regla nueva es una clase
/// más en esa lista, nunca hay que tocar la resolución de filas para sumar una
/// validación. Portado de IReglaValidacionImportacionGenerica.
/// </summary>
public interface IGenericImportValidationRule
{
    /// <summary>Devuelve un mensaje de error por cada problema encontrado -- vacío si la fila pasa esta regla.</summary>
    IEnumerable<string> Validate(GenericImportRowDto row);
}

internal sealed class PositiveQuantityRule : IGenericImportValidationRule
{
    public IEnumerable<string> Validate(GenericImportRowDto row)
    {
        if (row.Quantity is null or <= 0)
        {
            yield return "Cantidad debe ser mayor a 0.";
        }
    }
}

internal sealed class ValidDiscountPercentRule : IGenericImportValidationRule
{
    public IEnumerable<string> Validate(GenericImportRowDto row)
    {
        if (row.DiscountPercent is { } discount && (discount < 0 || discount > 100))
        {
            yield return "PorcentajeDescuento debe estar entre 0 y 100.";
        }
    }
}
