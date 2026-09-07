<#
.SYNOPSIS
    Compila todo el monorepo y, si el build (y los tests) pasan, commitea y sube a git.

.DESCRIPTION
    1. Mata procesos PortalSaas.Host / dotnet colgados sobre este repo (locks de .dll).
    2. Limpia dist\ de los plugins externos.
    3. Compila plugins externos + Portal SaaS - Core, en orden, abortando al primer error.
    4. (opcional) Corre los tests.
    5. git add -A + commit + push.

    NO levanta el Host. Para levantar los ambientes usa build-all.ps1.

.PARAMETER Message
    Mensaje de commit. Si se omite se usa "chore: build + sync <timestamp>".

.PARAMETER Configuration
    Configuration de MSBuild para Core. Los plugins siempre van en Release
    (su target PublicarComoPlugin copia desde bin\Release a dist\). Default: Debug.

.PARAMETER SkipTests
    Salta la corrida de tests.

.PARAMETER NoPush
    Compila y commitea pero no hace push.

.PARAMETER NoCommit
    Solo compila (y testea). No toca git.

.EXAMPLE
    .\build-and-push.ps1 -Message "feat: nueva pantalla de conexiones"

.EXAMPLE
    .\build-and-push.ps1 -SkipTests -Message "fix rapido"
#>
[CmdletBinding()]
param(
    [string]$Message,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipTests,
    [switch]$NoPush,
    [switch]$NoCommit
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

function Step($t) { Write-Host "`n== $t ==" -ForegroundColor Cyan }
function Die($t)  { Write-Host "`nERROR: $t" -ForegroundColor Red; exit 1 }

# --- 0. sanity -------------------------------------------------------------
if (-not (Test-Path "$root\.git")) { Die "no estas en la raiz del repo git" }
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "Repo: $root" -ForegroundColor DarkGray
Write-Host "Rama: $branch" -ForegroundColor DarkGray

# --- 1. matar procesos que bloquean los .dll ----------------------------
Step "Deteniendo procesos de este repo (Host / dotnet colgados)"
$rootEscaped = [regex]::Escape($root)
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='PortalSaas.Host.exe'" |
    Where-Object {
        $_.Name -eq 'PortalSaas.Host.exe' -or
        ($_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll')) -or
        ($_.CommandLine -and $_.CommandLine -match $rootEscaped)
    } |
    ForEach-Object {
        Write-Host "  kill PID $($_.ProcessId) ($($_.Name))"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
Start-Sleep -Seconds 1

# --- 2. limpiar dist\ de los plugins externos --------------------------
Step "Limpiando dist\ de los plugins externos"
@(
    "$root\Portal SaaS - Plugins\Modulo.Rendiciones\dist\Modulo.Rendiciones",
    "$root\Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\dist\Modulo.GestionDistribucionGastos",
    "$root\Portal SaaS - Plugins\Modulo.SellOut\dist\Modulo.SellOut"
) | ForEach-Object {
    if (Test-Path $_) { Write-Host "  rm $_"; Remove-Item -Recurse -Force $_ }
}

# --- 3. compilar --------------------------------------------------------
# Mismo orden que build-all.ps1: los plugins primero (su output se copia al
# junction de Core\artifacts\plugins antes de compilar el Host).
$plugins = @(
    "Portal SaaS - Plugins\Modulo.Rendiciones\src\Modulo.Rendiciones\Modulo.Rendiciones.csproj",
    "Portal SaaS - Plugins\Modulo.GestionDistribucionGastos\src\Modulo.GestionDistribucionGastos\Modulo.GestionDistribucionGastos.csproj",
    "Portal SaaS - Plugins\Modulo.SellOut\src\Modulo.SellOut\Modulo.SellOut.csproj"
)
foreach ($p in $plugins) {
    Step "Compilando $($p.Split('\')[-1]) (Release)"
    dotnet build "$root\$p" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Die "fallo el build de $p" }
}

Step "Compilando Portal SaaS - Core ($Configuration)"
dotnet build "$root\Portal SaaS - Core\PortalSaas.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { Die "fallo el build de Portal SaaS - Core" }

# Modulo.Wms no esta en build-all.ps1; se compila aparte para no romperlo silenciosamente.
Step "Compilando Modulo.Wms ($Configuration)"
dotnet build "$root\Portal SaaS - Plugins\Modulo.Wms\src\Modulo.Wms\Modulo.Wms.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { Die "fallo el build de Modulo.Wms" }

# --- 4. tests ---------------------------------------------------------
if (-not $SkipTests) {
    $testProjects = @(
        "Portal SaaS - Core\PortalSaas.sln",
        "Portal SaaS - Plugins\Modulo.Rendiciones\src\Modulo.Rendiciones.Tests\Modulo.Rendiciones.Tests.csproj",
        "Portal SaaS - Plugins\Modulo.Wms\tests\Modulo.Wms.Tests\Modulo.Wms.Tests.csproj"
    )
    foreach ($t in $testProjects) {
        if (-not (Test-Path "$root\$t")) { continue }
        Step "Tests: $($t.Split('\')[-1])"
        dotnet test "$root\$t" -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { Die "fallaron tests en $t" }
    }
} else {
    Write-Host "`n(tests saltados por -SkipTests)" -ForegroundColor Yellow
}

# --- 5. git -----------------------------------------------------------
if ($NoCommit) {
    Step "Build OK. -NoCommit: no se toca git."
    exit 0
}

Step "git add + commit + push"
git add -A

$pending    = (git status --porcelain)
$aheadCount = (git rev-list --count "@{upstream}..HEAD" 2>$null)
if (-not $aheadCount) { $aheadCount = "0" }

if (-not $pending -and $aheadCount -eq "0") {
    Write-Host "Nada que commitear ni pushear. Todo sincronizado." -ForegroundColor Green
    exit 0
}

if ($pending) {
    if (-not $Message) {
        $Message = "chore: build + sync $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    }
    git commit -m $Message
    if ($LASTEXITCODE -ne 0) { Die "fallo git commit" }
    Write-Host "commit: $Message" -ForegroundColor Green
} else {
    Write-Host "sin cambios en working tree; hay $aheadCount commit(s) local(es) para pushear" -ForegroundColor DarkGray
}

if ($NoPush) {
    Write-Host "`n-NoPush: commit hecho, push pendiente." -ForegroundColor Yellow
    exit 0
}

$upstream = (git rev-parse --abbrev-ref --symbolic-full-name "@{upstream}" 2>$null)
if ($upstream) {
    git push
} else {
    Write-Host "la rama '$branch' no tiene upstream; usando 'origin $branch'" -ForegroundColor Yellow
    git push -u origin $branch
}
if ($LASTEXITCODE -ne 0) { Die "fallo git push" }

Write-Host "`nOK -- build, commit y push completos." -ForegroundColor Green
