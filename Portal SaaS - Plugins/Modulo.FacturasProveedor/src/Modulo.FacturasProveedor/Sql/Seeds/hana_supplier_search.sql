-- Búsqueda de proveedores de SAP por RUT, código o nombre (máximo 20). El parámetro se usa una sola vez
-- (el servicio HANA enlaza por posición), por eso va en la CTE.
WITH P AS (SELECT UPPER(:text) AS "q" FROM DUMMY)
SELECT TOP 20 C."CardCode" AS "card_code", C."CardName" AS "card_name", C."LicTradNum" AS "tax_id",
       CASE WHEN C."frozenFor" = 'Y' THEN 0 ELSE 1 END AS "is_active"
FROM OCRD C, P
WHERE C."CardType" = 'S'
  AND (UPPER(C."CardName") LIKE '%' || P."q" || '%' OR UPPER(C."CardCode") LIKE '%' || P."q" || '%' OR C."LicTradNum" LIKE '%' || P."q" || '%')
ORDER BY C."CardName"
