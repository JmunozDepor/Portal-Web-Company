-- Transferencias de stock (OWTR) a una bodega desde una fecha, no anuladas (Fase B, "Agregar pendientes").
-- Mismas columnas que la consulta por LPN. El portal descarta las que ya estan en un manifiesto suyo.
-- :from_date debe ser la fecha de puesta en marcha o posterior (no se consultan tablas del add-on).
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
WHERE T0."ToWhsCode" = :warehouse
  AND T0."DocDate" >= :from_date
  AND T0."CANCELED" = 'N'
ORDER BY T0."DocDate", T0."DocNum"
