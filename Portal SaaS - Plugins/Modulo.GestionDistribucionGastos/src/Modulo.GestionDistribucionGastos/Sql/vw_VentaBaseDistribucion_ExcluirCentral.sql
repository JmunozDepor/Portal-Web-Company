-- Excluye del cálculo de participación (y por lo tanto del reparto) la venta que cae en
-- Canal='IND3' o Sucursal='IND4' -- ambos son marcadores de "centralizado/sin resolver", no
-- destinos reales: IND3 es el canal crudo que trae SAP cuando la sucursal no está en
-- Maestro_Sucursal (ver comentario en Eerr/Index.cshtml.cs) e IND4 es la fila "Central" del
-- maestro (CodCentroCosto '9999', ver Sql/Maestro_Sucursal.sql). Antes esos dos valores competían
-- por porcentaje igual que cualquier sucursal/canal real, así que una parte del gasto distribuido
-- terminaba asignado ahí -- exactamente lo que se pidió evitar: todo lo centralizado debe repartirse
-- ENTRE las sucursales/canales reales, nunca quedarse en el propio marcador central.
-- Al sacarlos de VentaConCanalCorregido (el CTE base, antes de las 4 ramas de TipoBase), el
-- PorcentajeParticipacion se renormaliza solo entre los destinos reales en una sola pasada --
-- no hace falta una segunda distribución/cascada aparte. Único cambio respecto a la versión
-- anterior (Sql/vw_VentaBaseDistribucion_SucursalTprSoloEcom.sql): el WHERE nuevo en el CTE.
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
      AND ISNULL(s.CodSucursal, '') <> 'IND4'
      AND ISNULL(m.CodCanal, s.CodCanal) <> 'IND3'
      AND ISNULL(m.CodCanal, s.CodCanal) <> 'OTROS'
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
GROUP BY AnioMes, CodSucursal;