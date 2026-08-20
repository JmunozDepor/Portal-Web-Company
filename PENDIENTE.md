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

## Pendiente antes de dar el módulo por operativo

- [ ] Crear la junction `Portal SaaS - Core\artifacts\plugins\Modulo.SellOut` →
      `dist\Modulo.SellOut\1.0.0` de este repo (mismo mecanismo que
      `Modulo.Rendiciones`, ver comentario en `publish-dist.ps1`) — o copiar el
      artefacto manualmente si no se quiere junction.
- [ ] `dotnet build` de verificación (sin acceso a `Portal SaaS - Core` resuelto
      todavía en esta sesión de migración — confirmar que el `ProjectReference`
      relativo resuelve bien).
- [ ] Dar de alta la fila en `ModuleExternalConnection` (`module_external_connections`)
      para cada `Company` de Comercial Depor que use Sell Out, apuntando a
      CLSELLOUT (motor `sqlserver`) — sin UI todavía, alta directa en base (mismo
      estado inicial que `Modulo.Rendiciones`).
- [ ] Confirmar mapeo de permisos: los `Code` de menú (`clientes`, `sucursales`,
      etc.) deben existir en el árbol de menú sincronizado (`MenuSyncService`) con
      perfiles (`UserMenuProfile`/`ProfileAction`) antes de que cualquier usuario
      no-admin pueda ver el módulo.
- [ ] Revisar vocabulario/nombres de vista por colisión con otros plugins (§4 de
      la guía) — este módulo no comparte vistas parciales con nombre genérico,
      pero confirmar tras el primer build real junto al resto de plugins.
