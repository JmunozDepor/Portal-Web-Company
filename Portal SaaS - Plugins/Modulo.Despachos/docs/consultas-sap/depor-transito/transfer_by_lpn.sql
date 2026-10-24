-- DEPOR (modelo con almacen de transito). Transferencia por LPN.
-- El despacho a tienda es C_RETAIL -> 9999 (transito) con U_GSP_WHSDEST = sucursal y U_GSP_SENDTPV = 'Y'.
-- La recepcion en tienda (9999 -> sucursal) es una copia con el mismo LPN y sin U_GSP_WHSDEST: se excluye.
-- warehouse_* / destination_* = sucursal destino (OWHS por U_GSP_WHSDEST).
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
WHERE T0."U_NX_LPN" = :lpn
  AND T0."CANCELED" <> 'C'
  AND IFNULL(T0."U_GSP_WHSDEST", '') <> ''
  AND T0."U_GSP_SENDTPV" = 'Y'
