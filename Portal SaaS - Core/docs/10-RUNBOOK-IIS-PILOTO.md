# Runbook — publicar el piloto en IIS (Central + OnPremise Comercial Depor)

Complementa `docs/05-RUNBOOK-PRODUCCION.md` (que cubre solo la base de datos). Este
runbook cubre publicar el binario en un servidor Windows real con IIS, para que el
piloto de Comercial Depor lo pueda usar de verdad — no solo `dotnet run` local.

Estado de las bases: `PS_CENTRAL` y `PS_COMDEPOR` (renombradas 2026-08-08, antes
`portalsaas_central`/`portalsaas_comercialdepor` -- ver `docs/11-ESTADO-PILOTO-DESARROLLO.md`
§2) en `sqlsap.cdepor.cl,11433` ya están migradas y con el `ActivationKey` de piloto vigente
(`sAs4ImoPXnINrtE2kjRAhcHDs/Rqhjhl`, rotado — el de la prueba de escritorio anterior ya
no es válido). No hace falta tocar la base de nuevo para este paso.

**Topología real del servidor**: `https://depor-sl.sapenlanube.com:9003` es el único
puerto público del servidor IIS, compartido por varias aplicaciones existentes — no es
un servidor dedicado a este portal. Por eso los dos sitios de este runbook NO se crean
como `New-Website` con su propio puerto (eso rompería lo que ya corre en `:9003`):
se cuelgan como **IIS Applications** (subaplicaciones) del sitio existente que ya tiene
el binding a `:9003`, diferenciadas por path:
- `https://depor-sl.sapenlanube.com:9003/Saas_Central` — rol Central.
- `https://depor-sl.sapenlanube.com:9003/Depor` — rol OnPremise, el que van a usar los
  pilotos de Comercial Depor.

Certificado TLS: ya existe uno real para `depor-sl.sapenlanube.com` en ese servidor
(confirmado) — no hace falta generar nada, el binding HTTPS de `:9003` ya está resuelto
por el sitio padre.

**Nota de seguridad**: las claves de este documento (MasterSecretKey, clave privada de
firma de licencias, ActivationKey) se generaron y aparecen en texto plano en esta
sesión de trabajo — aceptable para un piloto, pero antes de que esto sea producción
real hay que rotarlas generándolas directo en el servidor (nunca pegadas en un chat).

## 1. Prerrequisitos en el servidor Windows

- **.NET 8 Hosting Bundle** (no el SDK, el *Hosting Bundle* — trae el módulo
  `AspNetCoreModuleV2` que IIS necesita). Se descarga desde la página oficial de
  descargas de .NET 8 de Microsoft — buscar "ASP.NET Core Runtime 8.0 - Windows
  Hosting Bundle". Después de instalarlo: `iisreset` (reinicia IIS para que cargue el
  módulo).
- **IIS** con el rol "Web Server (IIS)" ya instalado (rol de Windows Server, o
  "Internet Information Services" en Windows 10/11 si el piloto corre en una
  workstation).
- Verificar acceso de red saliente desde el servidor hacia `sqlsap.cdepor.cl,11433`
  (puerto TCP 11433 abierto).

## 2. Copiar los dos paquetes al servidor

Ya publicados y listos en esta máquina, en:
```
...\scratchpad\sitio-central\
...\scratchpad\sitio-comercialdepor\
```
Cada carpeta ya trae:
- El binario de `PortalSaas.Host` (framework-dependent — necesita el Hosting Bundle
  del paso 1, no es self-contained).
- `artifacts\plugins\` con los 6 módulos (Administracion, Compras, ImportacionGenerica,
  Inventario, Rendiciones, Ventas) — sin esto el portal arranca pero sin ningún módulo
  de negocio.
- `web.config` YA CONFIGURADO con las connection strings y claves del piloto (rol
  Central en un config, rol OnPremise en el otro) — no hace falta tocar nada más ahí
  salvo el paso 5 (URL real del Central).

Copiar cada carpeta completa al servidor, por ejemplo:
```
C:\inetpub\portalsaas-central\
C:\inetpub\portalsaas-comercialdepor\
```
(Cualquier ruta sirve, esta es solo la convención estándar de IIS.)

## 3. Identificar el sitio existente en :9003 y colgar las dos aplicaciones

**Este paso va ANTES que los permisos (paso 4)** — `icacls` con `IIS AppPool\<nombre>`
falla con "No mapping between account names and security IDs was done" si el
Application Pool todavía no existe (la identidad virtual del pool solo se crea junto
con el pool, `New-WebAppPool`, que está acá abajo).

Primero identificar el nombre exacto del sitio IIS que ya tiene el binding a `:9003`
(no lo inventamos — hay que confirmarlo en el servidor):

```powershell
Import-Module WebAdministration
Get-WebBinding | Where-Object { $_.bindingInformation -like "*:9003:*" }
Get-Website
```

Con el nombre real del sitio (reemplazar `<SITIO_9003>` abajo por ese valor):

```powershell
# --- Central: /Saas_Central ---
New-WebAppPool -Name "portalsaas-central"
Set-ItemProperty "IIS:\AppPools\portalsaas-central" -Name managedRuntimeVersion -Value ""
New-WebApplication -Site "<SITIO_9003>" -Name "Saas_Central" -PhysicalPath "C:\inetpub\portalsaas-central" -ApplicationPool "portalsaas-central"

# --- OnPremise Comercial Depor: /Depor ---
New-WebAppPool -Name "portalsaas-comercialdepor"
Set-ItemProperty "IIS:\AppPools\portalsaas-comercialdepor" -Name managedRuntimeVersion -Value ""
New-WebApplication -Site "<SITIO_9003>" -Name "Depor" -PhysicalPath "C:\inetpub\portalsaas-comercialdepor" -ApplicationPool "portalsaas-comercialdepor"
```

`managedRuntimeVersion=""` es obligatorio (ASP.NET Core no usa el CLR de .NET
Framework que gestiona IIS — "No Managed Code"). Cada aplicación queda con su PROPIO
Application Pool (aislamiento de proceso — un fallo o reciclado de una no afecta a la
otra ni a las demás apps que ya comparten el sitio en `:9003`).

Con esto, las URLs finales quedan:
- Central: `https://depor-sl.sapenlanube.com:9003/Saas_Central`
- Comercial Depor (piloto): `https://depor-sl.sapenlanube.com:9003/Depor`

No hace falta abrir firewall ni tocar bindings — `:9003` ya está público y HTTPS ya
resuelto por el sitio padre.

**Habilitar "Load User Profile" en los dos pools — obligatorio, no opcional.** Sin
esto, `ECDsa.Create()`/`ImportPkcs8PrivateKey` (usado por `LicenseTokenService` para
firmar/verificar licencias) falla con
`CryptographicException: The system cannot find the file specified.`, porque Windows
CNG no tiene dónde alojar el key container temporal sin un perfil de usuario cargado —
mismo motivo por el que ASP.NET Core Data Protection cae a "ephemeral key repository"
(bug real encontrado en el primer deploy de este runbook, 2026-08-02):
```powershell
Set-ItemProperty "IIS:\AppPools\portalsaas-central" -Name processModel.loadUserProfile -Value $true
Set-ItemProperty "IIS:\AppPools\portalsaas-comercialdepor" -Name processModel.loadUserProfile -Value $true
Restart-WebAppPool -Name "portalsaas-central"
Restart-WebAppPool -Name "portalsaas-comercialdepor"
```

## 4. Permisos de carpeta

El identity de cada Application Pool (recién creado en el paso 3) necesita permisos de
**escritura** en su carpeta, no solo lectura — el Host escribe logs (`logs\stdout`) y,
en el sitio OnPremise, el fingerprint de licenciamiento
(`licensing\fingerprint.txt`, ver `IInstallationFingerprintProvider`). Sin esto el
primer arranque falla al no poder crear ese archivo.

```powershell
icacls "C:\inetpub\portalsaas-central" /grant "IIS AppPool\portalsaas-central:(OI)(CI)M"
icacls "C:\inetpub\portalsaas-comercialdepor" /grant "IIS AppPool\portalsaas-comercialdepor:(OI)(CI)M"
```

## 5. Licensing:CentralServerUrl ya está seteado

`web.config` del sitio `/Depor` ya trae
`Licensing__CentralServerUrl=https://depor-sl.sapenlanube.com:9003/Saas_Central` — no
requiere ajuste, salvo que el path del paso 4 termine siendo distinto a `/Saas_Central`
(en ese caso editar esa línea y `Restart-WebAppPool -Name "portalsaas-central"` /
`portalsaas-comercialdepor` para que tome el cambio).

## 6. Primer arranque y verificación

1. Abrir `https://depor-sl.sapenlanube.com:9003/Depor` — debe cargar el login del
   portal (`Pages/Account/Login`). Si en vez del login aparece un 404 o los estilos no
   cargan, es casi seguro un problema de path base de la subaplicación — revisar que
   los links generados (`asp-page`, `~/css/...`) respeten el prefijo `/Depor`; el
   módulo IIS/ASP.NET Core lo resuelve solo en el caso estándar, pero vale la pena
   confirmarlo en el primer acceso real.
2. El primer arranque del sitio OnPremise dispara el heartbeat automático
   (`LicenseActivatorBackgroundService`) contra `/Saas_Central` — confirmar en
   `PS_COMDEPOR` que `signed_status_token` quedó no-nulo:
   ```sql
   SELECT signed_status_token, signed_status_updated_at FROM on_premise_licenses;
   ```
3. Revisar `C:\inetpub\portalsaas-comercialdepor\logs\stdout*.log` si algo falla —
   `stdoutLogEnabled="true"` ya quedó habilitado en el `web.config` para este piloto
   (apagarlo cuando esté estable, genera bastante volumen).
4. Dar de alta el primer usuario tenant real (todavía no hay UI de self-registro —
   insertar en `users` de `PS_COMDEPOR`, mismo criterio que
   `platform_admins` del runbook de base de datos, o usar el panel Admin en
   `https://depor-sl.sapenlanube.com:9003/Saas_Central/Admin/Login` si ya se accedió
   como platform admin).

## Reciclar tras un cambio de configuración

Editar `web.config` no alcanza solo con guardarlo — hay que reciclar el proceso:
```powershell
Restart-WebAppPool -Name "portalsaas-comercialdepor"
```
