-- Bodegas de SAP (OWHS) para elegir las bodegas sucursal (Fase B). Sin parametros.
SELECT
  W."WhsCode" AS "warehouse_code",
  W."WhsName" AS "warehouse_name",
  W."Street" AS "address",
  W."County" AS "commune"
FROM OWHS W
WHERE W."Inactive" = 'N'
ORDER BY W."WhsCode"
