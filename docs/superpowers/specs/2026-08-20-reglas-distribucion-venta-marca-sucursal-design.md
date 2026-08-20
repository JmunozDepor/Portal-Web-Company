# Diseño: nuevas bases de distribución "venta por marca, por sucursal"

**Fecha:** 2026-08-20
**Módulo:** `Modulo.GestionDistribucionGastos`

## Contexto

El módulo ya soporta reglas de distribución (`Reglas_Distribucion`) que reparten un
gasto entre canales o sucursales según distintas "bases de distribución"
(`BaseDistribucion`), cada una una fila en `vw_VentaBaseDistribucion` con el
`TipoBase`, `CodDimension` (sucursal o canal) y `PorcentajeParticipacion` de esa
dimensión sobre el total del mes. Las bases existentes (`CANAL`, `SUCURSAL`,
`SUCURSAL_TPR_ECM`, `SUCURSAL_TPR_SOLO_ECOM`) calculan el % de participación sobre
la **venta total** (o un subconjunto de sucursales/canales), agregando
`Staging_CentralizacionContable` con `TipoRegistro = 'RESUMEN'`.

## Necesidad

Se necesitan 4 bases de distribución nuevas, todas "por sucursal" (participan
TODAS las sucursales, sin subconjunto), pero el % de participación de cada
sucursal se calcula sobre la venta de **una cuenta contable específica** (una
marca), no sobre la venta total:

| Base (código)              | Cuenta de venta   | Marca    |
|-----------------------------|-------------------|----------|
| `VENTA_SUCURSAL_CONVERSE`   | 41-01-001-04       | Converse |
| `VENTA_SUCURSAL_UMBRO`      | 41-01-001-05       | Umbro    |
| `VENTA_SUCURSAL_FILA`       | 41-01-001-06       | Fila     |
| `VENTA_SUCURSAL_SM`         | 41-01-001-09       | SM       |

Uso: una regla con esta base sirve para repartir un gasto (ej. marketing o comisión
de una marca) entre sucursales en proporción a cuánto vendió cada sucursal de esa
marca puntual ese mes — no a su venta total.

## Diseño

Mismo patrón exacto que las bases `SUCURSAL_TPR_ECM`/`SUCURSAL_TPR_SOLO_ECOM`
(agregar una rama al `UNION ALL` de la vista + agregar el `TipoBase` nuevo a los
`IN (...)` de cascada de sucursal→centro de costo/canal del SP), con la única
diferencia de que el filtro es por `NroCuenta` en vez de por canal/sucursal.

### 1. `vw_VentaBaseDistribucion`

Nuevo script `Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql`
(`CREATE OR ALTER VIEW`, reemplaza la definición completa vigente — mismo patrón
que los scripts anteriores de esta vista). Agrega 4 ramas idénticas entre sí
salvo por `TipoBase` y `NroCuenta`:

```sql
UNION ALL
SELECT s.AnioMes, 'VENTA_SUCURSAL_CONVERSE' AS TipoBase, s.CodSucursal AS CodDimension,
       MAX(ISNULL(m.Sucursal, s.Sucursal)) AS NombreDimension,
       ABS(SUM(s.MontoNeto)) AS MontoVenta,
       ABS(SUM(s.MontoNeto)) * 1.0 / NULLIF(SUM(ABS(SUM(s.MontoNeto))) OVER (PARTITION BY s.AnioMes), 0) AS PorcentajeParticipacion
FROM dbo.Staging_CentralizacionContable s
LEFT JOIN dbo.Maestro_Sucursal m ON m.CodSucursal = s.CodSucursal AND m.Activo = 1
WHERE s.TipoRegistro = 'RESUMEN' AND s.NroCuenta = '41-01-001-04'
GROUP BY s.AnioMes, s.CodSucursal
```

Repetido para `VENTA_SUCURSAL_UMBRO` (41-01-001-05), `VENTA_SUCURSAL_FILA`
(41-01-001-06) y `VENTA_SUCURSAL_SM` (41-01-001-09).

Nota: a diferencia de la rama `SUCURSAL` (que parte del CTE `VentaConCanalCorregido`,
ya agregado sobre toda la venta del mes), estas ramas parten de
`Staging_CentralizacionContable` directo con su propio filtro `NroCuenta`, porque
necesitan agregar solo la venta de una cuenta puntual, no toda. El nombre de
sucursal se corrige igual vía `Maestro_Sucursal` para mantener consistencia con
las demás ramas.

Si una sucursal no tuvo venta de esa cuenta en el mes, simplemente no aparece como
fila en esa rama — una regla con esa base no le asignaría nada a esa sucursal ese
mes (mismo comportamiento ya existente en `SUCURSAL_TPR_ECM`/`SUCURSAL_TPR_SOLO_ECOM`
para sucursales sin venta en el subconjunto filtrado).

### 2. `sp_EjecutarDistribucionAutomatica`

Nuevo script `Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql`
(`CREATE OR ALTER PROCEDURE`), copia exacta de la versión vigente
(`Sql/sp_EjecutarDistribucionAutomatica_AjusteRedondeo.sql`) con el único cambio de
agregar las 4 bases nuevas a los tres `IN (...)` que hoy dicen
`('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM')`:

```sql
IN ('SUCURSAL', 'SUCURSAL_TPR_ECM', 'SUCURSAL_TPR_SOLO_ECOM',
    'VENTA_SUCURSAL_CONVERSE', 'VENTA_SUCURSAL_UMBRO', 'VENTA_SUCURSAL_FILA', 'VENTA_SUCURSAL_SM')
```

Esto asegura que estas 4 bases nuevas también cascadeen centro de costo/canal desde
`Maestro_Sucursal` según la sucursal destino, igual que cualquier otra base "por
sucursal".

### 3. `Form.cshtml` (Reglas)

Agrega 4 opciones al `<select asp-for="Regla.BaseDistribucion">`, después de las
existentes:

```html
<option value="VENTA_SUCURSAL_CONVERSE">Sucursal (venta Converse 41-01-001-04)</option>
<option value="VENTA_SUCURSAL_UMBRO">Sucursal (venta Umbro 41-01-001-05)</option>
<option value="VENTA_SUCURSAL_FILA">Sucursal (venta Fila 41-01-001-06)</option>
<option value="VENTA_SUCURSAL_SM">Sucursal (venta SM 41-01-001-09)</option>
```

## Fuera de alcance / no requiere cambios

- **`Models/ReglaDistribucion.cs`**: `BaseDistribucion` ya es `string` libre, sin
  enum ni validación de valores permitidos.
- **Esquema de `Reglas_Distribucion`**: `BaseDistribucion VARCHAR(50)` ya alcanza
  (el código más largo, `VENTA_SUCURSAL_CONVERSE`, tiene 24 caracteres).
- **`Reglas/Index.cshtml.cs` / `FormModel.HayConflicto`**: el chequeo de conflicto
  entre reglas activas es por `NroCuenta`/`CodCentroCosto` de la regla, no por
  `BaseDistribucion` — no cambia con bases nuevas.
- **Motor de aplicación (`AplicarSiCorresponde`/toggle/eliminar)**: ya es genérico
  respecto a la base, no requiere tocarlo.

## Rollout

Después de implementar, quedan pendientes para el usuario/DBA (el módulo no corre
DDL automáticamente al iniciar):
1. Ejecutar `Sql/vw_VentaBaseDistribucion_VentaSucursalMarcas.sql` contra
   `CLDEPORFIN`.
2. Ejecutar `Sql/sp_EjecutarDistribucionAutomatica_VentaSucursalMarcas.sql` contra
   `CLDEPORFIN`.
3. Crear las reglas de distribución concretas desde la UI
   (`/gestiongastos/reglas`), eligiendo la cuenta de gasto a repartir y la base
   nueva correspondiente a la marca.
