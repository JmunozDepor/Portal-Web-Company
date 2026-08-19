-- Agrega la base de distribución 'SUCURSAL_TPR_SOLO_ECOM': igual que 'SUCURSAL_TPR_ECM' (Tiendas
-- Propias + Ecommerce), pero dentro de ECM excluye los marketplaces (Dafiti, Mercado Libre,
-- MKP Falabella, MKP Ripley -- codigos 0801-0807 en Maestro_Sucursal) y solo deja el ecommerce
-- propio de marca (Coliseum Store, Steve Madden, Converse, Fila, Umbro -- codigos 0701-0705,
-- todos con prefijo '070'). Para cuentas que deben repartirse solo entre tienda propia y
-- ecommerce propio, sin que los marketplaces (canal comercial distinto, no operado por Depor)
-- se lleven parte del gasto. No toca las ramas CANAL/SUCURSAL/SUCURSAL_TPR_ECM existentes.
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
GROUP BY AnioMes, CodSucursal;
