-- Reversa de sp_RepartirSucursalCentral (ver ese archivo) -- acción global, a demanda, disparada
-- por el admin desde el mismo botón/tarjeta ("Deshacer reparto Sucursal Central"). Busca las filas
-- generadas por esa acción vía el sufijo en ReglaAplicada ("- origen MANUAL" / "- origen
-- SIN_AJUSTE"), las agrupa de vuelta por (StagingId, CodCanalDestino) -- puede haber varias filas
-- por grupo (una por sucursal real que recibió parte del reparto) -- y las colapsa en UNA sola
-- fila con Sucursal='IND4' otra vez, sumando MontoDistribuido, restaurando TipoOrigen/Estado según
-- el sufijo, y con CentroCosto Destino = '9999'/'Indirectos Central' (el mapeo propio de IND4 en
-- Maestro_Sucursal) -- no se intenta reconstruir el CC que tenía cada fila antes del reparto,
-- porque se perdió al cascadear por sucursal (cada destino real cascadea su propio CC).
-- No toca ReglaAplicada/Comentario/UsuarioResponsable previos a la reparto -- sp_RepartirSucursalCentral
-- tampoco los preservaba (columnas fuera de su INSERT), así que no hay nada que restaurar ahí.
-- Mismos candados que el resto del módulo: no toca mes cerrado ni cuenta aprobada. Si algún grupo
-- quedó parcialmente en un mes cerrado (no debería, ya que sp_RepartirSucursalCentral tampoco los
-- toca), esa fila simplemente no entra al filtro y queda sin deshacer -- se reporta si hiciera falta
-- ampliando el SELECT final, hoy no se distingue.
CREATE OR ALTER PROCEDURE dbo.sp_DeshacerRepartoSucursalCentral
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        SELECT
            d.Id, d.StagingId, d.CodCanalDestino,
            CASE WHEN CHARINDEX('- origen MANUAL', d.ReglaAplicada) > 0 THEN 'MANUAL' ELSE 'SIN_AJUSTE' END AS TipoOrigenPrevio
        INTO #FilasRepartidas
        FROM dbo.Distribucion_Final d
        LEFT JOIN dbo.Cierre_Mes cm ON cm.AnioMes = d.AnioMes
        LEFT JOIN dbo.Cuenta_Aprobada ca ON ca.AnioMes = d.AnioMes AND ca.NroCuenta = d.NroCuenta
        WHERE d.TipoOrigen = 'MANUAL'
          AND d.ReglaAplicada LIKE 'Reparto Sucursal Central (IND4)%'
          AND ISNULL(cm.Estado, 'ABIERTO') <> 'CERRADO'
          AND ca.NroCuenta IS NULL;

        IF EXISTS (SELECT 1 FROM #FilasRepartidas)
        BEGIN
            ;WITH Grupos AS (
                SELECT
                    d.StagingId, d.CodCanalDestino,
                    MAX(f.TipoOrigenPrevio) AS TipoOrigenPrevio,
                    MAX(d.NroAsiento) AS NroAsiento, MAX(d.LineaId) AS LineaId,
                    MAX(d.AnioMes) AS AnioMes, MAX(d.Fecha) AS Fecha,
                    MAX(d.NroCuenta) AS NroCuenta, MAX(d.NombreCuenta) AS NombreCuenta,
                    MAX(d.CodCentroCostoOriginal) AS CodCentroCostoOriginal, MAX(d.CentroCostoOriginal) AS CentroCostoOriginal,
                    MAX(d.CodCanalOriginal) AS CodCanalOriginal, MAX(d.CanalOriginal) AS CanalOriginal,
                    MAX(d.CodSucursalOriginal) AS CodSucursalOriginal, MAX(d.SucursalOriginal) AS SucursalOriginal,
                    MAX(d.MontoOriginal) AS MontoOriginal,
                    MAX(d.CanalDestino) AS CanalDestino,
                    SUM(d.MontoDistribuido) AS MontoDistribuido
                FROM dbo.Distribucion_Final d
                INNER JOIN #FilasRepartidas f ON f.Id = d.Id
                GROUP BY d.StagingId, d.CodCanalDestino
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
                 TipoOrigen, Estado)
            SELECT
                StagingId, NroAsiento, LineaId, AnioMes, Fecha,
                NroCuenta, NombreCuenta,
                CodCentroCostoOriginal, CentroCostoOriginal,
                CodCanalOriginal, CanalOriginal,
                CodSucursalOriginal, SucursalOriginal,
                MontoOriginal,
                '9999', 'Indirectos Central',
                CodCanalDestino, CanalDestino,
                'IND4', 'Central',
                MontoDistribuido,
                TipoOrigenPrevio,
                CASE WHEN TipoOrigenPrevio = 'MANUAL' THEN 'APROBADO' ELSE 'PENDIENTE' END
            FROM Grupos;

            DELETE FROM dbo.Distribucion_Final WHERE Id IN (SELECT Id FROM #FilasRepartidas);
        END

        COMMIT TRANSACTION;

        SELECT
            COUNT(DISTINCT CONCAT(StagingId, '|', CodCanalDestino)) AS GruposDeshechos,
            COUNT(*) AS FilasEliminadas
        FROM #FilasRepartidas;

        DROP TABLE #FilasRepartidas;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF OBJECT_ID('tempdb..#FilasRepartidas') IS NOT NULL DROP TABLE #FilasRepartidas;
        THROW;
    END CATCH
END
