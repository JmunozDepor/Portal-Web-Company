ALTER  PROCEDURE dbo.sp_EjecutarDistribucionAutomatica
    @AnioMes CHAR(6)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Generar filas espejo (SIN_AJUSTE) para todas las líneas del mes que aún no existan en Distribucion_Final
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
             TipoOrigen, Estado)
        SELECT
            s.Id, s.NroAsiento, s.LineaId, s.AnioMes, s.Fecha,
            s.NroCuenta, s.NombreCuenta,
            s.CodCentroCosto, s.CentroCosto,
            s.CodCanal, s.Canal,

            -- NORMALIZACIÓN DE SUCURSAL ORIGINAL:
            ISNULL(NULLIF(NULLIF(s.CodSucursal, 'N/A'), ''), 'IND4') AS CodSucursalOriginal,
            CASE
                WHEN ISNULL(NULLIF(NULLIF(s.CodSucursal, 'N/A'), ''), 'IND4') = 'IND4' THEN 'Central'
                ELSE s.Sucursal
            END AS SucursalOriginal,

            s.MontoNeto,
            s.CodCentroCosto, s.CentroCosto,
            s.CodCanal, s.Canal,

            -- NORMALIZACIÓN DE SUCURSAL DESTINO INICIAL:
            ISNULL(NULLIF(NULLIF(s.CodSucursal, 'N/A'), ''), 'IND4') AS CodSucursalDestino,
            CASE
                WHEN ISNULL(NULLIF(NULLIF(s.CodSucursal, 'N/A'), ''), 'IND4') = 'IND4' THEN 'Central'
                ELSE s.Sucursal
            END AS SucursalDestino,

            s.MontoNeto,
            'SIN_AJUSTE', 'PENDIENTE'
        FROM dbo.Staging_CentralizacionContable s
        WHERE s.AnioMes = @AnioMes
          AND s.TipoRegistro = 'DETALLE'
          AND NOT EXISTS (
              SELECT 1 FROM dbo.Distribucion_Final d WHERE d.StagingId = s.Id
          );

        -- 2. Identificar líneas que tienen regla activa aplicable
        --    Dos universos de líneas elegibles, según la base de la regla:
        --    a) Bases "normales" (CANAL, SUCURSAL, VENTA_SUCURSAL_*, etc.): solo tocan líneas
        --       con Canal indefinido (homologado a IND3) -- comportamiento histórico, sin cambios.
        --    b) Bases "...DESDE_IND4_POR_CANAL" (genérica + una por marca): exigen sucursal=IND4 Y
        --       ADEMÁS canal YA resuelto (distinto de IND3) -- justamente el caso complementario de
        --       (a), nunca el mismo. Esto es a propósito, no solo descriptivo: permite que una misma
        --       cuenta tenga ACTIVAS a la vez una regla "normal" (ej. VENTA_SUCURSAL_SM, para las
        --       líneas con canal todavía indefinido) y su variante "...DESDE_IND4_POR_CANAL" (para
        --       las que ya tienen canal pero quedaron en Central) sin que una misma línea matchee
        --       ambas condiciones a la vez y se distribuya dos veces -- ver también el permiso
        --       especial para esta combinación en FormModel.HayConflicto
        --       (Pages/GestionGastos/Reglas/Form.cshtml.cs).
        ;WITH LineasConRegla AS (
            SELECT
                d.Id AS DistribucionId,
                d.StagingId,
                d.MontoOriginal,
                d.CodCanalOriginal,
                r.Id AS ReglaId,
                r.BaseDistribucion
            FROM dbo.Distribucion_Final d
            INNER JOIN dbo.Reglas_Distribucion r
                ON d.NroCuenta LIKE r.NroCuenta
                AND (
                    r.CodCentroCosto IS NULL
                    OR r.CodCentroCosto = ISNULL(NULLIF(d.CodCentroCostoOriginal, 'N/A'), '9999')
                )
                AND r.Activo = 1
            WHERE d.AnioMes = @AnioMes
              AND d.TipoOrigen = 'SIN_AJUSTE'
              AND (
                    (r.BaseDistribucion NOT IN ('SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                                 'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL',
                                                 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                                 'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL',
                                                 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     AND ISNULL(NULLIF(NULLIF(d.CodCanalOriginal, 'N/A'), ''), 'IND3') = 'IND3')
                 OR (r.BaseDistribucion IN ('SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                             'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL',
                                             'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                             'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL',
                                             'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     AND d.CodSucursalOriginal = 'IND4'
                     AND ISNULL(NULLIF(NULLIF(d.CodCanalOriginal, 'N/A'), ''), 'IND3') <> 'IND3')
                  )
        ),
        -- 3a. Calcular TODAS las filas destino de cada línea antes de insertar
        LineasDistribuidas AS (
            SELECT
                d.Id AS DistribucionId,
                d.StagingId, d.NroAsiento, d.LineaId, d.AnioMes, d.Fecha,
                d.NroCuenta, d.NombreCuenta,
                d.CodCentroCostoOriginal, d.CentroCostoOriginal,
                d.CodCanalOriginal, d.CanalOriginal,
                d.CodSucursalOriginal, d.SucursalOriginal,
                d.MontoOriginal,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                          'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     THEN ISNULL(msuc.CodCentroCosto, d.CodCentroCostoOriginal) ELSE d.CodCentroCostoOriginal END AS CodCentroCostoDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                          'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     THEN ISNULL(msuc.CentroCosto, d.CentroCostoOriginal) ELSE d.CentroCostoOriginal END AS CentroCostoDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.CodDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                         'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                         'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                         'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                         THEN ISNULL(msuc.CodCanal, d.CodCanalOriginal)
                    ELSE d.CodCanalOriginal
                END AS CodCanalDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.NombreDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                         'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                         'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                         'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                         THEN ISNULL(msuc.Canal, d.CanalOriginal)
                    ELSE d.CanalOriginal
                END AS CanalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                          'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     THEN v.CodDimension ELSE d.CodSucursalOriginal END AS CodSucursalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                          'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                          'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                     THEN v.NombreDimension ELSE d.SucursalOriginal END AS SucursalDestino,
                ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2) AS MontoRedondeado,
                lr.ReglaId,
                ROW_NUMBER() OVER (PARTITION BY d.Id ORDER BY v.PorcentajeParticipacion DESC, v.CodDimension) AS Orden,
                SUM(ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2)) OVER (PARTITION BY d.Id) AS TotalRedondeado
            FROM LineasConRegla lr
            INNER JOIN dbo.Distribucion_Final d ON d.Id = lr.DistribucionId
            INNER JOIN dbo.vw_VentaBaseDistribucion v
                ON v.AnioMes = @AnioMes AND v.TipoBase = lr.BaseDistribucion
                -- Las bases "...DESDE_IND4_POR_CANAL" traen un set de pesos POR CANAL
                -- (CodCanalOrigen): solo deben calzar las sucursales del mismo canal con el que
                -- llegó la línea de IND4. Igualdad simple (equi-join) a propósito, NO
                -- "v.TipoBase NOT IN (...) OR v.CodCanalOrigen = ...": ese OR entre columnas
                -- distintas le impedía al optimizador armar un Hash Join contra la vista (que ya
                -- hace varios GROUP BY/OVER sobre toda Staging_CentralizacionContable) y lo forzaba
                -- a un Nested Loop que la reevaluaba por cada línea a distribuir -- con una cuenta
                -- que junta muchas líneas, eso colgaba el procedimiento en la práctica. Con el CASE
                -- de un solo lado vuelve a ser una igualdad pura sobre (TipoBase, CodCanalOrigen)
                -- -- para las bases que NO son "por canal", CodCanalOrigen en la vista viene fijo en
                -- 'TODOS' (ver vw_VentaBaseDistribucion_Final.sql) y acá el CASE también resuelve a
                -- 'TODOS', así que el filtro es un no-op idéntico al de antes.
                AND v.CodCanalOrigen = CASE WHEN lr.BaseDistribucion IN ('SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                                                          'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL',
                                                                          'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                                                          'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL',
                                                                          'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
                                             THEN lr.CodCanalOriginal ELSE 'TODOS' END
            LEFT JOIN dbo.Maestro_Sucursal msuc
                ON msuc.CodSucursal = v.CodDimension AND msuc.Activo = 1
                AND v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM', 'SUCURSAL_COLISEUM', 'SUCURSAL_DESDE_IND4_POR_CANAL', 'SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',
                                    'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM',
                                    'VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',
                                    'VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL', 'VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL')
        )
        -- 3b. Insertar las líneas distribuidas
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
            CASE WHEN Orden = 1 THEN MontoRedondeado + (MontoOriginal - TotalRedondeado) ELSE MontoRedondeado END,
            'AUTO', 'Regla #' + CAST(ReglaId AS VARCHAR), 'APROBADO'
        FROM LineasDistribuidas;

        -- 4. Eliminar las filas SIN_AJUSTE originales que ya fueron reemplazadas por su versión distribuida
        DELETE d
        FROM dbo.Distribucion_Final d
        WHERE d.AnioMes = @AnioMes
          AND d.TipoOrigen = 'SIN_AJUSTE'
          AND EXISTS (
              SELECT 1 FROM dbo.Distribucion_Final d2
              WHERE d2.StagingId = d.StagingId AND d2.TipoOrigen = 'AUTO'
          );

        COMMIT TRANSACTION;

        -- 5. Chequeo de integridad
        SELECT StagingId, SUM(MontoDistribuido) AS TotalDistribuido, MAX(MontoOriginal) AS MontoOriginal
        FROM dbo.Distribucion_Final
        WHERE AnioMes = @AnioMes
        GROUP BY StagingId
        HAVING SUM(MontoDistribuido) <> MAX(MontoOriginal);

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
