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
-- CodClasificacion/Clasificacion: clasificación de negocio libre asignada en
-- Agrupacion_Cuenta (mantenedor "Agrupación de Cuentas", solo a nivel de reporte).
-- NULL si la cuenta no tiene clasificación asignada todavía.
-- ReglaAplicada/BaseDistribucion (agregado 2026-08-28): qué regla de Reglas_Distribucion generó
-- cada línea de Distribucion_Final. ReglaAplicada es el texto crudo que ya guarda esa tabla
-- ("Regla #17", "División manual", etc.); BaseDistribucion resuelve el "Regla #N" contra
-- Reglas_Distribucion.BaseDistribucion para saber CON QUÉ LÓGICA se distribuyó (ej.
-- VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL) -- NULL si la regla ya no existe (fue borrada) o si
-- ReglaAplicada no tiene ese formato (división manual, o líneas del grupo 4/5 que vienen directo
-- de Staging_CentralizacionContable y nunca pasan por el motor de reglas). Agregar esta columna
-- vuelve el reporte más granular: una fila que antes agrupaba el aporte de varias reglas distintas
-- sobre el mismo mes/cuenta/canal/sucursal ahora se abre en una fila por regla -- el total
-- (SUMA(Monto)) no cambia, solo el detalle.
CREATE   VIEW dbo.vw_EerrAnual AS
WITH Datos AS (  
    SELECT  
        d.AnioMes,   
        LEFT(d.AnioMes, 4) AS Anio,   
        RIGHT(d.AnioMes, 2) AS Mes,  
        SUBSTRING(d.NroCuenta, 1, 1) AS Grupo,   
        d.NroCuenta,   
        d.NombreCuenta,  
        d.CodCentroCostoDestino AS CodCentroCosto,   
        d.CentroCostoDestino AS CentroCosto,  
          
        -- HOMOLOGACIÓN DE CANAL (Distribucion_Final)  
        CASE   
            WHEN d.CodCanalDestino = 'OTROS' OR d.CodCanalDestino IS NULL OR d.CodCanalDestino = 'N/A' THEN 'IND3'  
            ELSE d.CodCanalDestino   
        END AS CodCanal,  
        CASE   
            WHEN d.CanalDestino = 'OTROS' OR d.CanalDestino IS NULL OR d.CanalDestino = 'N/A' THEN 'Central'  
            ELSE d.CanalDestino   
        END AS Canal,  
          
        -- HOMOLOGACIÓN DE SUCURSAL (Distribucion_Final)  
        CASE   
            WHEN d.CodSucursalDestino IN ('1201', 'OTRAS VENTAS', 'N/A') OR d.CodSucursalDestino IS NULL THEN 'IND4'  
            ELSE d.CodSucursalDestino   
        END AS CodSucursal,  
        CASE   
            WHEN d.SucursalDestino IN ('1201', 'OTRAS VENTAS', 'N/A') OR d.SucursalDestino IS NULL THEN 'Central'  
            ELSE d.SucursalDestino   
        END AS Sucursal,  
  
        d.TipoOrigen,
        d.EsGastoIndirecto,
        d.ReglaAplicada,
        -d.MontoDistribuido AS Monto
    FROM dbo.Distribucion_Final d
    WHERE d.NroCuenta IS NOT NULL
  
    UNION ALL  
  
    SELECT  
        s.AnioMes,   
        LEFT(s.AnioMes, 4),   
        RIGHT(s.AnioMes, 2),  
        SUBSTRING(s.NroCuenta, 1, 1),   
        s.NroCuenta,   
        s.NombreCuenta,  
        s.CodCentroCosto,   
        s.CentroCosto,   
          
        -- HOMOLOGACIÓN DE CANAL (Staging_CentralizacionContable)  
        CASE   
            WHEN s.CodCanal = 'OTROS' OR s.CodCanal IS NULL OR s.CodCanal = 'N/A' THEN 'IND3'  
            ELSE s.CodCanal   
        END AS CodCanal,  
        CASE   
            WHEN s.Canal = 'OTROS' OR s.Canal IS NULL OR s.Canal = 'N/A' THEN 'Central'  
            ELSE s.Canal   
        END AS Canal,  
          
        -- HOMOLOGACIÓN DE SUCURSAL (Staging_CentralizacionContable)  
        CASE   
            WHEN s.CodSucursal IN ('1201', 'OTRAS VENTAS', 'N/A') OR s.CodSucursal IS NULL THEN 'IND4'  
            ELSE s.CodSucursal   
        END AS CodSucursal,  
        CASE   
            WHEN s.Sucursal IN ('1201', 'OTRAS VENTAS', 'N/A') OR s.Sucursal IS NULL THEN 'Central'  
            ELSE s.Sucursal   
        END AS Sucursal,  
  
        'RESUMEN',
        CAST(0 AS BIT),
        CAST(NULL AS NVARCHAR(200)),
        -s.MontoNeto
    FROM dbo.Staging_CentralizacionContable s
    WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta IS NOT NULL
)
SELECT  
    Datos.Anio,   
    Datos.Mes,   
    Datos.AnioMes,   
    Datos.Grupo,  
    NombreGrupo = CASE Datos.Grupo  
        WHEN '4' THEN '4 - INGRESOS'   
        WHEN '5' THEN '5 - COSTO DE VENTAS'  
        WHEN '6' THEN '6 - GASTOS OPERACIONALES'   
        WHEN '7' THEN '7 - OTROS INGRESOS Y EGRESOS'  
        WHEN '8' THEN '8 - OTROS GASTOS'   
        WHEN '9' THEN '9 - IMPUESTOS'   
    END,  
    Datos.NroCuenta,   
    Datos.NombreCuenta,  
    Datos.CodCentroCosto,   
    Datos.CentroCosto,   
    Datos.CodCanal,   
    Datos.Canal,   
    Datos.CodSucursal,   
    Datos.Sucursal,  
    Datos.TipoOrigen,
    Datos.EsGastoIndirecto,
    Tipo_Gasto = IIF(Datos.EsGastoIndirecto = 1, 'Indirectos', 'Directos'),
    cc.Codigo AS CodClasificacion,
    RIGHT('00' + CAST(cc.Orden AS VARCHAR), 2) + '-' + cc.Nombre AS Clasificacion,
    Datos.ReglaAplicada,
    r.BaseDistribucion,
    SUM(Datos.Monto) AS Monto
FROM Datos
LEFT JOIN dbo.Agrupacion_Cuenta ac ON ac.NroCuenta = Datos.NroCuenta
LEFT JOIN dbo.Clasificacion_Cuenta cc ON cc.Id = ac.ClasificacionId
-- Resuelve "Regla #17" contra Reglas_Distribucion -- TRY_CAST evita romper si ReglaAplicada trae
-- otro formato (división manual, o NULL en líneas que nunca pasaron por el motor de reglas).
LEFT JOIN dbo.Reglas_Distribucion r
    ON Datos.ReglaAplicada LIKE 'Regla #%'
   AND r.Id = TRY_CAST(SUBSTRING(Datos.ReglaAplicada, 8, 50) AS INT)
WHERE Datos.Grupo IN ('4', '5', '6', '7', '8', '9')
GROUP BY Datos.Anio, Datos.Mes, Datos.AnioMes, Datos.Grupo, Datos.NroCuenta, Datos.NombreCuenta,
         Datos.CodCentroCosto, Datos.CentroCosto, Datos.CodCanal, Datos.Canal, Datos.CodSucursal, Datos.Sucursal,
         Datos.TipoOrigen, Datos.EsGastoIndirecto, cc.Orden, cc.Codigo, cc.Nombre,
         Datos.ReglaAplicada, r.BaseDistribucion;