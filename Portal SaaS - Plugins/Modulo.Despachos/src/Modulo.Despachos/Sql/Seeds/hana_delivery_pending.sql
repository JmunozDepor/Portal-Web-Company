-- Entregas (ODLN) abiertas de un cliente desde una fecha, para Entregas con guia previa.
-- Una Entrega abierta = no facturada ni cerrada (DocStatus O) y no anulada.
-- lpn = NumAtCard de la Entrega (lo envia el WMS).
-- customer_po y order_date = N de referencia (NumAtCard) y fecha del pedido base (ORDR, lineas DLN1 de tipo 17).
-- po_count = cantidad de OC distintas en las lineas; mas de una impide agrupar la Entrega.
-- no_po_lines (opcional) = lineas sin OC (sin pedido base o pedido sin N de referencia); tambien impide agruparla.
-- net_amount = total sin IVA de la Entrega.
SELECT
  T0."DocEntry" AS "doc_entry",
  T0."DocNum" AS "doc_num",
  T0."DocDate" AS "document_date",
  T0."CardCode" AS "customer_code",
  COALESCE(C0."CardName", T0."CardName") AS "customer_name",
  T0."LicTradNum" AS "customer_tax_id",
  T2."StreetS" AS "destination_address",
  T2."CountyS" AS "destination_commune",
  T0."NumAtCard" AS "lpn",
  (SELECT MAX(O."NumAtCard") FROM DLN1 L
     INNER JOIN ORDR O ON O."DocEntry" = L."BaseEntry" AND L."BaseType" = 17
     WHERE L."DocEntry" = T0."DocEntry") AS "customer_po",
  (SELECT MAX(O."DocDate") FROM DLN1 L
     INNER JOIN ORDR O ON O."DocEntry" = L."BaseEntry" AND L."BaseType" = 17
     WHERE L."DocEntry" = T0."DocEntry") AS "order_date",
  (SELECT COUNT(DISTINCT NULLIF(TRIM(O."NumAtCard"), '')) FROM DLN1 L
     LEFT JOIN ORDR O ON O."DocEntry" = L."BaseEntry" AND L."BaseType" = 17
     WHERE L."DocEntry" = T0."DocEntry") AS "po_count",
  (SELECT COUNT(*) FROM DLN1 L
     LEFT JOIN ORDR O ON O."DocEntry" = L."BaseEntry" AND L."BaseType" = 17
     WHERE L."DocEntry" = T0."DocEntry" AND NULLIF(TRIM(O."NumAtCard"), '') IS NULL) AS "no_po_lines",
  (SELECT SUM(L."Quantity") FROM DLN1 L WHERE L."DocEntry" = T0."DocEntry") AS "units",
  T0."DocTotal" - T0."VatSum" AS "net_amount",
  CASE WHEN T0."DocStatus" = 'O' AND T0."CANCELED" = 'N' THEN 1 ELSE 0 END AS "is_open"
FROM ODLN T0
LEFT JOIN OCRD C0 ON C0."CardCode" = T0."CardCode"
LEFT JOIN DLN12 T2 ON T2."DocEntry" = T0."DocEntry"
WHERE T0."CardCode" = :customer
  AND T0."DocDate" >= :from_date
  AND T0."DocStatus" = 'O'
  AND T0."CANCELED" = 'N'
ORDER BY T0."DocDate", T0."DocNum"
