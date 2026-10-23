-- Lineas de una factura de venta (INV1) para la guia de despacho Res. 154: una fila por linea, por DocEntry.
-- Sin esquema fijo: aplica el esquema actual de la conexion SAP de la compania.
-- weight_kg = cantidad x peso unitario de venta (OITM.SWeight1) convertido a kg con la unidad OWGT
--   [validar con el administrador SAP: unidad y campo de peso de cada SAP]; NULL si el articulo no tiene peso.
-- customer_activity = giro del cliente: en muchos SAP chilenos se guarda en OCRD."Notes" [validar]; se repite en cada fila.
-- destination_city = ciudad de la direccion de despacho (INV12."CityS"); se repite en cada fila.
SELECT
  L."LineNum" AS "line_num",
  L."ItemCode" AS "item_code",
  L."Dscription" AS "item_name",
  L."Quantity" AS "quantity",
  L."unitMsr" AS "unit",
  CASE WHEN I."SWeight1" > 0 AND W."WightInMG" > 0
       THEN L."Quantity" * I."SWeight1" * W."WightInMG" / 1000000
       ELSE NULL END AS "weight_kg",
  C0."Notes" AS "customer_activity",
  T2."CityS" AS "destination_city"
FROM INV1 L
INNER JOIN OINV T0 ON T0."DocEntry" = L."DocEntry"
LEFT JOIN OITM I ON I."ItemCode" = L."ItemCode"
LEFT JOIN OWGT W ON W."UnitCode" = I."SWght1Unit"
LEFT JOIN OCRD C0 ON C0."CardCode" = T0."CardCode"
LEFT JOIN INV12 T2 ON T2."DocEntry" = T0."DocEntry"
WHERE L."DocEntry" = :doc_entry
ORDER BY L."LineNum"
