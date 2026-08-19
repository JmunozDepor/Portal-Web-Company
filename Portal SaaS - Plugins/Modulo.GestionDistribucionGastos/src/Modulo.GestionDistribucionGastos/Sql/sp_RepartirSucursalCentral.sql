-- Acción global, A DEMANDA (nunca la corre sola sp_EjecutarDistribucionAutomatica): reparte por
-- venta cualquier fila de Distribucion_Final que quedó con Sucursal='IND4' (Central) dentro de un
-- canal YA resuelto (CodCanalDestino real, distinto de IND3/vacío) -- sin importar la cuenta, y sin
-- necesidad de crear una fila en Reglas_Distribucion por cuenta. Es la única acción del módulo que
-- toca deliberadamente filas TipoOrigen='MANUAL' (además de 'SIN_AJUSTE') -- excepción a propósito
-- a la regla general de "el motor nunca pisa lo manual", acotada estrictamente a este caso (saldo
-- ya asignado a un canal real pero sin sucursal específica), y solo cuando un admin la dispara
-- explícitamente desde la UI, nunca como parte del flujo automático normal.
-- Respeta los mismos candados que el resto del módulo: no toca Cierre_Mes.Estado='CERRADO' ni
-- Cuenta_Aprobada. Deja como MANUAL (no AUTO) las filas resultantes, para que ninguna regla futura
-- las vuelva a tocar sin que alguien lo pida.
-- Filas cuyo canal no tiene venta real ese mes (ej. canal sin datos en Staging RESUMEN) se dejan
-- SIN TOCAR -- no hay base de participación con la cual repartir, se reportan aparte en el
-- resultado (FilasSinVentaEnSuCanal) para revisión manual.
-- ReglaAplicada queda con un sufijo "- origen MANUAL" / "- origen SIN_AJUSTE" según de dónde venía
-- cada fila ANTES de repartirla -- es lo que permite a sp_DeshacerRepartoSucursalCentral saber a
-- qué TipoOrigen/Estado volver si se deshace (ver Sql/sp_DeshacerRepartoSucursalCentral.sql).
CREATE OR ALTER PROCEDURE dbo.sp_RepartirSucursalCentral
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Candidatas: MANUAL o SIN_AJUSTE con Sucursal=IND4 dentro de un canal ya resuelto,
        -- en meses abiertos y cuentas no aprobadas.
        SELECT d.Id, d.AnioMes, d.CodCanalDestino, d.MontoDistribuido, d.TipoOrigen
        INTO #Candidatas
        FROM dbo.Distribucion_Final d
        LEFT JOIN dbo.Cierre_Mes cm ON cm.AnioMes = d.AnioMes
        LEFT JOIN dbo.Cuenta_Aprobada ca ON ca.AnioMes = d.AnioMes AND ca.NroCuenta = d.NroCuenta
        WHERE d.CodSucursalDestino = 'IND4'
          AND d.TipoOrigen IN ('MANUAL', 'SIN_AJUSTE')
          AND ISNULL(d.CodCanalDestino, '') NOT IN ('', 'IND3')
          AND ISNULL(cm.Estado, 'ABIERTO') <> 'CERRADO'
          AND ca.NroCuenta IS NULL;

        -- Base de venta real por AnioMes+Canal+Sucursal (excluye IND4, mismo criterio que
        -- vw_VentaBaseDistribucion_ExcluirCentral pero generalizado a cualquier canal, no solo TPR).
        SELECT
            s.AnioMes,
            ISNULL(m.CodCanal, s.CodCanal) AS CodCanal,
            s.CodSucursal,
            MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreSucursal,
            ABS(SUM(s.MontoNeto)) AS MontoVenta
        INTO #VentaBase
        FROM dbo.Staging_CentralizacionContable s
        LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
        WHERE s.TipoRegistro = 'RESUMEN' AND s.CodSucursal <> 'IND4'
        GROUP BY s.AnioMes, ISNULL(m.CodCanal, s.CodCanal), s.CodSucursal;

        -- Candidatas que sí tienen venta real en su canal/mes -- las únicas que se reparten.
        SELECT c.*
        INTO #ConVenta
        FROM #Candidatas c
        WHERE EXISTS (SELECT 1 FROM #VentaBase v WHERE v.AnioMes = c.AnioMes AND v.CodCanal = c.CodCanalDestino);

        IF EXISTS (SELECT 1 FROM #ConVenta)
        BEGIN
            ;WITH VentaPorCanal AS (
                SELECT
                    AnioMes, CodCanal, CodSucursal, NombreSucursal,
                    MontoVenta * 1.0 / NULLIF(SUM(MontoVenta) OVER (PARTITION BY AnioMes, CodCanal), 0) AS PorcentajeParticipacion
                FROM #VentaBase
            ),
            -- Calcula todas las filas nuevas por cada fila a reemplazar, con ajuste de redondeo
            -- (mismo criterio que sp_EjecutarDistribucionAutomatica_AjusteRedondeo: la fila de mayor
            -- participación absorbe el resto exacto para que la suma cierre contra el monto original).
            NuevasFilas AS (
                SELECT
                    d.StagingId, d.NroAsiento, d.LineaId, d.AnioMes, d.Fecha,
                    d.NroCuenta, d.NombreCuenta,
                    d.CodCentroCostoOriginal, d.CentroCostoOriginal,
                    d.CodCanalOriginal, d.CanalOriginal,
                    d.CodSucursalOriginal, d.SucursalOriginal,
                    d.MontoOriginal,
                    ISNULL(msuc.CodCentroCosto, d.CodCentroCostoDestino) AS CodCentroCostoDestino,
                    ISNULL(msuc.CentroCosto, d.CentroCostoDestino) AS CentroCostoDestino,
                    d.CodCanalDestino, d.CanalDestino,
                    v.CodSucursal AS CodSucursalDestino, v.NombreSucursal AS SucursalDestino,
                    ROUND(d.MontoDistribuido * v.PorcentajeParticipacion, 2) AS MontoRedondeado,
                    d.MontoDistribuido AS MontoAReemplazar,
                    f.TipoOrigen AS TipoOrigenPrevio,
                    ROW_NUMBER() OVER (PARTITION BY d.Id ORDER BY v.PorcentajeParticipacion DESC, v.CodSucursal) AS Orden,
                    SUM(ROUND(d.MontoDistribuido * v.PorcentajeParticipacion, 2)) OVER (PARTITION BY d.Id) AS TotalRedondeado
                FROM dbo.Distribucion_Final d
                INNER JOIN #ConVenta f ON f.Id = d.Id
                INNER JOIN VentaPorCanal v ON v.AnioMes = d.AnioMes AND v.CodCanal = d.CodCanalDestino
                LEFT JOIN dbo.Maestro_Sucursal msuc ON msuc.CodSucursal = v.CodSucursal AND msuc.Activo = 1
            )
            INSERT INTO dbo.Distribucion_Final
                (StagingId, NroAsiento, LineaId, AnioMes, Fecha,
                 NroCuenta, NombreCuenta,
                 CodCentroCostoOriginal, CentroCostoOriginal,
                 CodCanalOriginal, CanalOriginal,
                 CodSucursalOriginal, SucursalOriginal,
                 MontoOriginal,
                 CodCentroCostoDestino, CentroCostoDestino,
                 CodCanalDestino, CanalDestino,
                 CodSucursalDestino, SucursalDestino,
                 MontoDistribuido,
                 TipoOrigen, ReglaAplicada, Estado)
            SELECT
                StagingId, NroAsiento, LineaId, AnioMes, Fecha,
                NroCuenta, NombreCuenta,
                CodCentroCostoOriginal, CentroCostoOriginal,
                CodCanalOriginal, CanalOriginal,
                CodSucursalOriginal, SucursalOriginal,
                MontoOriginal,
                CodCentroCostoDestino, CentroCostoDestino,
                CodCanalDestino, CanalDestino,
                CodSucursalDestino, SucursalDestino,
                CASE WHEN Orden = 1 THEN MontoRedondeado + (MontoAReemplazar - TotalRedondeado) ELSE MontoRedondeado END,
                'MANUAL',
                CASE WHEN TipoOrigenPrevio = 'MANUAL'
                     THEN 'Reparto Sucursal Central (IND4) - origen MANUAL'
                     ELSE 'Reparto Sucursal Central (IND4) - origen SIN_AJUSTE'
                END,
                'APROBADO'
            FROM NuevasFilas;

            DELETE FROM dbo.Distribucion_Final WHERE Id IN (SELECT Id FROM #ConVenta);
        END

        COMMIT TRANSACTION;

        SELECT
            (SELECT COUNT(*) FROM #ConVenta) AS FilasRepartidas,
            (SELECT COUNT(*) FROM #Candidatas) - (SELECT COUNT(*) FROM #ConVenta) AS FilasSinVentaEnSuCanal;

        DROP TABLE #Candidatas;
        DROP TABLE #VentaBase;
        DROP TABLE #ConVenta;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF OBJECT_ID('tempdb..#Candidatas') IS NOT NULL DROP TABLE #Candidatas;
        IF OBJECT_ID('tempdb..#VentaBase') IS NOT NULL DROP TABLE #VentaBase;
        IF OBJECT_ID('tempdb..#ConVenta') IS NOT NULL DROP TABLE #ConVenta;
        THROW;
    END CATCH
END
