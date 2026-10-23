-- Lineas de una transferencia de stock (WTR1) para la guia de traslado interno (Fase B): una fila por linea.
-- Mismo contrato que "Lineas de factura" (item_name = OITM."FrgnName", peso, model_code/model_name), sin monto
-- (la guia de traslado interno no se valoriza) y sin giro (el receptor es la propia compania).
-- destination_city = ciudad de la bodega destino (OWHS."City") [validar].
SELECT
  L."LineNum" AS "line_num",
  L."ItemCode" AS "item_code",
  COALESCE(NULLIF(TRIM(I."FrgnName"), ''), L."Dscription") AS "item_name",
  L."Quantity" AS "quantity",
  L."unitMsr" AS "unit",
  CASE WHEN I."SWeight1" > 0 AND W."WightInMG" > 0
       THEN L."Quantity" * I."SWeight1" * W."WightInMG" / 1000000
       ELSE NULL END AS "weight_kg",
  NULL AS "customer_activity",
  H."City" AS "destination_city",
  I."U_GSP_REFERENCE" AS "model_code",
  (SELECT MAX(NULLIF(TRIM(X."FrgnName"), '')) FROM OITM X WHERE X."U_GSP_REFERENCE" = I."U_GSP_REFERENCE") AS "model_name"
FROM WTR1 L
INNER JOIN OWTR T0 ON T0."DocEntry" = L."DocEntry"
LEFT JOIN OITM I ON I."ItemCode" = L."ItemCode"
LEFT JOIN OWGT W ON W."UnitCode" = I."SWght1Unit"
LEFT JOIN OWHS H ON H."WhsCode" = T0."ToWhsCode"
WHERE L."DocEntry" = :doc_entry
ORDER BY L."LineNum"
