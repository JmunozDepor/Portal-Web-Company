-- Corrige un bug real en las 4 bases 'VENTA_SUCURSAL_CONVERSE'/'UMBRO'/'FILA'/'SM'
-- (introducidas en vw_VentaBaseDistribucion_VentaSucursalMarcas.sql): el ABS() se
-- aplicaba POR SUCURSAL, antes de sumar entre sucursales. Si una sucursal tenía el
-- neto de esa cuenta con signo invertido (devoluciones/notas de crédito superando la
-- venta del mes en esa sucursal puntual), su ABS() la convertía de "resta" a "suma"
-- -- inflaba el total de la vista respecto al reporte final (vw_EerrAnual/
-- EerrController, que suman el neto CON su signo real, sin ABS por sucursal) y le
-- daba a esa sucursal un PorcentajeParticipacion positivo cuando en realidad no
-- vendió nada neto de esa marca ese mes.
--
-- Decisión de negocio confirmada con el dueño del proyecto: una sucursal cuyo neto
-- de la cuenta queda invertido (devoluciones > venta) NO PARTICIPA ese mes de esa
-- base -- mismo criterio que ya aplica hoy una sucursal sin ninguna venta de esa
-- cuenta (no genera fila, la regla que use esa base simplemente no le asigna nada).
-- Se logra agregando `HAVING SUM(s.MontoNeto) < 0` -- el signo normal de una cuenta
-- de venta en la convención Débito-Crédito cruda de Staging_CentralizacionContable
-- es negativo (ver el comentario de vw_EerrAnual.sql: "Monto queda invertido...
-- Ingresos sale POSITIVO" via -MontoNeto, o sea el crudo es negativo para una venta
-- neta real). Una sucursal con SUM(MontoNeto) >= 0 para esa cuenta ese mes queda
-- fuera de la vista para esa base, igual que si no hubiera tenido venta.
--
-- Segundo cambio, pedido explícito del dueño del proyecto: las 4 ramas VENTA_SUCURSAL_*
-- nunca tuvieron la exclusión de Sucursal='IND4' (Central) que sí tienen CANAL/SUCURSAL/
-- SUCURSAL_TPR_ECM/SUCURSAL_TPR_SOLO_ECOM desde vw_VentaBaseDistribucion_ExcluirCentral.sql
-- -- porque estas 4 parten directo de Staging_CentralizacionContable en vez del CTE
-- VentaConCanalCorregido (que ya filtra IND4/IND3), ver el comentario del archivo que las
-- introdujo. Se agrega `AND s.CodSucursal <> 'IND4'` explícito en las 4 -- Central nunca debe
-- competir como destino de estas bases, mismo criterio que ya rige CANAL/SUCURSAL. Esto es
-- defensivo (evita que Central reciba % si alguna vez tuviera una fila de venta de marca por
-- un problema de datos) -- NO resuelve por sí solo el caso de un GASTO (ej. 61-09-001-01)
-- que quedó originalmente registrado en Sucursal Central: ese residuo requiere que la línea
-- efectivamente matchee la regla (Cuenta_Aprobada/Cierre_Mes abiertos, TipoOrigen='SIN_AJUSTE')
-- -- si matchea, se reparte 100% entre las sucursales reales sin importar dónde nació la línea,
-- porque la cascada usa CodDimension de la vista como destino, nunca conserva el original.
--
-- Solo toca las 4 ramas VENTA_SUCURSAL_* -- CANAL/SUCURSAL/SUCURSAL_TPR_ECM/
-- SUCURSAL_TPR_SOLO_ECOM (que agregan sobre TODA la venta del mes, no una cuenta
-- puntual) tienen el mismo patrón ABS()-por-grupo pero es mucho menos probable que
-- el neto total de una sucursal se invierta -- se dejan sin cambios a propósito,
-- fuera del alcance de este fix puntual reportado contra la cuenta 41-01-001-04.
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
