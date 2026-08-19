# Compila los plugins externos (Modulo.Rendiciones, Modulo.GestionDistribucionGastos) y
# luego Portal SaaS - Core, en ese orden -- los plugins primero porque su output se
# copia (por el junction de cada uno) a Core\artifacts\plugins\{Modulo} antes de que el
# Host los cargue. Para el Host antes de compilar Core (mismo motivo que
# .vscode/stop-host.ps1): el DLL queda bloqueado mientras el proceso corre.
#
# Levanta LOS DOS ambientes por default, en puertos fijos (2026-08-03, a pedido del
# dueño del proyecto; movidos de 5001/5002 a 6001/6002 el 2026-08-10 porque
# 4908-5007 quedó como rango excluido por Windows -- Kestrel fallaba a bindear ahí
# con SocketException 10013 "acceso no permitido", algo que quedaba oculto porque el
# script no validaba que el sitio realmente respondiera. Ver validación al final de
# Start-PortalSaasInstance. Si 6001/6002 también terminan excluidos en algún momento,
# `netsh interface ipv4 show excludedportrange protocol=tcp` muestra los rangos
# vigentes -- elegir un puerto fuera de todos ellos):
#   Central          -> http://localhost:6001  (emite/valida licencias, admin de plataforma)
#   Comercial Depor  -> http://localhost:6002  (OnPremise real, sin Licensing:Role para
#                        no competir con el heartbeat del piloto real en IIS)
# Cada uno en su propia ventana de consola (Start-Process) -- quedan corriendo después
# de que este script termina; cerrar esas ventanas (o Ctrl+C adentro) para pararlos.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$hostProject = "$root\Portal SaaS - Core\src\PortalSaas.Host"

# Credenciales reales (connection strings, MasterSecretKey, clave de licensing) NUNCA
# hardcodeadas acá -- viven en build-secrets.local.ps1 (no versionado, ver
# build-secrets.local.ps1.example para la plantilla). Falla temprano y con mensaje
# claro si falta, en vez de dejar que dotnet arranque con secretos vacíos.
$secretsFile = "$root\build-secrets.local.ps1"
if (-not (Test-Path $secretsFile)) {
    throw "Falta $secretsFile -- copia build-secrets.local.ps1.example a build-secrets.local.ps1 y completa los valores reales antes de correr este script."
}
. $secretsFile

Write-Host "== Deteniendo procesos de este repo que hayan quedado corriendo ==" -ForegroundColor Cyan
# Tres formas posibles de proceso zombie:
#   1. "dotnet.exe ... PortalSaas.Host.dll"      -> dotnet run/F5 quedó vivo
#   2. "PortalSaas.Host.exe"                     -> apphost, ej. bin\Debug\net8.0\ AnyCPU
#      stale (ver CLAUDE.md "Bug real de despliegue local")
#   3. Cualquier otro dotnet.exe (build/msbuild) lanzado sobre ESTE repo que quedó
#      colgado (ej. un `dotnet build` que el usuario mató con Ctrl+C a mitad de camino,
#      o una task de VS Code abandonada) -- estos no matchean 'PortalSaas.Host.dll'
#      pero igual bloquean los .dll con locks de archivo (MSB3027/MSB3021) y no los
#      cazaba el filtro original. Se identifican por tener el path del repo en su
#      CommandLine, sin importar qué target/proyecto estén corriendo.
$rootEscaped = [regex]::Escape($root)
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='PortalSaas.Host.exe'" |
    Where-Object {
        $_.Name -eq 'PortalSaas.Host.exe' -or
        ($_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll')) -or
        ($_.CommandLine -and $_.CommandLine -match $rootEscaped)
    } |
    ForEach-Object {
        Write-Host "Deteniendo PID $($_.ProcessId) ($($_.Name)): $($_.CommandLine)"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
Start-Sleep -Seconds 1

# El target PublicarComoPlugin de cada plugin copia con SkipUnchangedFiles="true" --
# no borra un .dll viejo que fue renombrado/eliminado del proyecto, así que un dist/
# desactualizado puede dejar artefactos stale que PluginManager carga igual (mismo
# tipo de bug ya documentado en Core\CLAUDE.md para bin\Debug\net8.0\ AnyCPU). Se
# limpia dist/{Modulo} de cada plugin ANTES de compilar para que el resultado sea
# siempre el output real del build de hoy, nunca una mezcla con uno anterior. No se
# toca el junction en Core\artifacts\plugins\{Modulo} -- apunta a la carpeta por
# nombre, no por inodo, así que sigue resolviendo bien apenas el build la recrea.
Write-Host "== Limpiando dist\ de ambos plugins ==" -ForegroundColor Cyan
@(
    "$root\Portal SaaS - Plugins\Modulo.Rendiciones\dist\Modulo.Rendiciones",
    "$root\Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\dist\Modulo.GestionDistribucionGastos"
) | ForEach-Object {
    if (Test-Path $_) {
        Write-Host "Borrando $_"
        Remove-Item -Recurse -Force $_
    }
}

Write-Host "== Compilando Modulo.Rendiciones (Release) ==" -ForegroundColor Cyan
dotnet build "$root\Portal SaaS - Plugins\Modulo.Rendiciones\src\Modulo.Rendiciones\Modulo.Rendiciones.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Falló el build de Modulo.Rendiciones" }

Write-Host "== Compilando Modulo.GestionDistribucionGastos (Release) ==" -ForegroundColor Cyan
dotnet build "$root\Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Modulo.GestionDistribucionGastos.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "Falló el build de Modulo.GestionDistribucionGastos" }

Write-Host "== Compilando Portal SaaS - Core ==" -ForegroundColor Cyan
dotnet build "$root\Portal SaaS - Core\PortalSaas.sln"
if ($LASTEXITCODE -ne 0) { throw "Falló el build de Portal SaaS - Core" }

# --no-launch-profile a propósito -- los puertos/rol de cada instancia se fijan acá
# abajo vía variables de entorno del PROCESO HIJO (Start-Process copia el entorno del
# padre al momento de lanzar), no vía Properties/launchSettings.json (que solo tiene
# UN perfil "https" -- no alcanza para levantar dos roles distintos a la vez).
function Start-PortalSaasInstance {
    param(
        [string]$Nombre,
        [string]$Url,
        [hashtable]$EnvVars
    )

    # Limpia las claves específicas de rol antes de cada arranque -- sin esto, una
    # variable seteada para Central (ej. Licensing__Role) quedaría pegada en el
    # entorno del script y se filtraría también al proceso de Comercial Depor.
    "Licensing__Role", "Licensing__SigningPrivateKey" | ForEach-Object {
        Remove-Item "Env:$_" -ErrorAction SilentlyContinue
    }
    foreach ($key in $EnvVars.Keys) {
        Set-Item "Env:$key" -Value $EnvVars[$key]
    }
    $env:ASPNETCORE_URLS = $Url
    $env:ASPNETCORE_ENVIRONMENT = "Development"

    Write-Host "== Levantando $Nombre en $Url ==" -ForegroundColor Cyan
    # -NoNewWindow + logs redirigidos a archivo, en vez de -WindowStyle Normal: una
    # ventana de consola nueva depende de que exista una estación de ventanas
    # interactiva -- en sesiones no interactivas (ej. tareas en background) el proceso
    # moría al instante sin dejar rastro. Redirigiendo a archivo funciona en cualquier
    # contexto y de paso deja logs para diagnosticar sin tener que ir a buscar la ventana.
    $logDir = "$root\.run-logs"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $slug = ($Nombre -replace '[^a-zA-Z0-9]', '')
    $outLog = "$logDir\$slug.out.log"
    $errLog = "$logDir\$slug.err.log"
    Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", "`"$hostProject`"", "--no-launch-profile") `
        -NoNewWindow `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog

    # No basta con que el proceso haya arrancado (Start-Process no espera a que Kestrel
    # bindee el puerto) -- se comprobó en la práctica que build-all.ps1 podía terminar
    # "en verde" mientras el sitio real tardaba en levantar o directamente crasheaba
    # después (ver ERR_CONNECTION_REFUSED en el navegador aunque el script no reportó
    # error). Se hace polling real a la URL hasta que responda o se agote el timeout.
    $timeoutSeconds = 60
    $elapsed = 0
    $up = $false
    while ($elapsed -lt $timeoutSeconds) {
        Start-Sleep -Seconds 2
        $elapsed += 2
        try {
            # -SkipHttpErrorCheck no existe en Windows PowerShell 5.1 (solo pwsh 7+),
            # así que se confía en el try/catch: Invoke-WebRequest sigue redirects (un
            # 302 a /Account/Login resuelve solo), y si el server respondiera con un
            # error HTTP real (4xx/5xx) igual cae acá abajo -- se trata como "arriba"
            # porque lo único que nos importa es que Kestrel esté escuchando.
            Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3 | Out-Null
            $up = $true
            break
        } catch [System.Net.WebException] {
            if ($_.Exception.Response) {
                $up = $true
                break
            }
            # Conexión rechazada todavía -- seguir esperando.
        } catch {
            # Conexión rechazada todavía -- seguir esperando.
        }
    }

    if (-not $up) {
        Write-Host "== $Nombre no respondió en $Url tras $timeoutSeconds s -- últimas líneas de $errLog ==" -ForegroundColor Red
        if (Test-Path $errLog) { Get-Content $errLog -Tail 40 }
        Write-Host "== últimas líneas de $outLog ==" -ForegroundColor Red
        if (Test-Path $outLog) { Get-Content $outLog -Tail 40 }
        throw "$Nombre no levantó en $Url -- revisa los logs de arriba."
    }

    Write-Host "== $Nombre respondiendo OK en $Url ==" -ForegroundColor Green
}

Start-PortalSaasInstance -Nombre "Central" -Url "http://localhost:6001" -EnvVars @{
    "Database__Provider"              = "sqlserver"
    "ConnectionStrings__Default"      = $Secrets.Central.ConnectionString
    "Security__MasterSecretKey"       = $Secrets.Central.MasterSecretKey
    "Licensing__Role"                 = "Central"
    "Licensing__SigningPrivateKey"    = $Secrets.Central.LicensingSigningKey
    "Licensing__CentralPublicKey"     = $Secrets.Central.LicensingPublicKey
}

Start-PortalSaasInstance -Nombre "Comercial Depor (OnPremise)" -Url "http://localhost:6002" -EnvVars @{
    "Database__Provider"          = "sqlserver"
    "ConnectionStrings__Default"  = $Secrets.ComercialDepor.ConnectionString
    "Security__MasterSecretKey"   = $Secrets.ComercialDepor.MasterSecretKey
    "Licensing__CentralPublicKey" = $Secrets.ComercialDepor.LicensingPublicKey
    # Instalación OnPremise de una sola organización/compañía real -- el Login precarga
    # y bloquea el campo Organización, y el selector de compañía (SelectCompany.cshtml)
    # hace lo mismo un paso más adelante, en vez de dejar "elegir" entre opciones que no
    # existen (ver appsettings.json, sección Tenant). Deben coincidir EXACTO con
    # Organization.Slug / Company.Code (case-insensitive) de esta organización.
    "Tenant__DefaultOrganizationSlug" = "Depor"
    # TEMPORAL -- cambiado de "DEPOR" a "DEPORTEST" para probar el flujo de
    # aprobación de Modulo.Rendiciones contra la base de desarrollo aislada
    # (PS_COMDEPOR_RG_DEV) sin tocar datos reales. Revertir a "DEPOR" después
    # de la prueba.
    "Tenant__DefaultCompanyCode"      = "DEPORTEST"
}

Write-Host "== Listo -- Central: http://localhost:6001 · Comercial Depor: http://localhost:6002 ==" -ForegroundColor Green
