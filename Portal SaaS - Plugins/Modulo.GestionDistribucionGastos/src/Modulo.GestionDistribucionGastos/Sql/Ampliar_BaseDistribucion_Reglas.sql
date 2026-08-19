-- Amplia Reglas_Distribucion.BaseDistribucion de VARCHAR(20) a VARCHAR(50) -- confirmado contra
-- el ambiente real (sys.columns: BaseDistribucion varchar(20) NOT NULL) que el valor nuevo
-- 'SUCURSAL_TPR_SOLO_ECOM' (22 caracteres) no entraba en 20, tirando "String or binary data
-- would be truncated" al guardar una regla con esa base. 50 deja margen para nombres de base
-- de distribucion futuros sin tener que volver a tocar el esquema por este mismo motivo.
ALTER TABLE dbo.Reglas_Distribucion
    ALTER COLUMN BaseDistribucion VARCHAR(50) NOT NULL;
