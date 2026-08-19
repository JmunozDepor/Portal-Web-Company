-- Consolida las dos tablas de correlación Sucursal->Canal en una sola (Maestro_Sucursal).
-- Antes: vw_VentaBaseDistribucion (motor automático) leía de Maestro_Sucursal_Canal, y la pantalla
-- de asignación manual leía de Maestro_Sucursal — mismo contenido, dos tablas. Se verificó que ambas
-- tienen exactamente los mismos 82 códigos y los mismos nombres antes de unificar.

-- 1) Maestro_Sucursal necesita la columna Activo para poder reemplazar a Maestro_Sucursal_Canal en el JOIN.
ALTER TABLE dbo.Maestro_Sucursal
    ADD Activo BIT NOT NULL CONSTRAINT DF_Maestro_Sucursal_Activo DEFAULT (1);
GO

-- 2) La vista pasa a usar Maestro_Sucursal en vez de Maestro_Sucursal_Canal (misma salida: AnioMes,
--    TipoBase, CodDimension, NombreDimension, MontoVenta, PorcentajeParticipacion — no cambia para
--    quien ya consume la vista, como sp_EjecutarDistribucionAutomatica).
CREATE OR ALTER VIEW dbo.vw_VentaBaseDistribucion AS
WITH VentaConCanalCorregido AS (
    SELECT
        s.AnioMes,
        ISNULL(s.CodSucursal, 'SIN_ASIGNAR') AS CodSucursal,
        ISNULL(m.Sucursal, s.Sucursal) AS NombreSucursal,
        -- El canal ya NO viene de s.CodCanal (digitado), sino del maestro, derivado de la sucursal
        ISNULL(m.CodCanal, s.CodCanal) AS CodCanalCorregido,  -- fallback al original si la sucursal no esta en el maestro
        ISNULL(m.Canal, s.Canal) AS NombreCanalCorregido,
        s.MontoNeto
    FROM dbo.Staging_CentralizacionContable s
    LEFT JOIN dbo.Maestro_Sucursal m
        ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
    WHERE s.TipoRegistro = 'RESUMEN'
)
SELECT AnioMes, 'CANAL' AS TipoBase, CodCanalCorregido AS CodDimension,
       MAX(NombreCanalCorregido) AS NombreDimension,
       ABS(SUM(MontoNeto)) AS MontoVenta,
       ABS(SUM(MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(MontoNeto))) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion
FROM VentaConCanalCorregido
GROUP BY AnioMes, CodCanalCorregido

UNION ALL

SELECT AnioMes, 'SUCURSAL' AS TipoBase, CodSucursal AS CodDimension,
       MAX(NombreSucursal) AS NombreDimension,
       ABS(SUM(MontoNeto)) AS MontoVenta,
       ABS(SUM(MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(MontoNeto))) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion
FROM VentaConCanalCorregido
GROUP BY AnioMes, CodSucursal;
GO

-- 3) Ya no hace falta la tabla vieja.
DROP TABLE IF EXISTS dbo.Maestro_Sucursal_Canal;
