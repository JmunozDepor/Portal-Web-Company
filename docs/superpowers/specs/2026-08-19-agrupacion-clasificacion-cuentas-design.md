# Agrupación y Clasificación de Cuentas (nivel reporte) — Diseño

## Contexto

El módulo `Modulo.GestionDistribucionGastos` alimenta hoy `CLDEPORFIN..vw_EerrAnual`, una vista SQL Server de solo lectura usada como fuente única para reportes EERR fuera de la app (Excel/SQL directo). Esa vista agrupa las cuentas únicamente por el primer dígito de `NroCuenta` (Grupo 4-9, ver `Sql/vw_EerrAnual.sql`), sin ningún concepto de categoría de negocio configurable.

Se requiere agregar, **solo a nivel de reporte** (no afecta la lógica de distribución, reglas ni el motor automático/manual existente), la posibilidad de clasificar cada cuenta en una categoría de negocio libre, definida por el usuario, y exponerla como columna adicional en `vw_EerrAnual`.

## Alcance

- Nuevo catálogo de clasificaciones (mantenedor CRUD).
- Nueva tabla de agrupación que asocia cada cuenta real del sistema con una clasificación del catálogo.
- Extensión de `vw_EerrAnual` con las columnas de clasificación, sin alterar columnas ni lógica existentes.
- Dos páginas nuevas en el módulo, siguiendo el patrón visual/estructural de `Pages/GestionGastos/Reglas`.

Fuera de alcance: cualquier cambio a la pantalla EERR (`Pages/GestionGastos/Eerr`), a `Distribucion_Final`, `Staging_CentralizacionContable` o al motor de reglas/distribución.

## Modelo de datos

### `dbo.Clasificacion_Cuenta` (catálogo, mantenedor independiente)

```sql
CREATE TABLE dbo.Clasificacion_Cuenta (
    Id     INT IDENTITY PRIMARY KEY,
    Codigo VARCHAR(20)  NOT NULL UNIQUE,
    Nombre VARCHAR(100) NOT NULL,
    Orden  INT NOT NULL DEFAULT 0,
    Activo BIT NOT NULL DEFAULT 1
);
```

Categorías de negocio libres (no atadas a Ventas/Costo/Gasto ni a ningún esquema contable fijo). `Orden` define el orden de presentación en reportes; `Activo` permite deshabilitar sin borrar (igual criterio que `Reglas_Distribucion.Activo`).

### `dbo.Agrupacion_Cuenta` (todas las cuentas + clasificación asignada)

```sql
CREATE TABLE dbo.Agrupacion_Cuenta (
    NroCuenta            VARCHAR(50)  NOT NULL PRIMARY KEY,
    NombreCuenta         VARCHAR(200) NULL,
    ClasificacionId      INT NULL REFERENCES dbo.Clasificacion_Cuenta(Id),
    FechaModificacion    DATETIME NOT NULL DEFAULT GETDATE(),
    UsuarioModificacion  VARCHAR(100) NULL
);
```

`ClasificacionId` es nullable: una cuenta puede existir sin clasificación asignada todavía (columnas de reporte salen NULL en ese caso).

### Sincronización del universo de cuentas

`Agrupacion_Cuenta` no se carga manualmente ni por importación: se sincroniza contra el universo real de cuentas que ya existe en el sistema, con el mismo criterio que usa `ContarCuentasQueMatchean` en `Pages/GestionGastos/Reglas/Index.cshtml.cs` (`Staging_CentralizacionContable` con `TipoRegistro = 'DETALLE'`, más `NroCuenta`/`NombreCuenta` disponibles en `Distribucion_Final`).

La sincronización corre automáticamente cada vez que se entra a la página "Agrupación de Cuentas" (`OnGetAsync`), vía `MERGE`:
- Inserta cuentas nuevas encontradas en el universo real que aún no estén en `Agrupacion_Cuenta` (con `ClasificacionId = NULL`).
- No modifica ni borra filas existentes (una cuenta con clasificación ya asignada no se toca, y una cuenta que dejó de aparecer en el universo real no se elimina — se conserva su clasificación histórica).

## Cambios a `vw_EerrAnual`

Se agrega un `LEFT JOIN` sobre el `SELECT` final existente:

```sql
LEFT JOIN dbo.Agrupacion_Cuenta ac ON ac.NroCuenta = Datos.NroCuenta
LEFT JOIN dbo.Clasificacion_Cuenta cc ON cc.Id = ac.ClasificacionId
```

Columnas nuevas expuestas: `CodClasificacion` (`cc.Codigo`), `Clasificacion` (`cc.Nombre`). Todas las columnas y la lógica de `Grupo`/`NombreGrupo`/`Resultado` existentes quedan intactas. Cuentas sin fila en `Agrupacion_Cuenta` o sin `ClasificacionId` asignado devuelven `NULL` en ambas columnas nuevas.

## Páginas nuevas (módulo `Modulo.GestionDistribucionGastos`)

Mismo patrón estructural que `Pages/GestionGastos/Reglas` (Index + Form, `PageModelBaseGestionGastos`, mensajes de éxito/error vía `MensajeExito`/`MensajeError`).

### `Pages/GestionGastos/Clasificaciones/Index.cshtml` + `Form.cshtml`

CRUD simple del catálogo: crear, editar (Código, Nombre, Orden), activar/desactivar (mismo patrón `OnPostToggleActivoAsync` de Reglas). No se permite eliminar una clasificación que ya esté referenciada por alguna fila de `Agrupacion_Cuenta` (para no dejar `ClasificacionId` huérfano) — solo desactivar.

### `Pages/GestionGastos/AgrupacionCuentas/Index.cshtml`

- `OnGetAsync`: ejecuta el `MERGE` de sincronización descrito arriba, luego lista todas las cuentas de `Agrupacion_Cuenta` (ordenadas por `NroCuenta`), con un dropdown de clasificación por fila (poblado desde `Clasificacion_Cuenta` donde `Activo = 1`, más la clasificación actual de la fila aunque esté inactiva, para no perderla de la vista).
- `OnPostAsignarAsync(string nroCuenta, int? clasificacionId)`: actualiza `ClasificacionId`, `FechaModificacion`, `UsuarioModificacion` de una fila puntual y vuelve a la página (guardado por fila, sin formulario batch).
- Filtro simple por texto (cuenta sin clasificar / cuenta o nombre) para facilitar encontrar cuentas pendientes de clasificar, siguiendo el espíritu de `CuentasPendientes`.

## Modelos y DbContext

Dos entidades nuevas en `Models/`: `ClasificacionCuenta.cs`, `AgrupacionCuenta.cs`, con sus atributos `[Table(...)]` correspondientes. Se agregan como `DbSet` en `Data/ApplicationDbContext.cs`, con `HasKey` para `AgrupacionCuenta` (`NroCuenta`) y relación opcional hacia `ClasificacionCuenta`.

## Scripts SQL

Siguiendo el patrón de `Sql/Crear_Cuenta_Aprobada.sql` (ejecución manual, una sola vez, contra la base externa CLDEPORFIN — el proyecto no usa migraciones EF Core para estas tablas de reporte):

- `Sql/Crear_Clasificacion_Cuenta.sql`
- `Sql/Crear_Agrupacion_Cuenta.sql`
- Actualización de `Sql/vw_EerrAnual.sql` (mismo archivo, `CREATE OR ALTER VIEW`, agregando los dos `LEFT JOIN` y las dos columnas nuevas).

## Testing

- Verificación manual: crear clasificaciones, sincronizar `Agrupacion_Cuenta`, asignar clasificación a un subconjunto de cuentas, y confirmar en SQL directo que `vw_EerrAnual` devuelve `CodClasificacion`/`Clasificacion` correctos (y `NULL` para cuentas sin asignar).
- Confirmar que el `MERGE` no borra ni sobreescribe clasificaciones ya asignadas al repetir la sincronización.
- Confirmar que desactivar (no eliminar) una clasificación referenciada no rompe filas existentes de `Agrupacion_Cuenta`, y que intentar eliminarla se bloquea con mensaje de error.
