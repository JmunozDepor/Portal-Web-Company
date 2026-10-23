-- Lineas de una factura de venta (INV1) para la guia de despacho Res. 154: una fila por linea, por DocEntry.
-- Sin esquema fijo: aplica el esquema actual de la conexion SAP de la compania.
-- weight_kg = cantidad x peso unitario de venta (OITM.SWeight1) convertido a kg con la unidad OWGT
--   [validar con el administrador SAP: unidad y campo de peso de cada SAP]; NULL si el articulo no tiene peso.
-- customer_activity = giro del cliente (GiroRecep, max. 40), el primero con dato de:
--   OCRD."U_U_NX_giro" (clientes mayoristas internos), "U_GSP_SubGroupName" (clientes creados por el punto de
--   venta, sin su codigo "10001-") y, al final, "Notes". Se repite en cada fila. Campos de Depor: ajustar por compania.
-- destination_city = ciudad de la direccion de despacho (INV12."CityS"); se repite en cada fila.
-- model_code (opcional) = codigo del modelo sin talla (OITM."U_GSP_REFERENCE" en Depor): con "Lineas de la guia =
--   por modelo" las tallas de un modelo van en una sola linea. model_name (opcional) = nombre del modelo; si no
--   viene se usa el nombre del primer articulo del modelo.
SELECT
  L."LineNum" AS "line_num",
  L."ItemCode" AS "item_code",
  L."Dscription" AS "item_name",
  L."Quantity" AS "quantity",
  L."unitMsr" AS "unit",
  CASE WHEN I."SWeight1" > 0 AND W."WightInMG" > 0
       THEN L."Quantity" * I."SWeight1" * W."WightInMG" / 1000000
       ELSE NULL END AS "weight_kg",
  LEFT(COALESCE(
    NULLIF(TRIM(C0."U_U_NX_giro"), ''),
    NULLIF(TRIM(SUBSTR_AFTER(C0."U_GSP_SubGroupName", '-')), ''),
    NULLIF(TRIM(C0."U_GSP_SubGroupName"), ''),
    NULLIF(TRIM(C0."Notes"), '')), 40) AS "customer_activity",
  T2."CityS" AS "destination_city",
  I."U_GSP_REFERENCE" AS "model_code"
FROM INV1 L
INNER JOIN OINV T0 ON T0."DocEntry" = L."DocEntry"
LEFT JOIN OITM I ON I."ItemCode" = L."ItemCode"
LEFT JOIN OWGT W ON W."UnitCode" = I."SWght1Unit"
LEFT JOIN OCRD C0 ON C0."CardCode" = T0."CardCode"
LEFT JOIN INV12 T2 ON T2."DocEntry" = T0."DocEntry"
WHERE L."DocEntry" = :doc_entry
ORDER BY L."LineNum"
