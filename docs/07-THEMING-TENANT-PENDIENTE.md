# 07 — Theming Visual por Tenant (pendiente de implementar)

**Estado: diseñado, no implementado.** Se pausó a propósito porque hay otro
proceso trabajando en paralelo sobre varios de los mismos archivos que esta
feature necesita tocar (`Plan.cs`, `PortalSaasDbContext.cs`,
`ContractLimitService.cs`, `Organizations/Index.cshtml`, además del conector
SAP que se está portando). Implementar theming ahora encima de eso arriesga
choques. Este documento existe para que **cualquier módulo nuevo que se
construya mientras tanto quede compatible desde el día uno**, sin necesitar
un rework cuando esta feature se retome.

El plan completo y detallado (archivos exactos, código de ejemplo, migraciones)
vive en `C:\Users\Jorge\.claude\plans\iridescent-petting-castle.md` — es un
archivo local de la sesión de Claude Code que lo diseñó, no está en este repo.
Cuando se retome la implementación, usar ese plan como punto de partida pero
**releer el estado actual de cada archivo antes de aplicar nada** — el trabajo
concurrente puede haberlos cambiado desde que se escribió.

## Qué es

Cada organización va a poder definir sus propios colores corporativos
(primario/secundario) y un logo (link a una imagen que el cliente aloja, sin
upload/storage propio), aplicado solo en páginas ya autenticadas de tenant
(`/Home/*`, layout `_Layout.cshtml`), gateado por plan de suscripción.

## Reglas de compatibilidad para módulos nuevos (mientras esta feature no se implementa)

1. **Nunca hardcodear colores de marca en hex dentro de Razor/CSS nuevo.**
   Usar siempre las clases utilitarias de Bootstrap (`btn-primary`,
   `text-secondary`, `bg-primary`, etc.) o `var(--bs-primary)` /
   `var(--bs-secondary)`. Theming va a sobreescribir esas variables CSS en
   `_Layout.cshtml`; cualquier estilo que no dependa de ellas queda "sordo" al
   branding del cliente y va a necesitar rework.

2. **`_Layout.cshtml` (el de tenant, NO `_AdminLayout.cshtml`) va a ganar un
   `<style>` inyectado** con `--bs-primary` / `--bs-primary-rgb` /
   `--bs-secondary` / `--bs-secondary-rgb`, ubicado justo después del `<link>`
   a `bootstrap.min.css`. Nueva UI de tenant no debe asumir que ese bloque no
   existe, ni agregar su propio `<style>` con colores fijos ahí mismo.

3. **Nombres reservados** — ningún módulo nuevo debe reusarlos para otra cosa:
   - Entidad `OrganizationBranding` / tabla `organization_branding`
   - Columna `plans.allows_custom_branding`
   - Método `IContractLimitService.CheckBrandingAllowedAsync`
   - Carpeta `Pages/Admin/Organizations/Branding/`
   - DTO `OrganizationBrandingDto`, servicio `IOrganizationBrandingService`

4. **`Organizations/Index.cshtml`** — el bloque de links a sub-recursos por
   organización (Usuarios · Instancias · Compañías · Licencia/Suscripción) va
   a sumar un link más ("Marca") en el mismo formato. Si el trabajo en curso
   reestructura ese bloque, mantener un formato donde agregar un ítem más sea
   trivial (no una lista hardcodeada que haya que reescribir entera).

5. **`Plan.cs` / `Plans/Edit.cshtml.cs` / `Plans/Create.cshtml.cs`** — esta
   feature va a sumar `public bool AllowsCustomBranding { get; set; } = false;`
   a `Plan`, más un checkbox correspondiente en los formularios de admin. Si el
   trabajo concurrente reestructura el modelo de límites de `Plan` (ej. lo
   mueve a un sub-objeto, cambia de columnas planas a JSON, etc.), tenerlo en
   cuenta para que sumar este campo después sea un cambio chico.

6. **`ContractLimitService.cs`** — el resolver privado `GetActivePlanAsync`
   (resuelve el plan activo de una organización por `Subscription` o
   `OnPremiseLicense` según `Organization.Mode`) debe seguir siendo reusable
   tal cual — `CheckBrandingAllowedAsync` lo va a llamar exactamente igual que
   `CheckUserLimitAsync`/`CheckCompanyLimitAsync`/`CheckMonthlyTransactionLimitAsync`.
   Si se refactoriza, mantener la misma firma/comportamiento (recibe
   `organizationId`, devuelve `Plan?`, `null` si no hay plan activo).

## Decisiones ya cerradas (no reabrir sin razón nueva)

- Solo post-login, sin branding en `/Account/Login`.
- Colores + logo por URL únicamente — sin upload de archivo en esta entrega.
- Gateado por plan, falla hacia "denegado" si no hay plan activo (mismo
  criterio que el resto de `IContractLimitService`).
- Nombre `OrganizationBranding`, no `OrganizationTheme` — evita colisión de
  concepto con `UserThemePreference` (light/dark/system, algo completamente
  distinto: preferencia visual por usuario, no marca por organización).
