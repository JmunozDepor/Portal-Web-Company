-- Facturas de proveedor de SAP vinculadas a la OC (líneas copiadas de la OC: BaseType 22).
-- Hoy las cargadas por DataTransfer no quedan vinculadas: esta consulta suma lo que cree la Etapa 2.
SELECT H."LicTradNum" AS "supplier_tax_id", H."FolioNum" AS "folio", SUM(L."GTotal") AS "total_amount_local",
       H."DocEntry" AS "invoice_doc_entry"
FROM OPCH H
INNER JOIN PCH1 L ON L."DocEntry" = H."DocEntry" AND L."BaseType" = 22 AND L."BaseEntry" = :doc_entry
WHERE H."CANCELED" = 'N'
GROUP BY H."DocEntry", H."LicTradNum", H."FolioNum"
