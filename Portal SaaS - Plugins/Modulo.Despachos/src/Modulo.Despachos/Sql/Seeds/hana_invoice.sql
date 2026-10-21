-- Factura de venta por folio (OINV.FolioNum). Adaptada de la consulta de manifiestos de Depor.
-- Sin esquema fijo: aplica el esquema actual de la conexion SAP de la compania.
-- El parametro sii_document_type en 0 no filtra; 33 o 34 desambigua un FolioNum repetido.
-- Bultos = LPN distintos de las entregas base (ODLN.U_NX_LPN); pedido base = MIN(DLN1.BaseEntry) de tipo 17.
SELECT
  T0."DocEntry" AS "doc_entry",
  T0."DocNum" AS "doc_num",
  T0."FolioNum" AS "folio",
  CASE WHEN UPPER(T0."FolioPref") IN ('34', 'FAE') THEN 34 ELSE 33 END AS "sii_document_type",
  T0."DocDate" AS "document_date",
  T0."CardCode" AS "customer_code",
  COALESCE(C0."CardName", T0."CardName") AS "customer_name",
  T0."LicTradNum" AS "customer_tax_id",
  T2."StreetS" AS "destination_address",
  T2."CountyS" AS "destination_commune",
  T0."NumAtCard" AS "customer_po",
  (SELECT MIN(D1."BaseEntry")
     FROM INV1 L1
     INNER JOIN DLN1 D1 ON D1."DocEntry" = L1."BaseEntry" AND D1."LineNum" = L1."BaseLine"
     WHERE L1."DocEntry" = T0."DocEntry" AND D1."BaseType" = 17) AS "base_order_entry",
  (SELECT SUM(L2."Quantity") FROM INV1 L2 WHERE L2."DocEntry" = T0."DocEntry") AS "units",
  (SELECT COUNT(DISTINCT D3."U_NX_LPN")
     FROM INV1 L3
     INNER JOIN DLN1 D2 ON D2."DocEntry" = L3."BaseEntry" AND D2."LineNum" = L3."BaseLine"
     INNER JOIN ODLN D3 ON D3."DocEntry" = D2."DocEntry"
     WHERE L3."DocEntry" = T0."DocEntry") AS "packages",
  T0."DocTotal" AS "total_amount",
  CASE WHEN T0."CANCELED" = 'Y' THEN 1 ELSE 0 END AS "is_canceled"
FROM OINV T0
LEFT JOIN OCRD C0 ON C0."CardCode" = T0."CardCode"
LEFT JOIN INV12 T2 ON T2."DocEntry" = T0."DocEntry"
WHERE T0."FolioNum" = :folio
  AND (:sii_document_type = 0
       OR CASE WHEN UPPER(T0."FolioPref") IN ('34', 'FAE') THEN 34 ELSE 33 END = :sii_document_type)
