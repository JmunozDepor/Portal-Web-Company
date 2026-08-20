-- Agrega 4 bases de distribución nuevas, una por marca: 'VENTA_SUCURSAL_CONVERSE',
-- 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM'. A diferencia de
-- 'SUCURSAL_TPR_ECM'/'SUCURSAL_TPR_SOLO_ECOM' (que restringen a un subconjunto de
-- sucursales/canales), estas 4 participan con TODAS las sucursales -- lo que cambia
-- es que el % de participación de cada sucursal se calcula sobre la venta de UNA
-- cuenta contable puntual (la marca), no sobre la venta total. Por eso cada rama
-- nueva parte de Staging_CentralizacionContable directo con su propio filtro
-- NroCuenta, en vez de partir del CTE VentaConCanalCorregido (que ya viene agregado
-- sobre toda la venta del mes, sin filtro de cuenta). Si una sucursal no tuvo venta
-- de esa cuenta en el mes, no aparece como fila en esa rama -- una regla con esa
-- base no le asigna nada a esa sucursal ese mes (mismo comportamiento que ya tienen
-- SUCURSAL_TPR_ECM/SUCURSAL_TPR_SOLO_ECOM para sucursales fuera del subconjunto).
-- No toca las ramas CANAL/SUCURSAL/SUCURSAL_TPR_ECM/SUCURSAL_TPR_SOLO_ECOM existentes.
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
GROUP BY AnioMes, CodSucursal

UNION ALL

SELECT AnioMes, 'SUCURSAL_TPR_ECM' AS TipoBase, CodSucursal AS CodDimension,
       MAX(NombreSucursal) AS NombreDimension,
       ABS(SUM(MontoNeto)) AS MontoVenta,
       ABS(SUM(MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(MontoNeto))) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion
FROM VentaConCanalCorregido
WHERE CodCanalCorregido IN ('TPR', 'ECM')
GROUP BY AnioMes, CodSucursal

UNION ALL

SELECT AnioMes, 'SUCURSAL_TPR_SOLO_ECOM' AS TipoBase, CodSucursal AS CodDimension,
       MAX(NombreSucursal) AS NombreDimension,
       ABS(SUM(MontoNeto)) AS MontoVenta,
       ABS(SUM(MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(MontoNeto))) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion
FROM VentaConCanalCorregido
WHERE CodCanalCorregido = 'TPR'
   OR (CodCanalCorregido = 'ECM' AND CodSucursal LIKE '070%')
GROUP BY AnioMes, CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_CONVERSE' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-04'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_UMBRO' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-05'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_FILA' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-06'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_SM' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-09'
GROUP BY s.AnioMes, s.CodSucursal;
