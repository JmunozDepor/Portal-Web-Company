-- Proveedor de SAP por RUT (sin puntos, con guion y DV en mayúscula).
SELECT "CardCode" AS "card_code", "CardName" AS "card_name", "LicTradNum" AS "tax_id",
       CASE WHEN "frozenFor" = 'Y' THEN 0 ELSE 1 END AS "is_active"
FROM OCRD
WHERE "CardType" = 'S' AND UPPER(REPLACE(REPLACE("LicTradNum", '.', ''), ' ', '')) = :tax_id
