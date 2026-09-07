using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Una regla del catálogo de validación pre-carga (ver docs/superpowers/specs/2026-09-
/// 07-reglas-validacion-importacion-generica-design.md). A diferencia del spec original
/// (que separaba reglas "por fila" de "por lote"), acá TODAS las reglas reciben el
/// archivo completo de una vez -- incluso una regla conceptualmente "por fila" (ej.
/// PriceVsFixedList) necesita batch-fetch contra SAP de todos los ItemCode distintos
/// antes de poder validar cada fila, exactamente el mismo criterio anti-N+1 que ya usa
/// GenericImportService.ProcessFileAsync para items/almacenes/cuentas -- una interfaz
/// única evita duplicar esa lógica de batch-fetch en dos formas distintas.
/// </summary>
public interface IGenericImportValidationRule
{
    GenericImportValidationRuleType RuleType { get; }

    /// <summary>Módulos a los que aplica -- el motor no corre la regla fuera de su módulo aunque esté configurada (defensa en profundidad).</summary>
    IReadOnlyList<GenericImportModule> ApplicableModules { get; }

    /// <summary>false = la severidad SIEMPRE es Block sin importar lo que traiga el RuleAssignment (PositiveQuantity/ValidDiscountPercent) -- esas dos ni siquiera necesitan un RuleAssignment, el motor las corre siempre.</summary>
    bool SeverityIsConfigurable { get; }

    /// <summary>
    /// Corre sobre TODAS las filas del archivo de una vez. Devuelve, por cada
    /// RowNumber con problema, la lista de mensajes de ESA regla para esa fila (vacío
    /// si no aplica esta regla a ninguna fila). `parameters` viene de
    /// GenericImportValidationRuleAssignmentDto.Parameters -- vacío para las reglas
    /// estructurales fijas.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);
}
