-- Diagnostico: confirmar el tamano real de la columna BaseDistribucion antes de ampliarla.
-- Sospecha: quedo dimensionada para 'SUCURSAL_TPR_ECM' (16 caracteres) y no alcanza para
-- 'SUCURSAL_TPR_SOLO_ECOM' (22 caracteres), lo que tira "String or binary data would be
-- truncated" al guardar una regla nueva con ese valor.
SELECT
    c.name AS Columna,
    t.name AS TipoDato,
    c.max_length AS LargoMaximoEnBytes,  -- NVARCHAR: bytes / 2 = caracteres; VARCHAR: bytes = caracteres
    c.is_nullable AS PermiteNulo
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Reglas_Distribucion')
  AND c.name = 'BaseDistribucion';
