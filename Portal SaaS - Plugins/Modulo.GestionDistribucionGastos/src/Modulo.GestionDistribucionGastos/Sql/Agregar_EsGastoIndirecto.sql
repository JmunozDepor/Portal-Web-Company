-- Marca por línea si un gasto (grupos 6-9) es "indirecto" (no directo del canal) y por lo tanto debe
-- excluirse del Resultado Operacional 1 del EERR, quedando recién en el Resultado Operacional 2.
-- Por defecto todas las líneas quedan en Resultado Operacional 1 (EsGastoIndirecto = 0).
ALTER TABLE dbo.Distribucion_Final
    ADD EsGastoIndirecto BIT NOT NULL CONSTRAINT DF_Distribucion_Final_EsGastoIndirecto DEFAULT (0);
