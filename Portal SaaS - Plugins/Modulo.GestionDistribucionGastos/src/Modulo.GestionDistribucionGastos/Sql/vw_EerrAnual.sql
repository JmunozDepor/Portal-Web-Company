-- Fuente única agregada para reportes EERR fuera de la app (Excel/SQL directo): un año completo,
-- sin detalle a nivel de línea/documento (agrupa por mes/cuenta/dimensión, no trae NroAsiento/
-- LineaId/StagingId/comentario/usuario). Combina grupos 6-9 (Distribucion_Final, ya corregidos,
-- columnas *Destino) con grupos 4-5 (Staging_CentralizacionContable RESUMEN, sin tocar) -- mismo
-- criterio que usa Controllers/EerrController.cs para la pantalla EERR.
-- Monto queda invertido respecto al dato crudo de SAP (Crédito - Débito, no Débito - Crédito):
-- Ingresos (grupo 4) sale POSITIVO, Costo/Gastos (5-9) salen NEGATIVOS. Es a propósito -- así,
-- para cualquiera que arme un pivot en Excel, un simple SUMA(Monto) da directo el resultado final
-- con el signo correcto (Ingresos - Costo - Gastos), sin tener que restar nada a mano. Ojo: este
-- signo es el inverso del que usan MontoDistribuido/MontoNeto en las tablas base y en
-- Controllers/EerrController.cs (que sí usan la convención cruda Débito-Crédito).
CREATE OR ALTER VIEW dbo.vw_EerrAnual AS
WITH Datos AS (
    SELECT
        d.AnioMes, LEFT(d.AnioMes, 4) AS Anio, RIGHT(d.AnioMes, 2) AS Mes,
        SUBSTRING(d.NroCuenta, 1, 1) AS Grupo, d.NroCuenta, d.NombreCuenta,
        d.CodCentroCostoDestino AS CodCentroCosto, d.CentroCostoDestino AS CentroCosto,
        d.CodCanalDestino AS CodCanal, d.CanalDestino AS Canal,
        d.CodSucursalDestino AS CodSucursal, d.SucursalDestino AS Sucursal,
        d.TipoOrigen, d.EsGastoIndirecto, -d.MontoDistribuido AS Monto
    FROM dbo.Distribucion_Final d
    WHERE d.NroCuenta IS NOT NULL

    UNION ALL

    SELECT
        s.AnioMes, LEFT(s.AnioMes, 4), RIGHT(s.AnioMes, 2),
        SUBSTRING(s.NroCuenta, 1, 1), s.NroCuenta, s.NombreCuenta,
        s.CodCentroCosto, s.CentroCosto, s.CodCanal, s.Canal, s.CodSucursal, s.Sucursal,
        'RESUMEN', CAST(0 AS BIT), -s.MontoNeto
    FROM dbo.Staging_CentralizacionContable s
    WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta IS NOT NULL
)
SELECT
    Anio, Mes, AnioMes, Grupo,
    NombreGrupo = CASE Grupo
        WHEN '4' THEN '4 - INGRESOS' WHEN '5' THEN '5 - COSTO DE VENTAS'
        WHEN '6' THEN '6 - GASTOS OPERACIONALES' WHEN '7' THEN '7 - OTROS INGRESOS Y EGRESOS'
        WHEN '8' THEN '8 - OTROS GASTOS' WHEN '9' THEN '9 - IMPUESTOS' END,
    NroCuenta, NombreCuenta,
    CodCentroCosto, CentroCosto, CodCanal, Canal, CodSucursal, Sucursal,
    TipoOrigen, EsGastoIndirecto,
    -- Para poder filtrar/agrupar el pivot igual que la pantalla EERR: todo lo que no es gasto
    -- indirecto (ingresos, costo de ventas, gastos directos) queda dentro de Res.Operacional-1;
    -- el gasto indirecto es lo único que recién se resta para llegar a Res.Operacional-2.
    Resultado = IIF(EsGastoIndirecto = 1, 'Res.Operacional-2', 'Res.Operacional-1'),
    SUM(Monto) AS Monto
FROM Datos
WHERE Grupo IN ('4', '5', '6', '7', '8', '9')
GROUP BY Anio, Mes, AnioMes, Grupo, NroCuenta, NombreCuenta,
         CodCentroCosto, CentroCosto, CodCanal, Canal, CodSucursal, Sucursal,
         TipoOrigen, EsGastoIndirecto;
