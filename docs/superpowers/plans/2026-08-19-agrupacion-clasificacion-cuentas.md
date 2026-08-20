# Agrupación y Clasificación de Cuentas (nivel reporte) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar un mantenedor de clasificaciones de negocio y una tabla de agrupación que asigna esa clasificación a cada cuenta contable, exponiéndola como columna adicional en `CLDEPORFIN..vw_EerrAnual`, sin tocar la lógica de distribución/reglas existente.

**Architecture:** Dos tablas nuevas en la base externa SQL Server (CLDEPORFIN): `Clasificacion_Cuenta` (catálogo) y `Agrupacion_Cuenta` (cuenta → clasificación, auto-sincronizada con el universo real de cuentas vía `MERGE`). Dos entidades EF Core nuevas en `Modulo.GestionDistribucionGastos`, agregadas al `ApplicationDbContext` existente. Dos páginas Razor Pages nuevas siguiendo el patrón exacto de `Pages/GestionGastos/Reglas` (Index+Form para el catálogo, Index con guardado por fila para la agrupación). `vw_EerrAnual` se actualiza con dos `LEFT JOIN` adicionales.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core (SQL Server provider), T-SQL puro (sin migraciones EF para estas tablas de reporte — mismo patrón que `Cuenta_Aprobada`).

## Global Constraints

- No modificar `Distribucion_Final`, `Staging_CentralizacionContable`, el motor de reglas/distribución, ni las columnas/lógica existentes de `vw_EerrAnual` (`Grupo`, `NombreGrupo`, `Resultado`, montos). Ver spec: alcance solo de reporte.
- Seguir el patrón estructural exacto de `Pages/GestionGastos/Reglas` (Index.cshtml/Form.cshtml + `PageModelBaseGestionGastos`, mensajes vía `MensajeExito`/`MensajeError` TempData, clases CSS `.card`, `.grid`, `.toolbar`, `.btn-primary`, `.badge`).
- Scripts SQL van en `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Sql\`, ejecución manual una sola vez contra CLDEPORFIN (no hay migraciones EF Core en este módulo).
- No existe proyecto de tests automatizados en este módulo ni en el repo para esta capa (Razor Pages + SQL Server externo real) — la verificación de cada tarea es manual: compilar, y cuando aplique, ejecutar el script SQL contra una base de desarrollo/staging y revisar el resultado con una query directa.
- Todas las páginas nuevas requieren `[Authorize]` (heredado de `PageModelBaseGestionGastos`) y deben agregarse a `Pages/Shared/_NavInterna.cshtml`.
- Idioma de UI, mensajes y comentarios: español, igual que el resto del módulo.

---

## Task 1: Scripts SQL — Clasificacion_Cuenta, Agrupacion_Cuenta y actualización de vw_EerrAnual

**Files:**
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Sql\Crear_Clasificacion_Cuenta.sql`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Sql\Crear_Agrupacion_Cuenta.sql`
- Modify: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Sql\vw_EerrAnual.sql`

**Interfaces:**
- Produces: tablas `dbo.Clasificacion_Cuenta(Id, Codigo, Nombre, Orden, Activo)` y `dbo.Agrupacion_Cuenta(NroCuenta, NombreCuenta, ClasificacionId, FechaModificacion, UsuarioModificacion)`; columnas nuevas `CodClasificacion`, `Clasificacion` en `vw_EerrAnual`. Estos nombres son los que consumen las Tasks 2-5.

- [ ] **Step 1: Crear el script de la tabla de catálogo**

```sql
-- dbo/Sql/Crear_Clasificacion_Cuenta.sql
-- Catálogo de clasificaciones de negocio libres para reportes EERR (solo a nivel de
-- reporte, no afecta distribución). Ejecutar una sola vez contra la base externa
-- (CLDEPORFIN).
CREATE TABLE dbo.Clasificacion_Cuenta (
    Id     INT IDENTITY PRIMARY KEY,
    Codigo VARCHAR(20)  NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Orden  INT NOT NULL DEFAULT 0,
    Activo BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Clasificacion_Cuenta_Codigo UNIQUE (Codigo)
);
```

- [ ] **Step 2: Crear el script de la tabla de agrupación**

```sql
-- dbo/Sql/Crear_Agrupacion_Cuenta.sql
-- Todas las cuentas del sistema, con la clasificación de negocio asignada (si la
-- tiene). Se sincroniza automáticamente desde la pantalla "Agrupación de Cuentas"
-- (MERGE, ver AgrupacionCuentas/Index.cshtml.cs) -- no se carga a mano. Ejecutar una
-- sola vez contra la base externa (CLDEPORFIN).
CREATE TABLE dbo.Agrupacion_Cuenta (
    NroCuenta            VARCHAR(50)  NOT NULL PRIMARY KEY,
    NombreCuenta         VARCHAR(200) NULL,
    ClasificacionId      INT NULL,
    FechaModificacion    DATETIME NOT NULL DEFAULT GETDATE(),
    UsuarioModificacion  VARCHAR(100) NULL,
    CONSTRAINT FK_Agrupacion_Cuenta_Clasificacion FOREIGN KEY (ClasificacionId)
        REFERENCES dbo.Clasificacion_Cuenta(Id)
);
```

- [ ] **Step 3: Actualizar vw_EerrAnual con los LEFT JOIN y columnas nuevas**

Editar `Sql/vw_EerrAnual.sql`: agregar los dos `LEFT JOIN` y las dos columnas nuevas al `SELECT` final, sin tocar nada más del archivo (mismo `WITH Datos AS (...)` intacto). El archivo completo queda así:

```sql
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
    Datos.Anio, Datos.Mes, Datos.AnioMes, Datos.Grupo,
    NombreGrupo = CASE Datos.Grupo
        WHEN '4' THEN '4 - INGRESOS' WHEN '5' THEN '5 - COSTO DE VENTAS'
        WHEN '6' THEN '6 - GASTOS OPERACIONALES' WHEN '7' THEN '7 - OTROS INGRESOS Y EGRESOS'
        WHEN '8' THEN '8 - OTROS GASTOS' WHEN '9' THEN '9 - IMPUESTOS' END,
    Datos.NroCuenta, Datos.NombreCuenta,
    Datos.CodCentroCosto, Datos.CentroCosto, Datos.CodCanal, Datos.Canal, Datos.CodSucursal, Datos.Sucursal,
    Datos.TipoOrigen, Datos.EsGastoIndirecto,
    -- Para poder filtrar/agrupar el pivot igual que la pantalla EERR: todo lo que no es gasto
    -- indirecto (ingresos, costo de ventas, gastos directos) queda dentro de Res.Operacional-1;
    -- el gasto indirecto es lo único que recién se resta para llegar a Res.Operacional-2.
    Resultado = IIF(Datos.EsGastoIndirecto = 1, 'Res.Operacional-2', 'Res.Operacional-1'),
    cc.Codigo AS CodClasificacion,
    cc.Nombre AS Clasificacion,
    SUM(Datos.Monto) AS Monto
FROM Datos
LEFT JOIN dbo.Agrupacion_Cuenta ac ON ac.NroCuenta = Datos.NroCuenta
LEFT JOIN dbo.Clasificacion_Cuenta cc ON cc.Id = ac.ClasificacionId
WHERE Datos.Grupo IN ('4', '5', '6', '7', '8', '9')
GROUP BY Datos.Anio, Datos.Mes, Datos.AnioMes, Datos.Grupo, Datos.NroCuenta, Datos.NombreCuenta,
         Datos.CodCentroCosto, Datos.CentroCosto, Datos.CodCanal, Datos.Canal, Datos.CodSucursal, Datos.Sucursal,
         Datos.TipoOrigen, Datos.EsGastoIndirecto, cc.Codigo, cc.Nombre;
```

- [ ] **Step 4: Verificación manual (contra base de desarrollo/staging, no producción)**

Ejecutar los 3 scripts en orden (`Crear_Clasificacion_Cuenta.sql`, `Crear_Agrupacion_Cuenta.sql`, `vw_EerrAnual.sql`) contra una base de desarrollo. Confirmar con:
```sql
SELECT TOP 5 * FROM dbo.vw_EerrAnual;
```
que la vista sigue devolviendo filas y que `CodClasificacion`/`Clasificacion` salen `NULL` (todavía no hay filas en `Agrupacion_Cuenta`).

- [ ] **Step 5: Commit**

No hay repositorio git en este proyecto (confirmado: "Is a git repository: false"). Omitir este paso — continuar a la Task 2.

---

## Task 2: Modelos EF Core y DbContext

**Files:**
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Models\ClasificacionCuenta.cs`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Models\AgrupacionCuenta.cs`
- Modify: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Data\ApplicationDbContext.cs`

**Interfaces:**
- Consumes: nada de tasks previas (usa solo EF Core, ya referenciado en el proyecto).
- Produces: `ClasificacionCuenta { int Id; string Codigo; string Nombre; int Orden; bool Activo }`, `AgrupacionCuenta { string NroCuenta; string? NombreCuenta; int? ClasificacionId; DateTime FechaModificacion; string? UsuarioModificacion }`, `ApplicationDbContext.ClasificacionCuenta` (`DbSet<ClasificacionCuenta>`), `ApplicationDbContext.AgrupacionCuenta` (`DbSet<AgrupacionCuenta>`). Estos son los tipos y nombres que consumen las Tasks 3, 4 y 5.

- [ ] **Step 1: Crear la entidad ClasificacionCuenta**

```csharp
// Models/ClasificacionCuenta.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Clasificacion_Cuenta")]
public class ClasificacionCuenta
{
    public int Id { get; set; }

    [Required, Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required, Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Display(Name = "Orden")]
    public int Orden { get; set; }

    public bool Activo { get; set; } = true;
}
```

- [ ] **Step 2: Crear la entidad AgrupacionCuenta**

```csharp
// Models/AgrupacionCuenta.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Agrupacion_Cuenta")]
public class AgrupacionCuenta
{
    public string NroCuenta { get; set; } = string.Empty;
    public string? NombreCuenta { get; set; }
    public int? ClasificacionId { get; set; }
    public DateTime FechaModificacion { get; set; } = DateTime.Now;
    public string? UsuarioModificacion { get; set; }
}
```

- [ ] **Step 3: Registrar los DbSet y claves en ApplicationDbContext**

Modificar `Data/ApplicationDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Models;

namespace Modulo.GestionDistribucionGastos.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<StagingCentralizacion> StagingCentralizacion => Set<StagingCentralizacion>();
    public DbSet<DistribucionFinal> DistribucionFinal => Set<DistribucionFinal>();
    public DbSet<ReglaDistribucion> ReglasDistribucion => Set<ReglaDistribucion>();
    public DbSet<CuentaEnTrabajo> CuentaEnTrabajo => Set<CuentaEnTrabajo>();
    public DbSet<CuentaAprobada> CuentaAprobada => Set<CuentaAprobada>();
    public DbSet<CierreMes> CierreMes => Set<CierreMes>();
    public DbSet<MaestroSucursal> MaestroSucursal => Set<MaestroSucursal>();
    public DbSet<ClasificacionCuenta> ClasificacionCuenta => Set<ClasificacionCuenta>();
    public DbSet<AgrupacionCuenta> AgrupacionCuenta => Set<AgrupacionCuenta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CuentaEnTrabajo>()
            .HasKey(c => new { c.AnioMes, c.NroCuenta });

        modelBuilder.Entity<CuentaAprobada>()
            .HasKey(c => new { c.AnioMes, c.NroCuenta });

        modelBuilder.Entity<CierreMes>()
            .HasKey(c => c.AnioMes);

        modelBuilder.Entity<MaestroSucursal>()
            .HasKey(m => m.CodSucursal);

        modelBuilder.Entity<AgrupacionCuenta>()
            .HasKey(a => a.NroCuenta);

        modelBuilder.Entity<AgrupacionCuenta>()
            .HasOne<ClasificacionCuenta>()
            .WithMany()
            .HasForeignKey(a => a.ClasificacionId);

        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 4: Verificar que el proyecto compila**

Run: `dotnet build "Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Modulo.GestionDistribucionGastos.csproj"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Commit**

No hay repositorio git en este proyecto. Omitir — continuar a la Task 3.

---

## Task 3: Mantenedor de Clasificaciones (Index + Form)

**Files:**
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\Clasificaciones\Index.cshtml`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\Clasificaciones\Index.cshtml.cs`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\Clasificaciones\Form.cshtml`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\Clasificaciones\Form.cshtml.cs`
- Modify: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\Shared\_NavInterna.cshtml`

**Interfaces:**
- Consumes: `ApplicationDbContext.ClasificacionCuenta` (Task 2), `PageModelBaseGestionGastos` (`MensajeExito`, `MensajeError`, `ObtenerMensajeError`, `NombreUsuarioActual`), `ICurrentUserContext` (patrón de constructor de `Reglas/Index.cshtml.cs`).
- Produces: rutas `/gestiongastos/clasificaciones` y `/gestiongastos/clasificaciones/form`, handlers `OnPostToggleActivoAsync(int id)`, `OnPostEliminarAsync(int id)` (bloquea si está referenciada), `OnPostGuardarAsync(int? id)` en Form. Estos nombres de ruta y de página (`/GestionGastos/Clasificaciones/Index`, `/GestionGastos/Clasificaciones/Form`) los usa la Task 4 para el enlace desde Agrupación de Cuentas.

- [ ] **Step 1: Crear Index.cshtml.cs (listado + activar/desactivar + eliminar con bloqueo)**

```csharp
// Pages/GestionGastos/Clasificaciones/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public List<ClasificacionCuenta> Clasificaciones { get; set; } = new();

    public async Task OnGetAsync()
    {
        Clasificaciones = await _db.ClasificacionCuenta
            .OrderBy(c => c.Orden)
            .ThenBy(c => c.Nombre)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleActivoAsync(int id)
    {
        var clasificacion = await _db.ClasificacionCuenta.FindAsync(id);
        if (clasificacion == null)
            return RedirectToPage();

        clasificacion.Activo = !clasificacion.Activo;
        await _db.SaveChangesAsync();

        MensajeExito = clasificacion.Activo ? "Clasificación activada." : "Clasificación desactivada.";
        return RedirectToPage();
    }

    // No se permite eliminar una clasificación referenciada por Agrupacion_Cuenta -- dejaría
    // filas con ClasificacionId huérfano. Solo desactivar en ese caso.
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var clasificacion = await _db.ClasificacionCuenta.FindAsync(id);
        if (clasificacion == null)
            return RedirectToPage();

        bool enUso = await _db.AgrupacionCuenta.AnyAsync(a => a.ClasificacionId == id);
        if (enUso)
        {
            MensajeError = $"No se puede eliminar \"{clasificacion.Nombre}\": hay cuentas asignadas a esta clasificación. Desactívala en vez de eliminarla.";
            return RedirectToPage();
        }

        _db.ClasificacionCuenta.Remove(clasificacion);
        await _db.SaveChangesAsync();

        MensajeExito = $"Clasificación \"{clasificacion.Nombre}\" eliminada.";
        return RedirectToPage();
    }
}
```

- [ ] **Step 2: Crear Index.cshtml**

```cshtml
@page "/gestiongastos/clasificaciones"
@model Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones.IndexModel
@section Styles {
    <link rel="stylesheet" href="~/css/gestiongastos.css" />
}
@{ ViewData["Title"] = "Clasificaciones de cuentas"; }

<partial name="_MensajesGestionGastos" model="Model" />
<partial name="_NavInterna" />

<div class="card">
    <div class="toolbar">
        <strong>Clasificaciones de cuentas (categorías de negocio, solo a nivel de reporte)</strong>
        <a asp-page="/GestionGastos/Clasificaciones/Form" class="btn-primary toolbar-right">Nueva clasificación</a>
    </div>

    <table class="grid">
        <thead>
            <tr>
                <th>Código</th>
                <th>Nombre</th>
                <th>Orden</th>
                <th>Estado</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var c in Model.Clasificaciones)
            {
                <tr>
                    <td>@c.Codigo</td>
                    <td>@c.Nombre</td>
                    <td>@c.Orden</td>
                    <td>
                        @if (c.Activo)
                        {
                            <span class="badge badge-success">Activa</span>
                        }
                        else
                        {
                            <span class="badge badge-danger">Inactiva</span>
                        }
                    </td>
                    <td style="display:flex; gap:6px;">
                        <a asp-page="/GestionGastos/Clasificaciones/Form" asp-route-id="@c.Id">Editar</a>
                        <form asp-page-handler="ToggleActivo" asp-route-id="@c.Id" method="post">
                            <button type="submit">@(c.Activo ? "Desactivar" : "Activar")</button>
                        </form>
                        <form asp-page-handler="Eliminar" asp-route-id="@c.Id" method="post"
                              onsubmit="return confirm('¿Eliminar la clasificación @c.Nombre? No se puede deshacer.');">
                            <button type="submit" style="color:var(--danger); border-color:var(--danger);">Eliminar</button>
                        </form>
                    </td>
                </tr>
            }
            @if (!Model.Clasificaciones.Any())
            {
                <tr><td colspan="5">No hay clasificaciones creadas todavía.</td></tr>
            }
        </tbody>
    </table>
</div>
```

- [ ] **Step 3: Crear Form.cshtml.cs (alta/edición combinadas)**

```csharp
// Pages/GestionGastos/Clasificaciones/Form.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones;

public class FormModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public FormModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    [BindProperty]
    public ClasificacionCuenta Clasificacion { get; set; } = new();

    public bool EsNuevo { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EsNuevo = id is null;
        if (id is not null)
        {
            var clasificacion = await _db.ClasificacionCuenta.FindAsync(id.Value);
            if (clasificacion is null)
                return NotFound();
            Clasificacion = clasificacion;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(int? id)
    {
        EsNuevo = id is null;

        if (!ModelState.IsValid)
            return Page();

        bool codigoDuplicado = await _db.ClasificacionCuenta
            .AnyAsync(c => c.Codigo == Clasificacion.Codigo && c.Id != Clasificacion.Id);
        if (codigoDuplicado)
        {
            ModelState.AddModelError(nameof(Clasificacion.Codigo), "Ya existe otra clasificación con este código.");
            return Page();
        }

        try
        {
            if (EsNuevo)
            {
                _db.ClasificacionCuenta.Add(Clasificacion);
                await _db.SaveChangesAsync();
                MensajeExito = "Clasificación creada correctamente.";
            }
            else
            {
                _db.Entry(Clasificacion).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                MensajeExito = "Clasificación actualizada.";
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            return Page();
        }

        return RedirectToPage("/GestionGastos/Clasificaciones/Index");
    }
}
```

- [ ] **Step 4: Crear Form.cshtml**

```cshtml
@page "/gestiongastos/clasificaciones/form"
@model Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones.FormModel
@section Styles {
    <link rel="stylesheet" href="~/css/gestiongastos.css" />
}
@{ ViewData["Title"] = Model.EsNuevo ? "Nueva clasificación" : "Editar clasificación"; }

<partial name="_MensajesGestionGastos" model="Model" />
<partial name="_NavInterna" />

<div class="card">
    <form asp-page-handler="Guardar" asp-route-id="@(Model.EsNuevo ? null : Model.Clasificacion.Id)" method="post">
        <input type="hidden" asp-for="Clasificacion.Id" />
        <input type="hidden" asp-for="Clasificacion.Activo" />
        <div style="display:flex; flex-direction:column; gap:12px; max-width:420px;">
            <div>
                <label>Código</label><br />
                <input asp-for="Clasificacion.Codigo" type="text" style="width:100%" />
                <span asp-validation-for="Clasificacion.Codigo" style="color:var(--danger); font-size:12px;"></span>
            </div>
            <div>
                <label>Nombre</label><br />
                <input asp-for="Clasificacion.Nombre" type="text" style="width:100%" />
                <span asp-validation-for="Clasificacion.Nombre" style="color:var(--danger); font-size:12px;"></span>
            </div>
            <div>
                <label>Orden</label><br />
                <input asp-for="Clasificacion.Orden" type="number" style="width:100%" />
            </div>
        </div>
        <div style="margin-top:16px; display:flex; gap:8px;">
            <a asp-page="/GestionGastos/Clasificaciones/Index" class="btn">Cancelar</a>
            <button type="submit" class="btn-primary">@(Model.EsNuevo ? "Guardar" : "Guardar cambios")</button>
        </div>
    </form>
</div>
```

- [ ] **Step 5: Agregar la pestaña al nav interno**

Modificar `Pages/Shared/_NavInterna.cshtml`, agregando una pestaña nueva antes de `EERR`:

```cshtml
        <a class="@Clase("/gestiongastos/clasificaciones")" asp-page="/GestionGastos/Clasificaciones/Index">
            <i class="fas fa-tags"></i> Clasificaciones
        </a>
```

- [ ] **Step 6: Verificar que el proyecto compila**

Run: `dotnet build "Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Modulo.GestionDistribucionGastos.csproj"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 7: Verificación manual funcional**

Levantar la app, entrar a `/gestiongastos/clasificaciones`, crear una clasificación, editarla, desactivarla/reactivarla, y confirmar que aparece en la lista con los valores correctos. Confirmar que el botón "Eliminar" funciona cuando no está en uso (se validará el bloqueo en la Task 4, cuando ya exista `Agrupacion_Cuenta` con datos).

- [ ] **Step 8: Commit**

No hay repositorio git en este proyecto. Omitir — continuar a la Task 4.

---

## Task 4: Página de Agrupación de Cuentas (sincronización MERGE + asignación por fila)

**Files:**
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\AgrupacionCuentas\Index.cshtml`
- Create: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\GestionGastos\AgrupacionCuentas\Index.cshtml.cs`
- Modify: `Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Pages\Shared\_NavInterna.cshtml`

**Interfaces:**
- Consumes: `ApplicationDbContext.AgrupacionCuenta`, `ApplicationDbContext.ClasificacionCuenta` (Task 2); ruta `/GestionGastos/Clasificaciones/Index` (Task 3, para el enlace "Gestionar clasificaciones"); universo real de cuentas vía SQL directo sobre `Staging_CentralizacionContable` (mismo criterio que `Reglas/Index.cshtml.cs::ContarCuentasQueMatchean`).
- Produces: ruta `/gestiongastos/agrupacioncuentas`, handler `OnPostAsignarAsync(string nroCuenta, int? clasificacionId)`.

- [ ] **Step 1: Crear Index.cshtml.cs con el MERGE de sincronización y el listado**

```csharp
// Pages/GestionGastos/AgrupacionCuentas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.AgrupacionCuentas;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public record FilaAgrupacion(string NroCuenta, string? NombreCuenta, int? ClasificacionId);

    public List<FilaAgrupacion> Cuentas { get; set; } = new();
    public List<ClasificacionCuenta> ClasificacionesDisponibles { get; set; } = new();
    public string? Filtro { get; set; }
    public bool SoloSinClasificar { get; set; }

    public async Task OnGetAsync(string? filtro, bool soloSinClasificar = false)
    {
        Filtro = filtro;
        SoloSinClasificar = soloSinClasificar;

        try
        {
            await SincronizarUniversoDeCuentasAsync();
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        IQueryable<AgrupacionCuenta> query = _db.AgrupacionCuenta;
        if (!string.IsNullOrWhiteSpace(filtro))
            query = query.Where(a => a.NroCuenta.Contains(filtro) || (a.NombreCuenta != null && a.NombreCuenta.Contains(filtro)));
        if (soloSinClasificar)
            query = query.Where(a => a.ClasificacionId == null);

        Cuentas = await query
            .OrderBy(a => a.NroCuenta)
            .Select(a => new FilaAgrupacion(a.NroCuenta, a.NombreCuenta, a.ClasificacionId))
            .ToListAsync();

        // Trae las clasificaciones activas (para nuevas asignaciones) más cualquier
        // clasificación inactiva que ya esté asignada a una de las filas mostradas, para
        // no perderla de vista en el dropdown de su fila (evita que se vea/quede como
        // "(sin clasificar)" y se pierda la asignación real ante un guardado accidental).
        var idsAsignados = Cuentas
            .Where(c => c.ClasificacionId != null)
            .Select(c => c.ClasificacionId!.Value)
            .Distinct()
            .ToList();

        ClasificacionesDisponibles = await _db.ClasificacionCuenta
            .Where(c => c.Activo || idsAsignados.Contains(c.Id))
            .OrderBy(c => c.Orden)
            .ThenBy(c => c.Nombre)
            .ToListAsync();
    }

    // Inserta en Agrupacion_Cuenta las cuentas nuevas encontradas en el universo real
    // (Staging_CentralizacionContable, TipoRegistro='DETALLE' -- mismo criterio que
    // Reglas/Index.cshtml.cs::ContarCuentasQueMatchean) que todavía no existan en la
    // tabla. No modifica ni borra filas existentes: una cuenta ya clasificada no se
    // toca, y una cuenta que dejó de aparecer en el universo real conserva su fila y
    // su clasificación histórica.
    private async Task SincronizarUniversoDeCuentasAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                ;WITH Universo AS (
                    SELECT NroCuenta, NombreCuenta = MAX(NombreCuenta)
                    FROM dbo.Staging_CentralizacionContable
                    WHERE TipoRegistro = 'DETALLE' AND NroCuenta IS NOT NULL
                    GROUP BY NroCuenta
                )
                MERGE dbo.Agrupacion_Cuenta WITH (HOLDLOCK) AS destino
                USING Universo AS origen
                ON destino.NroCuenta = origen.NroCuenta
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (NroCuenta, NombreCuenta, ClasificacionId, FechaModificacion, UsuarioModificacion)
                    VALUES (origen.NroCuenta, origen.NombreCuenta, NULL, GETDATE(), NULL);";
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<IActionResult> OnPostAsignarAsync(string nroCuenta, int? clasificacionId)
    {
        var fila = await _db.AgrupacionCuenta.FindAsync(nroCuenta);
        if (fila == null)
        {
            MensajeError = $"La cuenta {nroCuenta} ya no existe en la tabla de agrupación.";
            return RedirectToPage();
        }

        fila.ClasificacionId = clasificacionId;
        fila.FechaModificacion = DateTime.Now;
        fila.UsuarioModificacion = NombreUsuarioActual;
        await _db.SaveChangesAsync();

        MensajeExito = $"Cuenta {nroCuenta} actualizada.";
        return RedirectToPage(new { filtro = Filtro, soloSinClasificar = SoloSinClasificar });
    }
}
```

- [ ] **Step 2: Crear Index.cshtml**

```cshtml
@page "/gestiongastos/agrupacioncuentas"
@model Modulo.GestionDistribucionGastos.Pages.GestionGastos.AgrupacionCuentas.IndexModel
@section Styles {
    <link rel="stylesheet" href="~/css/gestiongastos.css" />
}
@{ ViewData["Title"] = "Agrupación de cuentas"; }

<partial name="_MensajesGestionGastos" model="Model" />
<partial name="_NavInterna" />

<div class="card">
    <div class="toolbar">
        <strong>Agrupación de cuentas</strong>
        <a asp-page="/GestionGastos/Clasificaciones/Index" class="toolbar-right">Gestionar clasificaciones</a>
    </div>
    <p style="color:var(--text-muted); font-size:13px; margin:0 0 8px;">
        Todas las cuentas alguna vez centralizadas, con la clasificación de negocio asignada
        para reportes (solo afecta CLDEPORFIN..vw_EerrAnual, no la distribución de gastos).
    </p>

    <form method="get" style="display:flex; gap:8px; align-items:center; margin-bottom:12px;">
        <input type="text" name="filtro" value="@Model.Filtro" placeholder="Buscar por cuenta o nombre" style="min-width:260px;" />
        <label style="display:flex; align-items:center; gap:4px; font-size:13px;">
            <input type="checkbox" name="soloSinClasificar" value="true" checked="@Model.SoloSinClasificar" />
            Solo sin clasificar
        </label>
        <button type="submit" class="btn-primary">Buscar</button>
    </form>

    <table class="grid">
        <thead>
            <tr>
                <th>Cuenta</th>
                <th>Nombre</th>
                <th>Clasificación</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var c in Model.Cuentas)
            {
                <tr>
                    <td>@c.NroCuenta</td>
                    <td>@c.NombreCuenta</td>
                    <td colspan="2">
                        <form asp-page-handler="Asignar" method="post" style="display:flex; gap:6px;">
                            <input type="hidden" name="nroCuenta" value="@c.NroCuenta" />
                            <input type="hidden" name="filtro" value="@Model.Filtro" />
                            <input type="hidden" name="soloSinClasificar" value="@Model.SoloSinClasificar" />
                            <select name="clasificacionId" style="min-width:220px;">
                                <option value="">(sin clasificar)</option>
                                @foreach (var cl in Model.ClasificacionesDisponibles)
                                {
                                    <option value="@cl.Id" selected="@(cl.Id == c.ClasificacionId)">@cl.Nombre</option>
                                }
                            </select>
                            <button type="submit">Guardar</button>
                        </form>
                    </td>
                </tr>
            }
            @if (!Model.Cuentas.Any())
            {
                <tr><td colspan="4">No hay cuentas para mostrar.</td></tr>
            }
        </tbody>
    </table>
</div>
```

- [ ] **Step 3: Agregar la pestaña al nav interno**

Modificar `Pages/Shared/_NavInterna.cshtml`, agregando una pestaña nueva junto a `Clasificaciones` (agregada en Task 3, Step 5):

```cshtml
        <a class="@Clase("/gestiongastos/agrupacioncuentas")" asp-page="/GestionGastos/AgrupacionCuentas/Index">
            <i class="fas fa-layer-group"></i> Agrupación cuentas
        </a>
```

- [ ] **Step 4: Verificar que el proyecto compila**

Run: `dotnet build "Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Modulo.GestionDistribucionGastos.csproj"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Verificación manual funcional**

Levantar la app, entrar a `/gestiongastos/agrupacioncuentas`. Confirmar que:
1. La tabla se puebla con cuentas reales (la sincronización corrió sin error).
2. Asignar una clasificación a una cuenta y guardar deja el valor persistido al recargar.
3. El filtro por texto y el checkbox "Solo sin clasificar" funcionan.
4. Volver a `/gestiongastos/agrupacioncuentas` una segunda vez no duplica ni pierde la clasificación ya asignada (repetir el MERGE es idempotente).

- [ ] **Step 6: Verificación manual del bloqueo de eliminación en Clasificaciones (pendiente de Task 3)**

Con al menos una cuenta ya asignada a una clasificación (Step 5), volver a `/gestiongastos/clasificaciones` e intentar eliminar esa clasificación: debe mostrar el mensaje de error y no eliminarla. Desactivarla en cambio debe funcionar sin problema.

- [ ] **Step 7: Verificación manual del reflejo en vw_EerrAnual**

Contra la base de desarrollo, con al menos una cuenta clasificada:
```sql
SELECT TOP 20 NroCuenta, CodClasificacion, Clasificacion, Monto
FROM dbo.vw_EerrAnual
WHERE CodClasificacion IS NOT NULL;
```
Confirmar que aparece al menos una fila con la clasificación recién asignada.

- [ ] **Step 8: Commit**

No hay repositorio git en este proyecto. Omitir — este es el último paso del plan.

---

## Self-Review

**Cobertura del spec:**
- Tabla `Clasificacion_Cuenta` → Task 1 (script) + Task 2 (entidad) + Task 3 (CRUD UI). ✓
- Tabla `Agrupacion_Cuenta` → Task 1 (script) + Task 2 (entidad) + Task 4 (sincronización + UI). ✓
- Sincronización automática (MERGE) al entrar a la página → Task 4, Step 1 (`SincronizarUniversoDeCuentasAsync`, llamada desde `OnGetAsync`). ✓
- No borra ni sobreescribe clasificaciones existentes → `WHEN NOT MATCHED BY TARGET` únicamente, sin rama `WHEN MATCHED`. ✓
- `vw_EerrAnual` con columnas nuevas sin tocar lógica existente → Task 1, Step 3 (columnas `Grupo`/`NombreGrupo`/`Resultado`/`Monto` idénticas, solo se agregan los dos `LEFT JOIN` y las dos columnas). ✓
- Dos páginas separadas, mismo patrón que Reglas → Task 3 y Task 4. ✓
- Bloqueo de eliminación de clasificación en uso → Task 3, Step 1 (`OnPostEliminarAsync`) + verificación cruzada en Task 4, Step 6. ✓
- Integración a `_NavInterna.cshtml` → Task 3 Step 5 y Task 4 Step 3. ✓

**Placeholder scan:** sin TBD/TODO; todos los steps de código traen el archivo completo o el fragmento exacto a insertar, sin "similar a la tarea N" sin contenido.

**Consistencia de tipos:** `AgrupacionCuenta.ClasificacionId` (int?) se usa consistentemente en Task 2 (entidad), Task 4 (`FilaAgrupacion`, `OnPostAsignarAsync(string nroCuenta, int? clasificacionId)`) y en el `<select name="clasificacionId">` del cshtml (mismo nombre de parámetro). `ClasificacionCuenta.Id` (int) coincide entre Task 2, Task 3 (`Form.cshtml.cs`) y Task 4 (`ClasificacionesDisponibles`, comparación `cl.Id == c.ClasificacionId`).
