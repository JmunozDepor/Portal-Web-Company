ALTER VIEW dbo.vw_VentaBaseDistribucion AS

-- 1. UNIVERSO GLOBAL DE VENTAS REGLA DE NEGOCIO
-- Solo cuentas 4%, registros 'RESUMEN' y canales de venta válidos
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
        ON m.CodSucursal = s.CodSucursal
       AND m.Activo = 1
    WHERE s.TipoRegistro = 'RESUMEN'
      AND s.NroCuenta LIKE '4%'
      AND ISNULL(m.CodCanal, s.CodCanal) IN ('TPR', 'ECM', 'MAY', 'REG')
),

-- 2. Pre-agregado por CANAL
BaseCanal AS (
    SELECT
        AnioMes,
        CodCanalCorregido AS CodDimension,
        MAX(NombreCanalCorregido) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    GROUP BY
        AnioMes,
        CodCanalCorregido
),

-- 3. Pre-agregado por SUCURSAL (Todas las sucursales del universo de venta)
BaseSucursal AS (
    SELECT
        AnioMes,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    GROUP BY
        AnioMes,
        CodSucursal
),

-- 4. Pre-agregado SUCURSAL TPR_ECM
BaseTPR_ECM AS (
    SELECT
        AnioMes,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    WHERE CodCanalCorregido IN ('TPR', 'ECM')
    GROUP BY
        AnioMes,
        CodSucursal
),

-- 5. Pre-agregado SUCURSAL TPR_SOLO_ECOM
BaseTPR_SOLO_ECOM AS (
    SELECT
        AnioMes,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    WHERE CodCanalCorregido = 'TPR'
       OR (CodCanalCorregido = 'ECM' AND CodSucursal LIKE '070%')
    GROUP BY
        AnioMes,
        CodSucursal
),

-- 6. Pre-agregado SUCURSAL COLISEUM
BaseColiseum AS (
    SELECT
        AnioMes,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    WHERE CodSucursal IN ('0201', '0202', '0203', '0204', '0205', '0206', '0207', '0208', '0209', '0210')
    GROUP BY
        AnioMes,
        CodSucursal
),

-- 6b. Pre-agregado SUCURSAL COLISEUM POR CANAL -- análogo a BaseSucursalPorCanal, pero acotado a
--     las 10 tiendas Coliseum: una cuenta ligada a Coliseum que quedó en Sucursal Central debe
--     repartirse SOLO entre esas 10 sucursales (ponderado por su venta dentro de cada canal), nunca
--     mezclado con la venta de otras tiendas. En la práctica las 10 tiendas Coliseum son todas canal
--     TPR (ver Maestro_Sucursal), así que hoy esto resuelve a un solo grupo de canal -- se deja
--     genérico por canal de todos modos, por si algún día se suma una tienda Coliseum de otro canal.
BaseColiseumPorCanal AS (
    SELECT
        AnioMes,
        CodCanalCorregido AS CodCanalOrigen,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    WHERE CodSucursal IN ('0201', '0202', '0203', '0204', '0205', '0206', '0207', '0208', '0209', '0210')
    GROUP BY
        AnioMes,
        CodCanalCorregido,
        CodSucursal
),

-- 7. Pre-agregado SUCURSAL POR CANAL -- para redistribuir lo que quedó asignado a
--    Sucursal Central (IND4): a diferencia de las bases anteriores (un solo set de pesos
--    para todo el mes), acá el peso de cada sucursal se calcula SEPARADO POR CANAL
--    (CodCanalOrigen), porque una línea que cayó en IND4 con canal TPR solo debe repartirse
--    entre sucursales reales de canal TPR (ponderado por su venta dentro de TPR), nunca
--    mezclado con el peso de sucursales de otro canal. Se excluye la propia IND4 como
--    destino (no tiene sentido que Central se reparta a sí misma).
BaseSucursalPorCanal AS (
    SELECT
        AnioMes,
        CodCanalCorregido AS CodCanalOrigen,
        CodSucursal AS CodDimension,
        MAX(NombreSucursal) AS NombreDimension,
        ABS(SUM(MontoNeto)) AS MontoVenta
    FROM VentaConCanalCorregido
    WHERE CodSucursal <> 'IND4'
    GROUP BY
        AnioMes,
        CodCanalCorregido,
        CodSucursal
),

-- 8. Pre-agregados por Cuentas / Marcas Específicas
BaseCuentasEspeciales AS (
    SELECT
        s.AnioMes,
        CASE s.NroCuenta
            WHEN '41-01-001-04' THEN 'VENTA_SUCURSAL_CONVERSE'
            WHEN '41-01-001-05' THEN 'VENTA_SUCURSAL_UMBRO'
            WHEN '41-01-001-06' THEN 'VENTA_SUCURSAL_FILA'
            WHEN '41-01-001-09' THEN 'VENTA_SUCURSAL_SM'
        END AS TipoBase,
        s.CodSucursal AS CodDimension,
        MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
        ABS(SUM(s.MontoNeto)) AS MontoVenta
    FROM dbo.Staging_CentralizacionContable s
    LEFT JOIN dbo.Maestro_Sucursal m
        ON m.CodSucursal = s.CodSucursal
       AND m.Activo = 1
    WHERE s.TipoRegistro = 'RESUMEN'
      AND s.NroCuenta IN ('41-01-001-04', '41-01-001-05', '41-01-001-06', '41-01-001-09')
      AND s.CodSucursal <> 'IND4'
      AND ISNULL(m.CodCanal, s.CodCanal) IN ('TPR', 'ECM', 'MAY', 'REG')
    GROUP BY
        s.AnioMes,
        s.NroCuenta,
        s.CodSucursal
    HAVING SUM(s.MontoNeto) < 0
),

-- 9. Pre-agregados por Cuentas/Marcas Específicas, PARA REDISTRIBUIR SU RESIDUO EN IND4 POR CANAL
--    -- análogo a BaseSucursalPorCanal, pero el peso es la venta de ESA marca puntual, no venta
--    total: una cuenta ligada a una marca (ej. un costo asociado a SM) que quedó en Sucursal
--    Central debe repartirse SOLO entre las sucursales que efectivamente venden esa marca en cada
--    canal, nunca proporcional a la venta total del canal (eso sería mezclar el costo de una marca
--    con la venta de otra). Mismo criterio de exclusión que BaseCuentasEspeciales (sin IND4, canales
--    válidos, HAVING < 0 para no contar sucursales con neto invertido), solo que agrupado también
--    por canal.
BaseCuentasEspecialesPorCanal AS (
    SELECT
        s.AnioMes,
        CASE s.NroCuenta
            WHEN '41-01-001-04' THEN 'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL'
            WHEN '41-01-001-05' THEN 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL'
            WHEN '41-01-001-06' THEN 'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL'
            WHEN '41-01-001-09' THEN 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL'
        END AS TipoBase,
        ISNULL(m.CodCanal, s.CodCanal) AS CodCanalOrigen,
        s.CodSucursal AS CodDimension,
        MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
        ABS(SUM(s.MontoNeto)) AS MontoVenta
    FROM dbo.Staging_CentralizacionContable s
    LEFT JOIN dbo.Maestro_Sucursal m
        ON m.CodSucursal = s.CodSucursal
       AND m.Activo = 1
    WHERE s.TipoRegistro = 'RESUMEN'
      AND s.NroCuenta IN ('41-01-001-04', '41-01-001-05', '41-01-001-06', '41-01-001-09')
      AND s.CodSucursal <> 'IND4'
      AND ISNULL(m.CodCanal, s.CodCanal) IN ('TPR', 'ECM', 'MAY', 'REG')
    GROUP BY
        s.AnioMes,
        s.NroCuenta,
        ISNULL(m.CodCanal, s.CodCanal),
        s.CodSucursal
    HAVING SUM(s.MontoNeto) < 0
)

-- UNION FINAL CON PORCENTAJE PONDERADO POR SU PROPIO SUB-TOTAL
-- CodCanalOrigen lleva el canal real solo en la base SUCURSAL_DESDE_IND4_POR_CANAL (para que el SP
-- una cada línea de IND4 con las sucursales de su mismo canal de origen); en las demás bases va con
-- el valor constante 'TODOS' -- a propósito, en vez de NULL, para que el join en el SP siga siendo
-- una igualdad simple (equi-join, ver comentario en sp_EjecutarDistribucionAutomatica) y no fuerce
-- un Nested Loop que reevalúe esta vista (varios GROUP BY/OVER sobre toda Staging_CentralizacionContable)
-- una vez por cada línea a distribuir -- eso fue justamente lo que colgó la primera versión de este cambio.
SELECT
    AnioMes,
    'CANAL' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseCanal

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseSucursal

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL_TPR_ECM' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseTPR_ECM

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL_TPR_SOLO_ECOM' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseTPR_SOLO_ECOM

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL_COLISEUM' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseColiseum

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes, CodCanalOrigen), 0) AS PorcentajeParticipacion,
    CodCanalOrigen
FROM BaseColiseumPorCanal

UNION ALL

SELECT
    AnioMes,
    'SUCURSAL_DESDE_IND4_POR_CANAL' AS TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes, CodCanalOrigen), 0) AS PorcentajeParticipacion,
    CodCanalOrigen
FROM BaseSucursalPorCanal

UNION ALL

SELECT
    AnioMes,
    TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes, TipoBase), 0) AS PorcentajeParticipacion,
    CAST('TODOS' AS VARCHAR(10)) AS CodCanalOrigen
FROM BaseCuentasEspeciales

UNION ALL

SELECT
    AnioMes,
    TipoBase,
    CodDimension,
    NombreDimension,
    MontoVenta,
    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes, TipoBase, CodCanalOrigen), 0) AS PorcentajeParticipacion,
    CodCanalOrigen
FROM BaseCuentasEspecialesPorCanal;
