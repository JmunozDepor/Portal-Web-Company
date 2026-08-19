namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Una fila de compania.HeaderQuerySource (legado: vista calculada _SYS_BIC.".../NX_AUTO_ABS").
/// Mismos 3 campos que leía Queries.QueryBuscarVistaCalculadaStock() del servicio legado.
/// </summary>
public sealed record HeaderDocument(int DocEntry, string ObjType, string CardCode);

/// <summary>
/// Una fila devuelta por compania.WarehouseAssignmentProcedure (legado: SP_DEP_ORDER_ABS)
/// para una iteración de prioridad de bodega. Transferencia es la cantidad a mover desde
/// WhsCodeDesde hacia WhsCode -- null/0 significa que esa bodega no aporta nada en esta
/// iteración (mismo criterio que el legado). Cuando no aporta nada, HANA devuelve la fila
/// con ItemCode/WhsCode/WhsCodeDesde en NULL también, por eso son nullable acá -- esas
/// filas nunca llegan a usarse (se descartan por Transferencia en Worker.cs).
/// </summary>
public sealed record WarehouseAllocationLine(string? ItemCode, int? Transferencia, string? WhsCode, string? WhsCodeDesde);
