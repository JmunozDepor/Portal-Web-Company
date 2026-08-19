namespace Servicios.TransferenciaAutomatica_v2.Domain;

/// <summary>
/// Una fila de compania.HeaderQuerySource -- a diferencia de v1, acá el filtro de "sin
/// picking" y el ORDER BY "nuevos primero" van dentro del propio HeaderQuerySource que
/// escribe cada compañía (convención documentada en el CLAUDE.md de este proyecto), no en
/// código. DocNum se agrega respecto a v1 para poder loguearlo (documento visible al
/// usuario en SAP, distinto de DocEntry).
/// </summary>
public sealed record HeaderDocument(int DocEntry, int DocNum, string ObjType, string CardCode);

/// <summary>Una línea del documento (legado: t0 en el SP), leída directo de la tabla de detalle.</summary>
public sealed record LineaDocumento(string ItemCode, string WhsCode, decimal Cantidad);

/// <summary>
/// Una fila de la tabla normalizada WarehousePriorityTable -- reemplaza a las columnas
/// fijas U_WhsCode1/2/3 de @DEP_ORDEN_ASIG_STK. Prioridad 1 = primera bodega a intentar.
/// </summary>
public sealed record PrioridadBodega(int Prioridad, string WhsCodeOrigen);
