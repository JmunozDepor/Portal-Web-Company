# Pendiente — Modulo.SellOut

Migrado desde `C:\PROYECTOS\PortalSAP_v2\plugins\Modulo.SellOut` (código base
original) a repo propio como plugin externo, siguiendo el mismo patrón que
`Modulo.Rendiciones`/`Modulo.Wms`/`Modulo.GestionDistribucionGastos`
(`docs/09-GUIA-DESARROLLO-PLUGINS.md` del portal).

## Cambios de adaptación (código original → contrato actual de `PortalSaas.Abstractions`)

- `PortalSAP.Abstractions.*` → `PortalSaas.Abstractions.*`.
- `IModuloPortal`: `CodigoModulo`→`ModuleCode`, `Nombre`→`Name`,
  `ObtenerMenu()`→`GetMenu()`, `RegistrarServicios()`→`RegisterServices()`.
- `MenuItemDefinition`: `Codigo`→`Code`, `CodigoPadre`→`ParentCode`,
  `Nombre`→`Name`, `Icono`→`Icon`, `RutaPagina`→`PageRoute`, `Orden`→`Order`.
- `ISqlServerService`/`ICurrentEmpresaAccessor` (motor único, resolución por
  `EmpresaCodigo`) → `IExternalDatabaseConnectionService`/`ICurrentCompanyAccessor`
  (motor dual Postgres/SqlServer, resolución **siempre** por `CompanyId`, sin
  fallback a fila global de la organización). Ver `ModuloSellOut.cs`.
- `Acciones.*` → `PortalActions.*` (`Ver`→`View`, `Crear`→`Create`,
  `Editar`→`Edit`, `Eliminar`→`Delete`).
- `PageModelBaseSellOut`: se agregó `[Authorize]` explícito (checklist §9 de la
  guía — sin esto un request anónimo crashea 500 en vez de redirigir a login).
- `DocumentListViewModel.TamanosPaginaDisponibles` → `.AvailablePageSizes`.
- `.csproj`: `ProjectReference` ahora apunta a
  `..\..\..\..\Portal SaaS - Core\src\PortalSaas.Abstractions\...` (repos hermanos
  bajo `Proyecto Portal Web-Company`), agregado `Npgsql.EntityFrameworkCore.PostgreSQL`
  (motor dual obligatorio aunque en producción CLSELLOUT resuelva siempre a
  SqlServer).

## Hecho

- [x] Repo git propio inicializado con commit inicial (`e573b39`).
- [x] Junction `Portal SaaS - Core\artifacts\plugins\Modulo.SellOut` →
      `dist\Modulo.SellOut\1.0.0` de este repo, mismo mecanismo que
      `Modulo.Rendiciones`.
- [x] `dotnet build` de verificación (Debug y Release) — compila limpio, 0
      errores/warnings. `Portal SaaS - Core\PortalSaas.sln` compila igual con
      el plugin ya cargable vía la junction.
- [x] Agregado a `build-all.ps1` (raíz del monorepo): limpieza de `dist/`,
      build Release del plugin, en el mismo paso que `Modulo.Rendiciones`/
      `Modulo.GestionDistribucionGastos` antes de compilar Core. **Cambio sin
      commitear todavía en el repo padre** (`Proyecto Portal Web-Company`) —
      requiere decisión explícita del dueño del repo antes de commitear ahí.

## Pendiente antes de dar el módulo por operativo

- [ ] Dar de alta la fila en `ModuleExternalConnection` (`module_external_connections`)
      para cada `Company` de Comercial Depor que use Sell Out, apuntando a
      CLSELLOUT (motor `sqlserver`) — sin UI todavía, alta directa en base (mismo
      estado inicial que `Modulo.Rendiciones`). Requiere credenciales/acceso a la
      base de la plataforma, no se hizo en esta sesión.
- [ ] Confirmar mapeo de permisos: los `Code` de menú (`clientes`, `sucursales`,
      etc.) deben existir en el árbol de menú sincronizado (`MenuSyncService`) con
      perfiles (`UserMenuProfile`/`ProfileAction`) antes de que cualquier usuario
      no-admin pueda ver el módulo.
- [ ] Prueba end-to-end real: levantar el Host (`build-all.ps1` o `dotnet run`)
      con la connection string de CLSELLOUT configurada y confirmar que las 8
      pantallas cargan y el CRUD funciona contra la base real.
- [ ] Revisar vocabulario/nombres de vista por colisión con otros plugins (§4 de
      la guía) — este módulo no comparte vistas parciales con nombre genérico,
      pero confirmar tras el primer arranque real junto al resto de plugins.
