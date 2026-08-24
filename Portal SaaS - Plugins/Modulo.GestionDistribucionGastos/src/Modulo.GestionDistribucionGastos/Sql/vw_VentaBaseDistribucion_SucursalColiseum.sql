-- Agrega la base 'SUCURSAL_COLISEUM': reparte SOLO entre las 10 tiendas Coliseum
-- (CodSucursal 0201-0210, ver Sql/Maestro_Sucursal.sql), en proporción a su venta
-- total del mes -- mismo criterio que 'SUCURSAL_TPR_ECM'/'SUCURSAL_TPR_SOLO_ECOM'
-- (agrega sobre VentaConCanalCorregido, venta total, no una cuenta puntual), NO el de
-- las 4 bases VENTA_SUCURSAL_* por marca (esas filtran por NroCuenta puntual).
-- Nota: existen además outlets 'OUTLET COLISEUM ...' con CodSucursal 06xx (0602, 0605,
-- 0607, 0609) que NO forman parte de esta base -- se filtra por la lista explícita de
-- códigos 0201-0210, no por LIKE '%COLISEUM%' sobre el nombre, para excluirlos a propósito.
-- Único cambio respecto a la versión anterior (vw_VentaBaseDistribucion_FixSignoMarcas.sql):
-- se agrega esta rama nueva vía UNION ALL: las 8 ramas anteriores quedan sin cambios.
CREATE OR ALTER VIEW dbo.vw_VentaBaseDistribucion AS
WITH VentaConCanalCorregido AS (
    SELECT
        s.AnioMes,
        ISNULL(s.CodSucursal, 'SIN_ASIGNAR') AS CodSucursal,
        ISNULL(m.Sucursal, s.Sucursal) AS NombreSucursal,
        ISNULL(m.CodCanal, s.CodCanal) AS CodCanalCorregido,
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

SELECT AnioMes, 'SUCURSAL_COLISEUM' AS TipoBase, CodSucursal AS CodDimension,
       MAX(NombreSucursal) AS NombreDimension,
       ABS(SUM(MontoNeto)) AS MontoVenta,
       ABS(SUM(MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(MontoNeto))) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion
FROM VentaConCanalCorregido
WHERE CodSucursal IN ('0201','0202','0203','0204','0205','0206','0207','0208','0209','0210')
GROUP BY AnioMes, CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_CONVERSE' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-04' AND s.CodSucursal <> 'IND4'
GROUP BY s.AnioMes, s.CodSucursal
HAVING SUM(s.MontoNeto) < 0

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_UMBRO' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-05' AND s.CodSucursal <> 'IND4'
GROUP BY s.AnioMes, s.CodSucursal
HAVING SUM(s.MontoNeto) < 0

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_FILA' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-06' AND s.CodSucursal <> 'IND4'
GROUP BY s.AnioMes, s.CodSucursal
HAVING SUM(s.MontoNeto) < 0

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_SM' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-09' AND s.CodSucursal <> 'IND4'
GROUP BY s.AnioMes, s.CodSucursal
HAVING SUM(s.MontoNeto) < 0;
