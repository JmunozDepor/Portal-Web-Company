# Login a compañía por defecto — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cuando el login de tenant intenta activar la "Compañía por defecto" del usuario (`UserPreference.DefaultCompanyId`) y falla porque el usuario no tiene acceso real a esa compañía, mostrar un mensaje explícito y accionable en vez del comportamiento actual (caída silenciosa al selector con mensaje genérico).

**Architecture:** Un solo archivo cambia — `SelectCompanyModel.OnGetAsync`
(`Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml.cs`). Se
extrae la consulta de "compañías con acceso real" (ya usada por `CompanySwitcherViewComponent`)
a un método privado reusado en el nuevo camino de error, y se agrega un `ErrorMessage`
+ una lista `Companies` ya filtrada quando el default falla. La vista
(`SelectCompany.cshtml`) se ajusta para ocultar el selector cuando no hay ninguna
compañía con acceso.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 8), EF Core, `PortalSaasDbContext`.

## Global Constraints

- Comentarios y texto de UI en español (regla del proyecto).
- No modificar `CompanySessionActivator.TryActivateAsync` ni el modelo de datos de acceso (fuera de alcance, ver spec).
- No tocar `CompanySwitcher` (ya funciona correctamente).
- Seguir el patrón ya usado en el archivo: `Url.IsLocalUrl` para cualquier redirect, `asp-antiforgery="true"` explícito en cualquier `<form method="post">` sin otro atributo `asp-*`.
- `dotnet build "Portal SaaS - Core/PortalSaas.sln"` en 0 advertencias/0 errores y `dotnet test` en verde antes de dar la tarea por terminada.

---

### Task 1: Login entra directo a la compañía por defecto o muestra error explícito

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml.cs:59-88` (método `OnGetAsync`)
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml` (bloque del `<select>`, líneas ~76-100)

**Interfaces:**
- Consumes: `ICompanySessionActivator.TryActivateAsync(HttpContext, Guid companyId) : Task<Company?>` (ya existe, sin cambios) — `PortalSaasDbContext.Companies`/`UserMenuGroups`/`UserMenuProfiles` (ya existen, sin cambios).
- Produces: `SelectCompanyModel.ErrorMessage` (ya existe como propiedad, ahora también se setea desde `OnGetAsync`, no solo `OnPostAsync`). `SelectCompanyModel.Companies` (ya existe) ahora, en el camino de error, contiene solo las compañías con acceso real del usuario — no todas las de la organización.

- [ ] **Step 1: Reescribir `OnGetAsync` con el nuevo flujo de fallback**

Reemplazar el bloque completo del método (líneas 59-88 del archivo actual) por:

```csharp
    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.FindFirst("CompanyId") is not null)
        {
            return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var defaultCompanyId = await _db.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => p.DefaultCompanyId)
            .FirstOrDefaultAsync();

        if (defaultCompanyId is { } companyId)
        {
            var activado = await _activator.TryActivateAsync(HttpContext, companyId);
            if (activado is not null)
            {
                return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : Url.Content("~/Home/Index"));
            }

            // Default configurado (UserPreference.DefaultCompanyId) pero sin acceso real
            // (compañía desactivada/borrada, o sin fila en UserMenuGroups/UserMenuProfiles
            // para el usuario -- ver CompanySessionActivator.TryActivateAsync). Antes esto
            // caía en silencio al selector normal con un mensaje genérico recién al
            // postear -- ahora se explica la causa acá mismo, apenas se detecta.
            var companiaDefault = await _db.Companies.FindAsync(companyId);
            var nombreDefault = companiaDefault is not null ? $"{companiaDefault.Code} — {companiaDefault.Name}" : "configurada";
            ErrorMessage = $"Tu compañía por defecto ({nombreDefault}) no tiene permisos configurados. Contacta a tu administrador.";
        }

        Input.ReturnUrl = returnUrl;
        await CargarCompaniasConAccesoAsync(userId);
        AplicarCompaniaBloqueadaSiCorresponde();
        return Page();
    }
```

- [ ] **Step 2: Agregar el método `CargarCompaniasConAccesoAsync`, reemplazando `CargarCompaniasAsync`**

Reemplazar el método privado `CargarCompaniasAsync` (líneas 158-168 del archivo actual)
por esta versión, que filtra por acceso real (mismo criterio ya usado por
`CompanySwitcherViewComponent.InvokeAsync`) en vez de listar todas las compañías activas
de la organización sin filtrar:

```csharp
    private async Task CargarCompaniasConAccesoAsync(Guid userId)
    {
        var organizationId = Guid.Parse(User.FindFirstValue("OrganizationId")!);
        var isAdmin = bool.Parse(User.FindFirstValue("IsAdmin")!);

        var query = _db.Companies.Where(c => c.OrganizationId == organizationId && c.IsActive);

        // Mismo criterio que CompanySessionActivator/CompanySwitcherViewComponent -- un
        // admin ve todas, un usuario normal solo las que tiene acceso real vía
        // UserMenuGroups/UserMenuProfiles. Sin esto, el selector mostraría compañías que
        // igual rechazaría OnPostAsync al intentar activarlas.
        if (!isAdmin)
        {
            query = query.Where(c =>
                _db.UserMenuGroups.Any(g => g.UserId == userId && g.CompanyId == c.Id) ||
                _db.UserMenuProfiles.Any(p => p.UserId == userId && p.CompanyId == c.Id));
        }

        _companyEntities = await query.OrderBy(c => c.Code).ToListAsync();
        Companies = _companyEntities
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString()))
            .ToList();
    }
```

- [ ] **Step 3: Actualizar `OnPostAsync` para usar el método renombrado**

En `OnPostAsync` (línea 97 del archivo actual), cambiar:

```csharp
        await CargarCompaniasAsync();
```

por:

```csharp
        var userIdPost = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await CargarCompaniasConAccesoAsync(userIdPost);
```

(Nota: `OnPostAsync` ya calcula `userId` más abajo en el método, línea 110 del archivo
actual — dejar esa línea tal cual, `userIdPost` es una variable local nueva solo para
este primer uso, antes de que exista la declaración original de `userId` en el método.)

- [ ] **Step 4: Compilar y verificar 0 advertencias/0 errores**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Ajustar la vista para ocultar el selector sin compañías con acceso**

En `SelectCompany.cshtml`, envolver el `<form method="post">` completo (línea 72 a 108
del archivo actual, desde `<form method="post" asp-antiforgery="true">` hasta su `</form>`
de cierre) con esta condición — si no queda ninguna compañía con acceso, solo se
muestra el mensaje de error ya renderizado más arriba (línea 61-66, sin cambios) y el
link "Cerrar sesión":

```cshtml
                @if (Model.Companies.Count > 0)
                {
                    @* form existente sin cambios acá adentro *@
                }
                else if (Model.ErrorMessage is not null)
                {
                    <p class="text-muted" style="font-size: 12.5px;">No tenés acceso a ninguna compañía todavía.</p>
                }
```

El `<form method="post" asp-page="/Account/Logout" ...>` de "Cerrar sesión" (línea 110-112
del archivo actual) queda **fuera** de este `@if`/`else`, sin cambios — debe seguir visible
siempre, tenga o no el usuario compañías con acceso.

- [ ] **Step 6: Verificar manualmente contra Postgres real (mismo criterio E2E del resto del proyecto — sin tests xUnit dedicados, es lógica de PageModel de Razor Pages sin precedente de test en este repo)**

Con el Host corriendo (`dotnet run --project "Portal SaaS - Core/src/PortalSaas.Host"`):
1. Usuario con `UserPreference.DefaultCompanyId` apuntando a una compañía sin fila en
   `UserMenuGroups`/`UserMenuProfiles`, y sin acceso a ninguna otra compañía de la
   organización → al loguearse debe ver el mensaje de error específico (con código y
   nombre de la compañía) y **sin** selector, solo "Cerrar sesión".
2. Mismo caso, pero con acceso real a **otra** compañía de la organización → debe ver
   el mismo mensaje de error arriba, y el selector debajo listando únicamente esa otra
   compañía (no todas las de la organización).
3. Usuario con `DefaultCompanyId` apuntando a una compañía con acceso real → debe seguir
   entrando directo a Home, sin ver esta página (comportamiento ya existente, no debe
   regresionar).
4. Usuario sin `DefaultCompanyId` configurado → debe seguir viendo el selector normal,
   sin mensaje de error (comportamiento ya existente, no debe regresionar).

- [ ] **Step 7: Ejecutar la suite de tests completa**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln"`
Expected: todos los tests existentes en verde (sin tests nuevos en esta tarea — ver Step 6).

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml.cs" "Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml"
git commit -m "fix: mensaje explícito y selector filtrado cuando la compañía por defecto del login no tiene acceso"
```

---

## Nota de alcance (no es una tarea, es contexto para quien ejecute este plan)

El punto B del pedido original (excepciones de permisos por usuario) **ya está
implementado** en ambas consolas de administración —
`/Admin/Organizations/Users/Permissions.cshtml` (sección "Perfil por nodo de menú")
y `/organizacion/usuarios/editar/{id}` (tab "Permisos") — vía `UserMenuProfile`, que
ya se resuelve con prioridad sobre el grupo heredado. No requiere ninguna tarea de
desarrollo. El punto 2 original (multi-compañía) tampoco requiere desarrollo — el
`CompanySwitcher` del topbar ya filtra por acceso real. Este plan cubre únicamente el
punto 1 (login a compañía por defecto).
