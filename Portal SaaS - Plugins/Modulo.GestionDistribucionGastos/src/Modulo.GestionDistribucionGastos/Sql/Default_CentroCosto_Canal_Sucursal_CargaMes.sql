USE [CLDEPORFIN]
GO
/****** Object:  StoredProcedure [dbo].[sp_CargarCentralizacionMes] ******/
-- Ajuste 2026-08-05: PARTE 1 (DETALLE, grupos 6/7/8/9 -- la que alimenta Distribucion_Final)
-- traía T0."ProfitCode"/"OcrCode3"/"OcrCode4" crudos desde HANA sin ningún fallback de código --
-- solo el NOMBRE tenía IFNULL(...,'N/A'), el CÓDIGO quedaba NULL/vacío tal cual llegaba de SAP.
-- Eso dejó ~228.000 líneas de un mes con CodCanal/CodSucursal en blanco en Distribucion_Final
-- (detectado y corregido puntualmente con UPDATE directo sobre las filas ya cargadas; ver
-- conversación -- este cambio evita que se repita en la próxima carga de mes).
-- Default aplicado en el ORIGEN (esta carga), no en Staging_CentralizacionContable después de
-- cargado ni por un UPDATE aparte: Centro de costo vacío -> '9999'/'Indirectos Central',
-- Canal vacío -> 'IND3'/'Central', Sucursal vacía -> 'IND4'/'Central' (mismos códigos/nombres
-- que ya usa Maestro_Sucursal para la fila IND4, ver Sql/Maestro_Sucursal.sql). El nombre por
-- lookup a OPRC (IFNULL(...,'N/A')) se conserva tal cual para el caso en que el código SÍ vino
-- pero no tiene nombre en OPRC -- el default solo pisa cuando el código en sí es NULL/vacío.
-- Marca (OcrCode2) y Tipo de gasto (OcrCode5) NO llevan default -- no se pidió y no hay
-- convención de código "central" definida para esas dos dimensiones.
-- PARTE 2 (RESUMEN, grupos 4/5 -- venta) SÍ lleva el mismo default de Centro de costo (mismo
-- problema de origen: T0."ProfitCode" pasaba crudo, sin fallback de código) -- detectado vía
-- vw_EerrAnual (junta Distribucion_Final grupos 6-9 con Staging RESUMEN grupos 4-5) mostrando
-- CodCentroCosto en blanco/'IND1' para filas de venta. Canal/Sucursal de PARTE 2 quedan SIN
-- tocar a propósito: la venta ya está bien distribuida en esas dos dimensiones (confirmado), y
-- vw_VentaBaseDistribucion_ExcluirCentral ya excluye IND3/IND4 de la base de participación
-- leyendo directo del staging -- no hace falta duplicar ese default ahí.
-- Resto del SP (fallback SYSDeb/SYSCred deshabilitado, filtros TransType<>58 / LineMemo NOT LIKE
-- 'P.41%', recarga idempotente que preserva MANUAL) sin cambios -- ver
-- Sql/Deshabilitar_Fallback_SysDeb_SysCred.sql para ese historial.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE [dbo].[sp_CargarCentralizacionMes]
    @Anio INT,
    @Mes  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FechaInicio VARCHAR(8) = CONVERT(VARCHAR(4), @Anio)
                                     + RIGHT('0' + CONVERT(VARCHAR(2), @Mes), 2) + '01';
    DECLARE @FechaFin VARCHAR(8) = CONVERT(VARCHAR(8),
                                    EOMONTH(DATEFROMPARTS(@Anio, @Mes, 1)), 112);
    DECLARE @AnioMes CHAR(6) = CONVERT(VARCHAR(4), @Anio)
                              + RIGHT('0' + CONVERT(VARCHAR(2), @Mes), 2);
    DECLARE @SQL NVARCHAR(MAX);

    -- Recarga idempotente que preserva el trabajo manual ya aprobado:
    -- 1) Se liberan (regeneran) las filas SIN_AJUSTE/AUTO; las MANUAL quedan intactas.
    -- 2) Solo se borra del staging lo que ya no tenga ninguna fila de Distribucion_Final
    --    apuntandolo (es decir, lo que no quedo protegido por una linea MANUAL).
    DELETE FROM dbo.Distribucion_Final
    WHERE AnioMes = @AnioMes AND TipoOrigen IN ('SIN_AJUSTE', 'AUTO');

    DELETE s
    FROM dbo.Staging_CentralizacionContable s
    WHERE s.AnioMes = @AnioMes
      AND NOT EXISTS (SELECT 1 FROM dbo.Distribucion_Final d WHERE d.StagingId = s.Id);

    -----------------------------------------------------------------
    -- PARTE 1: DETALLE grupos 6,7,8,9
    -- Se excluyen (NroAsiento, LineaId) que ya existan en el staging del mes,
    -- es decir, las lineas que quedaron protegidas por trabajo manual aprobado.
    -----------------------------------------------------------------
    SET @SQL = N'
    INSERT INTO dbo.Staging_CentralizacionContable
        (CodigoGrupo, NombreGrupo, NroCuenta, NombreCuenta, CodigoSocio, SocioNegocio,
         CodCentroCosto, CentroCosto, CodMarca, Marca, CodCanal, Canal,
         CodSucursal, Sucursal, CodTipoGasto, TipoGasto,
         NroAsiento, LineaId, DocInternoSAP, Fecha, Comentarios,
         Debito, Credito, MontoNeto, AnioMes, TipoRegistro)
    SELECT o.CodigoGrupo, o.NombreGrupo, o.NroCuenta, o.NombreCuenta, o.CodigoSocio, o.SocioNegocio,
           o.CodCentroCosto, o.CentroCosto, o.CodMarca, o.Marca, o.CodCanal, o.Canal,
           o.CodSucursal, o.Sucursal, o.CodTipoGasto, o.TipoGasto,
           o.NroAsiento, o.LineaId, o.DocInternoSAP, o.Fecha, o.Comentarios,
           o.Debito, o.Credito, o.MontoNeto, ''' + @AnioMes + ''', ''DETALLE''
    FROM (
        SELECT * FROM OPENQUERY([SAPHANA], ''
        SELECT
            T1."GroupMask",
            CASE
                WHEN T1."GroupMask" = ''''6'''' THEN ''''6 - GASTOS OPERACIONALES''''
                WHEN T1."GroupMask" = ''''7'''' THEN ''''7 - OTROS INGRESOS Y EGRESOS''''
                WHEN T1."GroupMask" = ''''8'''' THEN ''''8 - OTROS GASTOS''''
                WHEN T1."GroupMask" = ''''9'''' THEN ''''9 - IMPUESTOS''''
                ELSE ''''OTROS''''
            END,
            T1."FormatCode", T1."AcctName",
            IFNULL(T4."CardCode", ''''-''''), IFNULL(T4."CardName", ''''Sin Socio''''),
            IFNULL(NULLIF(T0."ProfitCode", ''''''''), ''''9999''''),
            CASE WHEN IFNULL(NULLIF(T0."ProfitCode", ''''''''), ''''9999'''') = ''''9999''''
                 THEN ''''Indirectos Central''''
                 ELSE IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."ProfitCode"), ''''N/A'''')
            END,
            T0."OcrCode2",
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode2"), ''''N/A''''),
            IFNULL(NULLIF(T0."OcrCode3", ''''''''), ''''IND3''''),
            CASE WHEN IFNULL(NULLIF(T0."OcrCode3", ''''''''), ''''IND3'''') = ''''IND3''''
                 THEN ''''Central''''
                 ELSE IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode3"), ''''N/A'''')
            END,
            IFNULL(NULLIF(T0."OcrCode4", ''''''''), ''''IND4''''),
            CASE WHEN IFNULL(NULLIF(T0."OcrCode4", ''''''''), ''''IND4'''') = ''''IND4''''
                 THEN ''''Central''''
                 ELSE IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode4"), ''''N/A'''')
            END,
            T0."OcrCode5",
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode5"), ''''N/A''''),
            TO_VARCHAR(T0."TransId"), T0."Line_ID",
            T3."BaseRef", TO_VARCHAR(T3."RefDate", ''''YYYY-MM-DD''''),
            T0."LineMemo",
            T0."Debit" /* CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Debit" ELSE T0."SYSDeb" END -- fallback deshabilitado, ver encabezado de la SP */,
            T0."Credit" /* CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Credit" ELSE T0."SYSCred" END */,
            (T0."Debit" - T0."Credit") /* CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN (T0."Debit" - T0."Credit") ELSE (T0."SYSDeb" - T0."SYSCred") END */
        FROM "CLPRDDEPOR"."JDT1" T0
        INNER JOIN "CLPRDDEPOR"."OACT" T1 ON T0."Account" = T1."AcctCode"
        INNER JOIN "CLPRDDEPOR"."OJDT" T3 ON T0."TransId" = T3."TransId"
        LEFT JOIN "CLPRDDEPOR"."OCRD" T4 ON T0."ShortName" = T4."CardCode"
        WHERE T3."RefDate" >= ''''' + @FechaInicio + '''''
          AND T3."RefDate" <= ''''' + @FechaFin + '''''
          AND T1."GroupMask" IN (''''6'''',''''7'''',''''8'''',''''9'''')
          AND (T0."Debit" <> 0 OR T0."Credit" <> 0 OR T0."SYSDeb" <> 0 OR T0."SYSCred" <> 0)
          AND T3."TransType" <> 58
          AND IFNULL(T0."LineMemo", '''''''') NOT LIKE ''''P.41%''''
    '')
    ) AS o (CodigoGrupo, NombreGrupo, NroCuenta, NombreCuenta, CodigoSocio, SocioNegocio,
            CodCentroCosto, CentroCosto, CodMarca, Marca, CodCanal, Canal,
            CodSucursal, Sucursal, CodTipoGasto, TipoGasto,
            NroAsiento, LineaId, DocInternoSAP, Fecha, Comentarios,
            Debito, Credito, MontoNeto)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Staging_CentralizacionContable s2
        WHERE s2.AnioMes = ''' + @AnioMes + '''
          AND s2.TipoRegistro = ''DETALLE''
          AND s2.NroAsiento = o.NroAsiento
          AND s2.LineaId = o.LineaId
    );
    ';

    EXEC sp_executesql @SQL;

    -----------------------------------------------------------------
    -- PARTE 2: RESUMEN grupos 4,5 (nuevo - necesario para venta base)
    -- Sin cambios en este ajuste -- la venta ya está bien distribuida (confirmado) y
    -- vw_VentaBaseDistribucion_ExcluirCentral ya excluye IND3/IND4 de la base leyendo
    -- directo del staging, así que no hace falta duplicar el default acá.
    -- No tiene FK desde Distribucion_Final, por lo que siempre se regenera completo
    -- (ya fue borrado en el DELETE general de arriba, junto con el resto del staging no protegido).
    -----------------------------------------------------------------
    SET @SQL = N'
    INSERT INTO dbo.Staging_CentralizacionContable
        (CodigoGrupo, NombreGrupo, NroCuenta, NombreCuenta, CodigoSocio, SocioNegocio,
         CodCentroCosto, CentroCosto, CodMarca, Marca, CodCanal, Canal,
         CodSucursal, Sucursal, CodTipoGasto, TipoGasto,
         NroAsiento, LineaId, DocInternoSAP, Fecha, Comentarios,
         Debito, Credito, MontoNeto, AnioMes, TipoRegistro)
    SELECT ''RESUMEN'', NULL, NroCuenta, NombreCuenta, ''-'', ''Resumen Cuenta'',
           CodCentroCosto, CentroCosto, CodMarca, Marca, CodCanal, Canal,
           CodSucursal, Sucursal, CodTipoGasto, TipoGasto,
           ''RESUMEN'', NULL, NULL, Fecha, ''Monto consolidado por dimensiones'',
           Debito, Credito, MontoNeto, ''' + @AnioMes + ''', ''RESUMEN''
    FROM OPENQUERY([SAPHANA], ''
        SELECT
            T1."GroupMask" AS GroupMask,
            T1."FormatCode" AS NroCuenta,
            T1."AcctName" AS NombreCuenta,
            IFNULL(NULLIF(T0."ProfitCode", ''''''''), ''''9999'''') AS CodCentroCosto,
            CASE WHEN IFNULL(NULLIF(T0."ProfitCode", ''''''''), ''''9999'''') = ''''9999''''
                 THEN ''''Indirectos Central''''
                 ELSE IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."ProfitCode"), ''''N/A'''')
            END AS CentroCosto,
            T0."OcrCode2" AS CodMarca,
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode2"), ''''N/A'''') AS Marca,
            T0."OcrCode3" AS CodCanal,
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode3"), ''''N/A'''') AS Canal,
            T0."OcrCode4" AS CodSucursal,
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode4"), ''''N/A'''') AS Sucursal,
            T0."OcrCode5" AS CodTipoGasto,
            IFNULL((SELECT "PrcName" FROM "CLPRDDEPOR"."OPRC" WHERE "PrcCode" = T0."OcrCode5"), ''''N/A'''') AS TipoGasto,
            TO_VARCHAR(T3."RefDate", ''''YYYY-MM-DD'''') AS Fecha,
            SUM(CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Debit" ELSE T0."SYSDeb" END) AS Debito,
            SUM(CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Credit" ELSE T0."SYSCred" END) AS Credito,
            SUM(CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN (T0."Debit" - T0."Credit")
                     ELSE (T0."SYSDeb" - T0."SYSCred") END) AS MontoNeto
        FROM "CLPRDDEPOR"."JDT1" T0
        INNER JOIN "CLPRDDEPOR"."OACT" T1 ON T0."Account" = T1."AcctCode"
        INNER JOIN "CLPRDDEPOR"."OJDT" T3 ON T0."TransId" = T3."TransId"
        WHERE T3."RefDate" >= ''''' + @FechaInicio + '''''
          AND T3."RefDate" <= ''''' + @FechaFin + '''''
          AND T1."GroupMask" IN (''''4'''',''''5'''')
          AND T3."TransType" <> 58
          AND IFNULL(T0."LineMemo", '''''''') NOT LIKE ''''P.41%''''
        GROUP BY T1."GroupMask", T1."FormatCode", T1."AcctName",
                 T0."ProfitCode", T0."OcrCode2", T0."OcrCode3", T0."OcrCode4", T0."OcrCode5",
                 TO_VARCHAR(T3."RefDate", ''''YYYY-MM-DD'''')
        HAVING SUM(CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Debit" ELSE T0."SYSDeb" END) <> 0
            OR SUM(CASE WHEN T0."Debit" <> 0 OR T0."Credit" <> 0 THEN T0."Credit" ELSE T0."SYSCred" END) <> 0
    '') AS Origen;
    ';

    EXEC sp_executesql @SQL;

    -----------------------------------------------------------------
    -- Retorno de control
    -----------------------------------------------------------------
    SELECT TipoRegistro, COUNT(*) AS Cantidad, SUM(MontoNeto) AS TotalMonto
    FROM dbo.Staging_CentralizacionContable
    WHERE AnioMes = @AnioMes
    GROUP BY TipoRegistro;
END