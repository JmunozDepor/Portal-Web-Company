-- Extiende la cascada Sucursal->Canal->Centro de costo (que hoy mira TipoBase IN ('SUCURSAL',
-- 'SUCURSAL_TPR_ECM')) para que también aplique a la nueva base 'SUCURSAL_TPR_SOLO_ECOM' -- misma
-- lógica de reparto por sucursal, solo que calculada sobre un subconjunto más chico de sucursales
-- (ver Sql/vw_VentaBaseDistribucion_SucursalTprSoloEcom.sql). Único cambio respecto a la versión
-- anterior (Sql/sp_EjecutarDistribucionAutomatica_SucursalTprEcm.sql): cada
-- "v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM')" pasa a
-- "v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')".
CREATE OR ALTER PROCEDURE dbo.sp_EjecutarDistribucionAutomatica
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
            s.CodSucursal, s.Sucursal,
            s.MontoNeto,
            s.CodCentroCosto, s.CentroCosto,
            s.CodCanal, s.Canal,
            s.CodSucursal, s.Sucursal,
            s.MontoNeto,
            'SIN_AJUSTE', 'PENDIENTE'
        FROM dbo.Staging_CentralizacionContable s
        WHERE s.AnioMes = @AnioMes
          AND s.TipoRegistro = 'DETALLE'
          AND NOT EXISTS (
              SELECT 1 FROM dbo.Distribucion_Final d WHERE d.StagingId = s.Id
          );

        -- 2. Identificar líneas que tienen regla activa aplicable
        --    (match por Cuenta -- vía LIKE, admite patrones -- + CC, o por Cuenta si la regla no especifica CC)
        ;WITH LineasConRegla AS (
            SELECT
                d.Id AS DistribucionId,
                d.StagingId,
                d.MontoOriginal,
                r.Id AS ReglaId,
                r.BaseDistribucion
            FROM dbo.Distribucion_Final d
            INNER JOIN dbo.Reglas_Distribucion r
                ON d.NroCuenta LIKE r.NroCuenta
                AND (r.CodCentroCosto IS NULL OR r.CodCentroCosto = d.CodCentroCostoOriginal)
                AND r.Activo = 1
            WHERE d.AnioMes = @AnioMes
              AND d.TipoOrigen = 'SIN_AJUSTE'
        )
        -- 3. Insertar las líneas distribuidas (una por canal/sucursal)
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
            d.StagingId, d.NroAsiento, d.LineaId, d.AnioMes, d.Fecha,
            d.NroCuenta, d.NombreCuenta,
            d.CodCentroCostoOriginal, d.CentroCostoOriginal,
            d.CodCanalOriginal, d.CanalOriginal,
            d.CodSucursalOriginal, d.SucursalOriginal,
            d.MontoOriginal,
            -- Centro de costo: si se distribuye por sucursal (SUCURSAL o alguna variante TPR/ECM) y
            -- esa sucursal está en el maestro, se cascadea; si no, igual al original.
            CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CodCentroCosto, d.CodCentroCostoOriginal) ELSE d.CodCentroCostoOriginal END,
            CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CentroCosto, d.CentroCostoOriginal) ELSE d.CentroCostoOriginal END,
            -- Canal: si la base es CANAL, viene directo de la vista; si es por sucursal, se cascadea desde el maestro (o queda igual si no está mapeada).
            CASE
                WHEN v.TipoBase = 'CANAL' THEN v.CodDimension
                WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CodCanal, d.CodCanalOriginal)
                ELSE d.CodCanalOriginal
            END,
            CASE
                WHEN v.TipoBase = 'CANAL' THEN v.NombreDimension
                WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.Canal, d.CanalOriginal)
                ELSE d.CanalOriginal
            END,
            CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN v.CodDimension ELSE d.CodSucursalOriginal END,
            CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN v.NombreDimension ELSE d.SucursalOriginal END,
            ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2),
            'AUTO', 'Regla #' + CAST(lr.ReglaId AS VARCHAR), 'APROBADO'
        FROM LineasConRegla lr
        INNER JOIN dbo.Distribucion_Final d ON d.Id = lr.DistribucionId
        INNER JOIN dbo.vw_VentaBaseDistribucion v
            ON v.AnioMes = @AnioMes AND v.TipoBase = lr.BaseDistribucion
        LEFT JOIN dbo.Maestro_Sucursal msuc
            ON msuc.CodSucursal = v.CodDimension AND msuc.Activo = 1 AND v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM');

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

        -- 5. Chequeo de integridad: la suma distribuida debe calzar con el original, por línea
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
