-- DEPOR (modelo con almacen de transito). Transferencias pendientes de despacho a UNA sucursal ("Agregar pendientes").
-- Despacho = C_RETAIL -> 9999 con U_GSP_WHSDEST = :warehouse y U_GSP_SENDTPV = 'Y'.
-- Pendiente = la tienda aun no la recibe: no existe la transferencia de recepcion (sale del almacen de transito
-- hacia la sucursal con el mismo LPN; OWTR."Filler" = almacen de origen de la cabecera). Las ya despachadas por el portal las descarta el portal.
SELECT
  T0."DocEntry" AS "doc_entry",
  T0."DocNum" AS "doc_num",
  T0."DocDate" AS "document_date",
  T0."U_GSP_WHSDEST" AS "warehouse_code",
  W."WhsName" AS "warehouse_name",
  W."Street" AS "destination_address",
  W."County" AS "destination_commune",
  (SELECT SUM(L."Quantity") FROM WTR1 L WHERE L."DocEntry" = T0."DocEntry") AS "units",
  CASE WHEN T0."CANCELED" = 'Y' THEN 1 ELSE 0 END AS "is_canceled",
  T0."U_NX_LPN" AS "lpn"
FROM OWTR T0
LEFT JOIN OWHS W ON W."WhsCode" = T0."U_GSP_WHSDEST"
WHERE T0."U_GSP_WHSDEST" = :warehouse
  AND T0."U_GSP_SENDTPV" = 'Y'
  AND T0."DocDate" >= :from_date
  AND T0."CANCELED" = 'N'
  AND NOT EXISTS (
    SELECT 1 FROM OWTR R
    WHERE R."U_NX_LPN" = T0."U_NX_LPN"
      AND R."Filler" = T0."ToWhsCode"
      AND R."ToWhsCode" = T0."U_GSP_WHSDEST"
      AND R."CANCELED" = 'N')
ORDER BY T0."DocDate", T0."DocNum"
