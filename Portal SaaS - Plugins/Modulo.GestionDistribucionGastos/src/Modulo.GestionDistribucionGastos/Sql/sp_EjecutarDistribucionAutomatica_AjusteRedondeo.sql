-- El paso 3 calculaba ROUND(MontoOriginal * PorcentajeParticipacion, 2) fila por fila (una por
-- cada sucursal/canal destino) -- la suma de esos redondeos independientes no siempre calza
-- exacto con MontoOriginal (diferencia de centavos por redondeo, más filas = más chance de
-- desvío). El propio paso 5 (chequeo de integridad) ya lo detectaba, pero solo lo reportaba,
-- nunca lo corregía -- quedaba un saldo suelto en la distribución.
-- Fix: se calculan todas las filas de una misma línea (StagingId) en un CTE aparte
-- (LineasDistribuidas), y a la fila con MAYOR PorcentajeParticipacion (la que menos se
-- distorsiona proporcionalmente al absorber el ajuste) se le asigna el resto exacto
-- (MontoOriginal - suma de las demás ya redondeadas) en vez de su propio ROUND() -- así
-- SUM(MontoDistribuido) por StagingId siempre calza exacto con MontoOriginal, sin cambiar el
-- criterio de reparto de ninguna otra fila. Empate de participación se desempata por
-- CodDimension (determinístico, mismo resultado si se corre dos veces). Único cambio respecto
-- a la versión anterior (Sql/sp_EjecutarDistribucionAutomatica_SucursalTprSoloEcom.sql): el
-- paso 3 pasa de un INSERT...SELECT directo a un CTE intermedio con ROW_NUMBER()/SUM() OVER()
-- para poder ajustar una sola fila por línea. El paso 5 (chequeo de integridad) se deja tal
-- cual -- ahora debería devolver siempre 0 filas, sirve como red de seguridad si aparece un
-- caso no contemplado.
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
        ),
        -- 3a. Calcular TODAS las filas destino de cada línea antes de insertar, para poder
        --     ajustar el redondeo dentro del propio grupo (PARTITION BY DistribucionId).
        LineasDistribuidas AS (
            SELECT
                d.Id AS DistribucionId,
                d.StagingId, d.NroAsiento, d.LineaId, d.AnioMes, d.Fecha,
                d.NroCuenta, d.NombreCuenta,
                d.CodCentroCostoOriginal, d.CentroCostoOriginal,
                d.CodCanalOriginal, d.CanalOriginal,
                d.CodSucursalOriginal, d.SucursalOriginal,
                d.MontoOriginal,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CodCentroCosto, d.CodCentroCostoOriginal) ELSE d.CodCentroCostoOriginal END AS CodCentroCostoDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CentroCosto, d.CentroCostoOriginal) ELSE d.CentroCostoOriginal END AS CentroCostoDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.CodDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.CodCanal, d.CodCanalOriginal)
                    ELSE d.CodCanalOriginal
                END AS CodCanalDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.NombreDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN ISNULL(msuc.Canal, d.CanalOriginal)
                    ELSE d.CanalOriginal
                END AS CanalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN v.CodDimension ELSE d.CodSucursalOriginal END AS CodSucursalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM') THEN v.NombreDimension ELSE d.SucursalOriginal END AS SucursalDestino,
                ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2) AS MontoRedondeado,
                lr.ReglaId,
                ROW_NUMBER() OVER (PARTITION BY d.Id ORDER BY v.PorcentajeParticipacion DESC, v.CodDimension) AS Orden,
                SUM(ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2)) OVER (PARTITION BY d.Id) AS TotalRedondeado
            FROM LineasConRegla lr
            INNER JOIN dbo.Distribucion_Final d ON d.Id = lr.DistribucionId
            INNER JOIN dbo.vw_VentaBaseDistribucion v
                ON v.AnioMes = @AnioMes AND v.TipoBase = lr.BaseDistribucion
            LEFT JOIN dbo.Maestro_Sucursal msuc
                ON msuc.CodSucursal = v.CodDimension AND msuc.Activo = 1 AND v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')
        )
        -- 3b. Insertar las líneas distribuidas -- la fila con mayor participación (Orden = 1)
        --     absorbe la diferencia de redondeo del grupo entero; el resto usa su ROUND() normal.
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

        -- 5. Chequeo de integridad: la suma distribuida debe calzar con el original, por línea.
        --    Con el ajuste del paso 3b esto debería devolver siempre 0 filas -- se deja como
        --    red de seguridad para detectar cualquier caso no contemplado.
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
