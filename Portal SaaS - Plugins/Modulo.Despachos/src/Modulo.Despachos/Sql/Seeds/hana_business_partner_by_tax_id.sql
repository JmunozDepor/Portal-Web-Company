-- Socio de negocio (cliente) por RUT con sus direcciones de despacho (receptor de la guía manual).
-- El portal envía el RUT normalizado (sin puntos, con guion). Una fila por dirección de despacho.
-- Giro (GiroRecep, max. 40) con la misma regla que las lineas de factura, el primero con dato de
--   U_U_NX_giro (clientes mayoristas internos), U_GSP_SubGroupName (clientes del punto de venta, sin su codigo "10001-")
--   y al final Notes. Campos de Depor, ajustar por compania.
SELECT
    T0."CardCode"  AS "card_code",
    T0."CardName"  AS "card_name",
    LEFT(COALESCE(
        NULLIF(TRIM(T0."U_U_NX_giro"), ''),
        NULLIF(TRIM(SUBSTR_AFTER(T0."U_GSP_SubGroupName", '-')), ''),
        NULLIF(TRIM(T0."U_GSP_SubGroupName"), ''),
        NULLIF(TRIM(T0."Notes"), '')), 40) AS "activity",
    CASE WHEN T0."validFor" = 'N' OR T0."frozenFor" = 'Y' THEN 0 ELSE 1 END AS "is_active",
    T1."Address"   AS "address_name",
    T1."Street"    AS "address",
    T1."County"    AS "commune",
    T1."City"      AS "city"
FROM OCRD T0
LEFT JOIN CRD1 T1 ON T1."CardCode" = T0."CardCode" AND T1."AdresType" = 'S'
WHERE T0."CardType" = 'C'
  AND REPLACE(T0."LicTradNum", '.', '') = :tax_id
ORDER BY T0."CardCode", T1."Address"
