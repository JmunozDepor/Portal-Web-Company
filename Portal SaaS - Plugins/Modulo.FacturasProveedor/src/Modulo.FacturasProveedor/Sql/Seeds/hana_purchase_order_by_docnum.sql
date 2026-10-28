-- OC de SAP por DocNum (puede devolver varias si el número se repite en otra serie).
SELECT T0."DocEntry" AS "doc_entry", T0."DocNum" AS "doc_num", COALESCE(S."SeriesName", CAST(T0."Series" AS NVARCHAR(20))) AS "series",
       T0."CardCode" AS "card_code", C."LicTradNum" AS "supplier_tax_id", T0."DocDate" AS "doc_date",
       T0."DocTotal" AS "total_local", T0."DocCur" AS "currency",
       CASE WHEN T0."CANCELED" = 'Y' THEN 'canceled' WHEN T0."DocStatus" = 'C' THEN 'closed' ELSE 'open' END AS "status"
FROM OPOR T0
LEFT JOIN NNM1 S ON S."Series" = T0."Series"
LEFT JOIN OCRD C ON C."CardCode" = T0."CardCode"
WHERE T0."DocNum" = :doc_num
