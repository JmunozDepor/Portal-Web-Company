-- Transferencia de stock (OWTR) por LPN (OWTR."U_NX_LPN", 1 transferencia = 1 LPN = 1 bulto), Fase B.
-- Sin esquema fijo: aplica el esquema actual de la conexion SAP de la compania.
-- warehouse_* / destination_* = bodega destino (OWHS por OWTR."ToWhsCode") [validar: direccion y comuna en OWHS].
-- No se consultan tablas del add-on: el control de transferencias ya despachadas lo lleva el portal.
-- CANCELED = 'C' es el documento de cancelacion que SAP crea al anular (copia los campos de usuario): se excluye.
SELECT
  T0."DocEntry" AS "doc_entry",
  T0."DocNum" AS "doc_num",
  T0."DocDate" AS "document_date",
  T0."ToWhsCode" AS "warehouse_code",
  W."WhsName" AS "warehouse_name",
  W."Street" AS "destination_address",
  W."County" AS "destination_commune",
  (SELECT SUM(L."Quantity") FROM WTR1 L WHERE L."DocEntry" = T0."DocEntry") AS "units",
  CASE WHEN T0."CANCELED" = 'Y' THEN 1 ELSE 0 END AS "is_canceled",
  T0."U_NX_LPN" AS "lpn"
FROM OWTR T0
LEFT JOIN OWHS W ON W."WhsCode" = T0."ToWhsCode"
WHERE T0."U_NX_LPN" = :lpn
  AND T0."CANCELED" <> 'C'
