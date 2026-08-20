# Bases de Distribución "Venta por Marca, por Sucursal" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar 4 bases de distribución nuevas (`VENTA_SUCURSAL_CONVERSE`, `VENTA_SUCURSAL_UMBRO`, `VENTA_SUCURSAL_FILA`, `VENTA_SUCURSAL_SM`) que reparten un gasto entre todas las sucursales en proporción a la venta de una cuenta contable puntual (una marca) en cada sucursal, no a la venta total.

**Architecture:** Se extiende `dbo.vw_VentaBaseDistribucion` (una rama `UNION ALL` nueva por marca, filtrando `Staging_CentralizacionContable` por `NroCuenta`) y `dbo.sp_EjecutarDistribucionAutomatica` (agregar las 4 bases nuevas a los `IN (...)` que activan la cascada sucursal→centro de costo/canal vía `Maestro_Sucursal`). Se agregan 4 `<option>` al dropdown de `BaseDistribucion` en `Reglas/Form.cshtml`. Ningún cambio de esquema ni de modelo — `BaseDistribucion` ya es `VARCHAR(50)` libre.

**Tech Stack:** ASP.NET Core 8 Razor Pages, T-SQL (SQL Server, base externa CLDEPORFIN).

## Global Constraints

- Los 4 códigos de base son exactamente: `VENTA_SUCURSAL_CONVERSE` (cuenta 41-01-001-04), `VENTA_SUCURSAL_UMBRO` (41-01-001-05), `VENTA_SUCURSAL_FILA` (41-01-001-06), `VENTA_SUCURSAL_SM` (41-01-001-09).
- Cada rama nueva de la vista participa con TODAS las sucursales (sin subconjunto de canal/sucursal, a diferencia de `SUCURSAL_TPR_ECM`/`SUCURSAL_TPR_SOLO_ECOM`).
- Los scripts SQL de este módulo son archivos `CREATE OR ALTER` versionados en `Sql/`, para que el usuario/DBA los ejecute manualmente contra CLDEPORFIN — el módulo no corre DDL automáticamente al iniciar. No hay motor de tests automatizado contra esta base externa; la verificación es revisión sintáctica manual + `dotnet build`/`-t:CoreCompile` del lado C#/Razor.
- Spec de referencia: `docs/superpowers/specs/2026-08-20-reglas-distribucion-venta-marca-sucursal-design.md`.

---

### Task 1: Vista `vw_VentaBaseDistribucion` — 4 ramas nuevas por marca

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql`
- Reference (para copiar la definición vigente): `Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/vw_VentaBaseDistribucion_SucursalTprSoloEcom.sql` (última versión de la vista antes de este cambio)

**Interfaces:**
- Consumes: nada (script SQL independiente).
- Produces: `dbo.vw_VentaBaseDistribucion` con 4 valores nuevos de `TipoBase` (`VENTA_SUCURSAL_CONVERSE`, `VENTA_SUCURSAL_UMBRO`, `VENTA_SUCURSAL_FILA`, `VENTA_SUCURSAL_SM`), cada uno con columnas `AnioMes, TipoBase, CodDimension, NombreDimension, MontoVenta, PorcentajeParticipacion` — mismo shape que las ramas existentes. Task 2 depende de que estos 4 nombres de `TipoBase` existan en la vista para poder cascadearlos en el SP.

- [ ] **Step 1: Crear el script con la vista completa (ramas existentes + 4 nuevas)**

Crear `Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql`:

```sql
-- Agrega 4 bases de distribución nuevas, una por marca: 'VENTA_SUCURSAL_CONVERSE',
-- 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM'. A diferencia de
-- 'SUCURSAL_TPR_ECM'/'SUCURSAL_TPR_SOLO_ECOM' (que restringen a un subconjunto de
-- sucursales/canales), estas 4 participan con TODAS las sucursales -- lo que cambia
-- es que el % de participación de cada sucursal se calcula sobre la venta de UNA
-- cuenta contable puntual (la marca), no sobre la venta total. Por eso cada rama
-- nueva parte de Staging_CentralizacionContable directo con su propio filtro
-- NroCuenta, en vez de partir del CTE VentaConCanalCorregido (que ya viene agregado
-- sobre toda la venta del mes, sin filtro de cuenta). Si una sucursal no tuvo venta
-- de esa cuenta en el mes, no aparece como fila en esa rama -- una regla con esa
-- base no le asigna nada a esa sucursal ese mes (mismo comportamiento que ya tienen
-- SUCURSAL_TPR_ECM/SUCURSAL_TPR_SOLO_ECOM para sucursales fuera del subconjunto).
-- No toca las ramas CANAL/SUCURSAL/SUCURSAL_TPR_ECM/SUCURSAL_TPR_SOLO_ECOM existentes.
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
GROUP BY AnioMes, CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_CONVERSE' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-04'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_UMBRO' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-05'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_FILA' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-06'
GROUP BY s.AnioMes, s.CodSucursal

UNION ALL

SELECT s.AnioMes, 'VENTA_SUCURSAL_SM' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-09'
GROUP BY s.AnioMes, s.CodSucursal;
```

- [ ] **Step 2: Verificación sintáctica manual (no hay motor de test contra CLDEPORFIN)**

No existe conexión de test automatizada a la base externa CLDEPORFIN desde este repo. Verificar manualmente releyendo el script creado:
1. Las 4 ramas nuevas tienen exactamente las mismas 6 columnas, en el mismo orden, que las ramas existentes (`AnioMes, TipoBase, CodDimension, NombreDimension, MontoVenta, PorcentajeParticipacion`) — requisito de un `UNION ALL` válido en SQL Server.
2. Las 4 ramas existentes (`CANAL`, `SUCURSAL`, `SUCURSAL_TPR_ECM`, `SUCURSAL_TPR_SOLO_ECOM`) quedaron copiadas sin ningún cambio de texto respecto al archivo de referencia (`vw_VentaBaseDistribucion_SucursalTprSoloEcom.sql`).
3. Cada una de las 4 ramas nuevas usa el `NroCuenta` correcto según la tabla de Global Constraints (Converse=04, Umbro=05, Fila=06, SM=09) y un `TipoBase` distinto.

- [ ] **Step 3: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql"
git commit -m "feat: agregar bases de distribución venta por marca (Converse/Umbro/Fila/SM) a vw_VentaBaseDistribucion"
```

---

### Task 2: SP `sp_EjecutarDistribucionAutomatica` — cascada de sucursal para las 4 bases nuevas

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql`
- Reference (versión vigente a copiar): `Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql`

**Interfaces:**
- Consumes: los 4 `TipoBase` nuevos producidos por Task 1 en `dbo.vw_VentaBaseDistribucion`.
- Produces: `dbo.sp_EjecutarDistribucionAutomatica` actualizado — sin cambio de firma (`@AnioMes CHAR(6)`), sin cambio de columnas de salida. Task 3 no depende de este SP directamente (solo de la opción del dropdown), pero una regla creada con una de las 4 bases nuevas solo cascadea centro de costo/canal correctamente si este SP está desplegado.

- [ ] **Step 1: Crear el script, copia del SP vigente con los 3 `IN (...)` extendidos**

Crear `Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql` copiando el contenido íntegro de `sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql` y reemplazando las 3 apariciones de:

```sql
IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')
```

por:

```sql
IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
    'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
```

Agregar como comentario inicial del archivo (mismo estilo que los scripts anteriores de este SP):

```sql
-- Extiende la cascada Sucursal->Canal->Centro de costo para que también aplique a
-- las 4 bases nuevas 'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO',
-- 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM' (ver
-- Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql) -- misma lógica de reparto
-- por sucursal que 'SUCURSAL', solo que el % de participación de cada sucursal se
-- calculó sobre la venta de una cuenta puntual en vez de la venta total. Único
-- cambio respecto a la versión anterior
-- (Sql/sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql): las 3 apariciones de
-- "v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')" pasan a
-- incluir también las 4 bases nuevas.
```

El resultado completo del archivo (comentario + `CREATE OR ALTER PROCEDURE`) es:

```sql
-- Extiende la cascada Sucursal->Canal->Centro de costo para que también aplique a
-- las 4 bases nuevas 'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO',
-- 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM' (ver
-- Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql) -- misma lógica de reparto
-- por sucursal que 'SUCURSAL', solo que el % de participación de cada sucursal se
-- calculó sobre la venta de una cuenta puntual en vez de la venta total. Único
-- cambio respecto a la versión anterior
-- (Sql/sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql): las 3 apariciones de
-- "v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')" pasan a
-- incluir también las 4 bases nuevas.
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
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                     THEN ISNULL(msuc.CodCentroCosto, d.CodCentroCostoOriginal) ELSE d.CodCentroCostoOriginal END AS CodCentroCostoDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                     THEN ISNULL(msuc.CentroCosto, d.CentroCostoOriginal) ELSE d.CentroCostoOriginal END AS CentroCostoDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.CodDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                         'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                         THEN ISNULL(msuc.CodCanal, d.CodCanalOriginal)
                    ELSE d.CodCanalOriginal
                END AS CodCanalDestino,
                CASE
                    WHEN v.TipoBase = 'CANAL' THEN v.NombreDimension
                    WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                         'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                         THEN ISNULL(msuc.Canal, d.CanalOriginal)
                    ELSE d.CanalOriginal
                END AS CanalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                     THEN v.CodDimension ELSE d.CodSucursalOriginal END AS CodSucursalDestino,
                CASE WHEN v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                          'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
                     THEN v.NombreDimension ELSE d.SucursalOriginal END AS SucursalDestino,
                ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2) AS MontoRedondeado,
                lr.ReglaId,
                ROW_NUMBER() OVER (PARTITION BY d.Id ORDER BY v.PorcentajeParticipacion DESC, v.CodDimension) AS Orden,
                SUM(ROUND(d.MontoOriginal * v.PorcentajeParticipacion, 2)) OVER (PARTITION BY d.Id) AS TotalRedondeado
            FROM LineasConRegla lr
            INNER JOIN dbo.Distribucion_Final d ON d.Id = lr.DistribucionId
            INNER JOIN dbo.vw_VentaBaseDistribucion v
                ON v.AnioMes = @AnioMes AND v.TipoBase = lr.BaseDistribucion
            LEFT JOIN dbo.Maestro_Sucursal msuc
                ON msuc.CodSucursal = v.CodDimension AND msuc.Activo = 1
                AND v.TipoBase IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
                                    'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
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
```

- [ ] **Step 2: Verificación manual del diff contra la versión de referencia**

No hay motor de test contra CLDEPORFIN. Verificar manualmente comparando contra
`sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql`:
1. El único texto distinto son las listas `IN (...)` (ahora 4: en `CodCentroCostoDestino`,
   `CentroCostoDestino`, `CodCanalDestino`/`CanalDestino` combinados en su propio `CASE`,
   `CodSucursalDestino`/`SucursalDestino`, y el `LEFT JOIN ... msuc`) — deben ser
   exactamente las mismas 4 nuevas bases en las 5 apariciones, sin typos.
2. Los pasos 1, 2, 4 y 5 del procedimiento quedaron idénticos carácter por carácter
   a la versión de referencia.
3. La firma (`@AnioMes CHAR(6)`) y el nombre del procedimiento no cambiaron.

- [ ] **Step 3: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql"
git commit -m "feat: cascadear sucursal->canal->centro de costo para las bases venta por marca en sp_EjecutarDistribucionAutomatica"
```

---

### Task 3: Dropdown de `BaseDistribucion` en `Reglas/Form.cshtml`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Pages/GestionGastos/Reglas/Form.cshtml` (líneas 27-31, dentro del `<select asp-for="Regla.BaseDistribucion">`)

**Interfaces:**
- Consumes: los 4 códigos de `TipoBase`/`BaseDistribucion` definidos en Global Constraints (deben coincidir carácter por carácter con los usados en Task 1/Task 2, porque el match entre `Reglas_Distribucion.BaseDistribucion` y `vw_VentaBaseDistribucion.TipoBase` es un `=` de texto en el SP).
- Produces: nada consumido por otra tarea — es la última tarea del plan.

- [ ] **Step 1: Confirmar el bloque actual del `<select>`**

El bloque actual (líneas 27-32) es:

```html
<select asp-for="Regla.BaseDistribucion" style="width:100%">
    <option value="CANAL">Canal</option>
    <option value="SUCURSAL">Sucursal</option>
    <option value="SUCURSAL_TPR_ECM">Sucursal (solo TPR y ECM)</option>
    <option value="SUCURSAL_TPR_SOLO_ECOM">Sucursal (TPR y solo Ecommerce propio, sin marketplace)</option>
</select>
```

- [ ] **Step 2: Agregar las 4 opciones nuevas, después de `SUCURSAL_TPR_SOLO_ECOM`**

Editar para dejar:

```html
<select asp-for="Regla.BaseDistribucion" style="width:100%">
    <option value="CANAL">Canal</option>
    <option value="SUCURSAL">Sucursal</option>
    <option value="SUCURSAL_TPR_ECM">Sucursal (solo TPR y ECM)</option>
    <option value="SUCURSAL_TPR_SOLO_ECOM">Sucursal (TPR y solo Ecommerce propio, sin marketplace)</option>
    <option value="VENTA_SUCURSAL_CONVERSE">Sucursal (venta Converse 41-01-001-04)</option>
    <option value="VENTA_SUCURSAL_UMBRO">Sucursal (venta Umbro 41-01-001-05)</option>
    <option value="VENTA_SUCURSAL_FILA">Sucursal (venta Fila 41-01-001-06)</option>
    <option value="VENTA_SUCURSAL_SM">Sucursal (venta SM 41-01-001-09)</option>
</select>
```

No se toca ningún otro `<option>` ni atributo del `<select>`.

- [ ] **Step 3: Verificar que el proyecto compila**

El módulo referencia `Portal SaaS - Core` por ruta relativa; si esa carpeta no está
disponible como junction en el entorno de ejecución, usar `-t:CoreCompile` para
verificar solo la compilación C#/Razor sin el paso de copia a `dist/` (que puede
fallar por lock de archivo si `PortalSaas.Host` está corriendo — no es un error de
código, ver notas de la sesión anterior sobre este mismo módulo).

Run:
```bash
dotnet build "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Modulo.GestionDistribucionGastos.csproj"
```
Expected: `Build succeeded. 0 Error(s)` (warnings preexistentes, si los hay, no son de este cambio).

Si falla solo en el target `PublicarComoPlugin` (copia a `dist/`) por archivo
bloqueado, correr en su lugar:
```bash
dotnet msbuild "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Modulo.GestionDistribucionGastos.csproj" -t:CoreCompile -restore
```
Expected: termina sin errores (confirma que el cambio de Razor compila; el error de
copia a `dist/` es un problema de entorno ajeno a este cambio).

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/Pages/GestionGastos/Reglas/Form.cshtml"
git commit -m "feat: agregar bases de distribución venta por marca al dropdown de Reglas de distribución"
```

---

## Rollout Post-Implementación

No es parte de las tareas de código (nada que un subagente pueda ejecutar contra la
base real), pero queda documentado para el usuario/DBA después de mergear:

1. Ejecutar `Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql` contra CLDEPORFIN.
2. Ejecutar `Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql` contra CLDEPORFIN.
3. Crear las reglas de distribución concretas desde `/gestiongastos/reglas`, eligiendo
   la cuenta de gasto a repartir y la base nueva correspondiente a la marca.
