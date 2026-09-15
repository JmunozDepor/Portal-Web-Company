# Publica PortalSaas.Host + los 5 plugins a dist/portalsaas, listo para copiar a un
# sitio IIS (ver docs/10-RUNBOOK-IIS-PILOTO.md). NO toca el servidor -- solo arma la
# carpeta local; subirla y reciclar el Application Pool sigue siendo manual a propósito.
#
# Publica-y-copia-junk-de-más fue la causa real del bug de 404 en producción (ver
# conversación 2026-08-06): un `artifacts/plugins` viejo o con archivos de más
# mezclado en el paquete subido al servidor. Este script limpia `dist/` siempre antes
# de escribir, y filtra del lado de los plugins todo lo que PluginManager/
# PluginLoadContext (src/PortalSaas.Core/Infraestructura/) NUNCA usa en runtime:
# *.pdb, *.xml (doc comments), *.deps.json, *.runtimeconfig*.json, y la carpeta ref/
# (assemblies de solo-compilación).

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$hostProject = "$root\src\PortalSaas.Host\PortalSaas.Host.csproj"
$artifactsPlugins = "$root\artifacts\plugins"
$distDir = "$root\dist\portalsaas"

$pluginProjects = @(
    "$root\plugins\Modulo.Administracion\Modulo.Administracion.csproj",
    "$root\plugins\Modulo.Ventas\Modulo.Ventas.csproj",
    "$root\plugins\Modulo.Compras\Modulo.Compras.csproj",
    "$root\plugins\Modulo.Inventario\Modulo.Inventario.csproj",
    "$root\plugins\Modulo.ImportacionGenerica\Modulo.ImportacionGenerica.csproj"
)

# Extensiones/carpetas que PluginManager/PluginLoadContext nunca leen en runtime --
# ver PublicarComoPlugin en cada plugins/*/*.csproj (copia $(OutputPath)**\*.* tal
# cual, sin filtrar), y PluginLoadContext.Load (solo resuelve *.dll).
$juncExtensions = @(".pdb", ".xml")
$juncNameParts = @(".deps.json", ".runtimeconfig.json", ".runtimeconfig.dev.json")

function Test-EsBasuraDePlugin([System.IO.FileInfo]$archivo) {
    if ($archivo.DirectoryName -match '\\ref(\\|$)') { return $true }
    # bin\...\publish\ anidado dentro de $(OutputPath) -- residuo de un `dotnet
    # publish` corrido antes a mano sobre el plugin suelto, que PublicarComoPlugin
    # arrastra igual por el wildcard **\*.* -- PluginManager nunca busca ahí, solo en
    # la carpeta de versión directa.
    if ($archivo.DirectoryName -match '\\publish(\\|$)') { return $true }
    if ($juncExtensions -contains $archivo.Extension) { return $true }
    foreach ($parte in $juncNameParts) {
        if ($archivo.Name.EndsWith($parte, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

Write-Host "== Deteniendo PortalSaas.Host si está corriendo ==" -ForegroundColor Cyan
# Mismo criterio que build-all.ps1 -- un Host local corriendo (dotnet run o el .exe
# apphost) bloquea los DLLs de artifacts/plugins y hace fallar PublicarComoPlugin con
# MSB3027 ("being used by another process").
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='PortalSaas.Host.exe'" |
    Where-Object { $_.Name -eq 'PortalSaas.Host.exe' -or ($_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll')) } |
    ForEach-Object {
        Write-Host "Deteniendo PID $($_.ProcessId) ($($_.Name))"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
Start-Sleep -Seconds 1

Write-Host "== Limpiando dist/ ==" -ForegroundColor Cyan
if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "== Publicando PortalSaas.Host (Release, framework-dependent) ==" -ForegroundColor Cyan
dotnet publish $hostProject -c Release -o $distDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "Falló el publish de PortalSaas.Host" }

Write-Host "== Compilando plugins (refresca artifacts/plugins) ==" -ForegroundColor Cyan
foreach ($proj in $pluginProjects) {
    $nombre = (Get-Item $proj).BaseName
    Write-Host "-- $nombre --" -ForegroundColor DarkCyan
    dotnet build $proj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Falló el build de $nombre" }
}

Write-Host "== Copiando artifacts/plugins a dist/ (sin archivos de basura) ==" -ForegroundColor Cyan
if (-not (Test-Path $artifactsPlugins)) {
    throw "No existe $artifactsPlugins -- el build de los plugins no generó nada, revisar PublicarComoPlugin"
}

$destinoPlugins = "$distDir\artifacts\plugins"
New-Item -ItemType Directory -Path $destinoPlugins -Force | Out-Null

$copiados = 0
$excluidos = 0

function Copy-PluginTree([string]$carpetaFuente, [string]$prefijoRelativo) {
    Get-ChildItem -Path $carpetaFuente -Recurse -File | ForEach-Object {
        if (Test-EsBasuraDePlugin $_) {
            $script:excluidos++
            return
        }
        $relativo = Join-Path $prefijoRelativo $_.FullName.Substring($carpetaFuente.Length + 1)
        $destino = Join-Path $destinoPlugins $relativo
        New-Item -ItemType Directory -Path (Split-Path $destino -Parent) -Force | Out-Null
        Copy-Item -Path $_.FullName -Destination $destino -Force
        $script:copiados++
    }
}

# Modulo.Rendiciones/Modulo.GestionDistribucionGastos (plugins EXTERNOS, ver
# "Portal SaaS - Plugins") no viven de verdad bajo artifacts/plugins -- son junctions
# NTFS hacia el dist/ de su propio repo (ver publish-dist.ps1 de cada uno). Windows
# PowerShell 5.1 NO desciende dentro de un reparse point con Get-ChildItem -Recurse
# (a diferencia de pwsh 7+, que sí con -FollowSymlink) -- se detiene en la junction
# como si fuera un archivo hoja, así que estos dos módulos quedaban SIEMPRE afuera del
# paquete publicado sin ningún error ni warning (bug real encontrado 2026-08-11: el
# paquete "Listo" se veía exitoso con los 5 módulos internos pero mudo para los 2
# externos -- el runbook de IIS documenta 6 módulos esperados). Se resuelve el target
# real de cada junction (Get-Item .Target) y se copia desde ahí explícitamente.
Get-ChildItem -Path $artifactsPlugins -Directory | ForEach-Object {
    if ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        $target = (Get-Item $_.FullName).Target
        if (-not $target -or -not (Test-Path $target)) {
            throw "$($_.FullName) es una junction rota (target: '$target') -- corré el publish-dist.ps1 del plugin externo correspondiente antes de este script."
        }
        Write-Host "$($_.Name) es una junction -> copiando desde $target" -ForegroundColor DarkGray
        Copy-PluginTree -carpetaFuente $target -prefijoRelativo $_.Name
    } else {
        Copy-PluginTree -carpetaFuente $_.FullName -prefijoRelativo $_.Name
    }
}
Write-Host "Copiados: $copiados archivo(s). Excluidos (basura): $excluidos archivo(s)." -ForegroundColor DarkGray

Write-Host "== Verificando que cada módulo tenga su DLL principal ==" -ForegroundColor Cyan
$faltantes = @()
Get-ChildItem -Path $destinoPlugins -Directory | ForEach-Object {
    $moduloNombre = $_.Name
    $carpetaVersion = Get-ChildItem -Path $_.FullName -Directory | Select-Object -First 1
    if ($null -eq $carpetaVersion) {
        $faltantes += "$moduloNombre (sin carpeta de versión)"
        return
    }
    $dllEsperada = Join-Path $carpetaVersion.FullName "$moduloNombre.dll"
    if (-not (Test-Path $dllEsperada)) {
        $faltantes += "$moduloNombre (falta $dllEsperada)"
    }
}

if ($faltantes.Count -gt 0) {
    Write-Host "== FALTAN DLLs principales -- dist/ quedaría incompleto ==" -ForegroundColor Red
    $faltantes | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Verificación de plugins falló, ver detalle arriba"
}

Write-Host "== Listo: $distDir ==" -ForegroundColor Green
Write-Host "Contiene el Host publicado + artifacts/plugins limpio. Copiar esta carpeta" -ForegroundColor Green
Write-Host "al servidor (ver docs/10-RUNBOOK-IIS-PILOTO.md) -- web.config con las" -ForegroundColor Green
Write-Host "connection strings/claves de cada rol sigue siendo manual a propósito." -ForegroundColor Green
