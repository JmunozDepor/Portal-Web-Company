# Compila Modulo.SellOut y deja dist/Modulo.SellOut/{version}/ limpio -- ese folder es
# el DESTINO de una junction real (ver `fsutil reparsepoint query` sobre
# "Portal SaaS - Core\artifacts\plugins\Modulo.SellOut") hacia
# "Portal SaaS - Core\artifacts\plugins\Modulo.SellOut", así que limpiar acá alcanza --
# no hace falta copiar nada al portal.
#
# A DIFERENCIA de los plugins internos de Core (Ventas/Compras/Inventario, que solo
# referencian PortalSaas.Abstractions): este módulo trae dependencias NuGet PROPIAS
# (EF Core SqlServer + Npgsql, motor dual obligatorio) con
# CopyLocalLockFileAssemblies=true. PluginLoadContext (Core/Infraestructura/) resuelve
# esas dependencias -- managed Y nativas (Microsoft.Data.SqlClient.SNI.dll) -- vía
# AssemblyDependencyResolver, que LEE Modulo.SellOut.deps.json. Por eso, a diferencia
# del script de Core, ACÁ *.deps.json NUNCA se borra -- borrarlo rompe la conexión a
# SQL Server/Postgres en el primer uso real.
#
# Lo que sí es basura real (nunca lo lee PluginManager/PluginLoadContext):
#   - *.pdb, *.xml (símbolos de debug / doc-comments)
#   - ref\ (assemblies de solo-compilación)
#   - *.runtimeconfig*.json (gobierna el lanzamiento de un .exe propio, irrelevante
#     para un assembly cargado por reflexión en el AppDomain del Host)
#   - publish\ anidado (residuo de un `dotnet publish` corrido antes a mano sobre el
#     plugin suelto, arrastrado por el wildcard **\*.* de PublicarComoPlugin)
#   - runtimes/{unix,win-arm,win-arm64,win-x86} -- el servidor real es SIEMPRE Windows
#     x64 IIS (ver PlatformTarget=x64 en el .csproj); solo runtimes/win (managed
#     genérico) y runtimes/win-x64 (nativo, incluye la SNI.dll de SqlClient) aplican.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$moduleName = "Modulo.SellOut"
$csproj = "$root\src\$moduleName\$moduleName.csproj"

$csprojContent = Get-Content $csproj -Raw
if ($csprojContent -notmatch '<ModuloVersion>([^<]+)</ModuloVersion>') {
    throw "No se encontró <ModuloVersion> en $csproj"
}
$version = $Matches[1]
$versionDir = "$root\dist\$moduleName\$version"

Write-Host "== Deteniendo PortalSaas.Host si está corriendo (bloquea los DLLs del plugin) ==" -ForegroundColor Cyan
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='PortalSaas.Host.exe'" |
    Where-Object { $_.Name -eq 'PortalSaas.Host.exe' -or ($_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll')) } |
    ForEach-Object {
        Write-Host "Deteniendo PID $($_.ProcessId) ($($_.Name))"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
Start-Sleep -Seconds 1

Write-Host "== Limpiando $versionDir ==" -ForegroundColor Cyan
if (Test-Path $versionDir) { Remove-Item -Recurse -Force $versionDir }

Write-Host "== Compilando $moduleName (Release) ==" -ForegroundColor Cyan
dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Falló el build de $moduleName" }

if (-not (Test-Path $versionDir)) {
    throw "No se generó $versionDir -- revisar el target PublicarComoPlugin en $csproj"
}

Write-Host "== Quitando archivos de basura (conservando *.deps.json) ==" -ForegroundColor Cyan
$juncExtensions = @(".pdb", ".xml")
$juncNameParts = @(".runtimeconfig.json", ".runtimeconfig.dev.json")
$ridsAWhitelistear = @("win", "win-x64")

$eliminados = 0
Get-ChildItem -Path $versionDir -Recurse -File | ForEach-Object {
    $esBasura = $false

    if ($_.DirectoryName -match '\\ref(\\|$)') { $esBasura = $true }
    if ($_.DirectoryName -match '\\publish(\\|$)') { $esBasura = $true }
    if ($juncExtensions -contains $_.Extension) { $esBasura = $true }
    foreach ($parte in $juncNameParts) {
        if ($_.Name.EndsWith($parte, [StringComparison]::OrdinalIgnoreCase)) { $esBasura = $true }
    }

    if ($_.DirectoryName -match '\\runtimes\\([^\\]+)\\') {
        $rid = $Matches[1]
        if ($ridsAWhitelistear -notcontains $rid) { $esBasura = $true }
    }

    if ($esBasura) {
        Remove-Item -Path $_.FullName -Force
        $eliminados++
    }
}

Get-ChildItem -Path $versionDir -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if ((Get-ChildItem -Path $_.FullName -Recurse -File | Measure-Object).Count -eq 0) {
        Remove-Item -Path $_.FullName -Recurse -Force
    }
}

Write-Host "Archivos eliminados: $eliminados" -ForegroundColor DarkGray

$dllPrincipal = Join-Path $versionDir "$moduleName.dll"
if (-not (Test-Path $dllPrincipal)) {
    throw "Falta $dllPrincipal tras la limpieza -- algo salió mal"
}
$depsJson = Join-Path $versionDir "$moduleName.deps.json"
if (-not (Test-Path $depsJson)) {
    throw "Falta $depsJson -- SIN esto, PluginLoadContext no puede resolver las dependencias NuGet propias del módulo (EF Core SqlServer/Npgsql) en runtime"
}

Write-Host "== Listo: $versionDir ==" -ForegroundColor Green
Write-Host "Ya visible en Core\artifacts\plugins\$moduleName vía la junction existente." -ForegroundColor Green
