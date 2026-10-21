-- Guia de traslado por folio (UDO consolidado @INX_ODGC.U_FoliNo). Adaptada de la consulta de manifiestos de Depor.
-- Sin esquema fijo: aplica el esquema actual de la conexion SAP de la compania.
-- Destino = bodega U_ToWhsCode (OWHS); unidades desde las OWTR enlazadas en @INX_DGC1.U_BseEntry; bultos = U_CardBord distintos.
SELECT
  H."DocEntry" AS "doc_entry",
  H."DocNum" AS "doc_num",
  H."U_FoliNo" AS "folio",
  52 AS "sii_document_type",
  H."U_DocDate" AS "document_date",
  W."WhsCode" AS "customer_code",
  W."WhsName" AS "customer_name",
  (SELECT MIN(A0."TaxIdNum") FROM OADM A0) AS "customer_tax_id",
  W."Street" AS "destination_address",
  W."County" AS "destination_commune",
  '' AS "customer_po",
  CAST(NULL AS INTEGER) AS "base_order_entry",
  (SELECT SUM(WT."Quantity")
     FROM WTR1 WT
     WHERE WT."DocEntry" IN (SELECT G1."U_BseEntry" FROM "@INX_DGC1" G1 WHERE G1."DocEntry" = H."DocEntry")) AS "units",
  (SELECT COUNT(DISTINCT G2."U_CardBord") FROM "@INX_DGC1" G2 WHERE G2."DocEntry" = H."DocEntry") AS "packages",
  (SELECT SUM(G3."U_Amount") FROM "@INX_DGC1" G3 WHERE G3."DocEntry" = H."DocEntry") AS "total_amount",
  CASE WHEN H."Canceled" = 'Y' THEN 1 ELSE 0 END AS "is_canceled"
FROM "@INX_ODGC" H
LEFT JOIN OWHS W ON W."WhsCode" = H."U_ToWhsCode"
WHERE H."U_FoliNo" = :folio
