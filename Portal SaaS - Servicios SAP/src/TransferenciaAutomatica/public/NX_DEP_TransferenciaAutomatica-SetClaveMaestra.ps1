#Requires -RunAsAdministrator
<#
    Setea Seguridad:ClaveMaestraSecretos (la clave AES-256 que ISecretoCifradoService usa
    para descifrar DbSecreto/ServiceLayerSecreto de appsettings.json) como variable de
    entorno scoped al servicio NX_DEP_TransferenciaAutomatica, vía el registro
    (HKLM\SYSTEM\CurrentControlSet\Services\<ServiceName>\Environment).

    Nunca en appsettings.json ni en texto plano en ningún archivo del publish (ver
    CLAUDE.md, regla dura de la familia) -- por eso va acá y no en la config. Se prefiere
    esta variable scoped al servicio en vez de una variable de entorno de máquina porque
    esta se aplica con solo reiniciar el servicio, no todo el servidor: el Service Control
    Manager recién relee el entorno de máquina en el boot, pero lee el de
    HKLM\...\Services\<ServiceName>\Environment cada vez que arranca ESE servicio puntual.

    El nombre de la variable usa doble guion bajo (Seguridad__ClaveMaestraSecretos) porque
    así traduce .NET la sección:clave de configuración cuando la fuente es una variable de
    entorno (AddEnvironmentVariables(), ya incluido por Host.CreateApplicationBuilder).

    Uso:
      .\NX_DEP_TransferenciaAutomatica-SetClaveMaestra.ps1 -ClaveMaestra "x0TvMEX6OFR5f3YJC+fChVP7RWin2TYKN67SQv/QGc0="
      .\NX_DEP_TransferenciaAutomatica-SetClaveMaestra.ps1 -ClaveMaestra "..." -NoReiniciar
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ClaveMaestra,

    [string]$ServiceName = "NX_DEP_TransferenciaAutomatica",

    [switch]$NoReiniciar
)

$ErrorActionPreference = "Stop"

try {
    $bytes = [Convert]::FromBase64String($ClaveMaestra)
}
catch {
    throw "ClaveMaestra no es un base64 válido."
}
if ($bytes.Length -ne 32) {
    throw "ClaveMaestra debe decodificar a 32 bytes (AES-256) -- se recibieron $($bytes.Length)."
}

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (-not (Test-Path $registryPath)) {
    throw "No existe el servicio '$ServiceName' en el registro. Instalalo primero con NX_DEP_TransferenciaAutomatica-Install.ps1."
}

$entornoActual = (Get-ItemProperty -Path $registryPath -Name Environment -ErrorAction SilentlyContinue).Environment
if (-not $entornoActual) { $entornoActual = @() }

# Reemplaza la entrada existente si ya había una, para no ir acumulando duplicados en
# reinstalaciones sucesivas de la clave.
$entornoNuevo = @($entornoActual | Where-Object { $_ -notlike "Seguridad__ClaveMaestraSecretos=*" })
$entornoNuevo += "Seguridad__ClaveMaestraSecretos=$ClaveMaestra"

Set-ItemProperty -Path $registryPath -Name Environment -Value $entornoNuevo -Type MultiString

Write-Host "Seguridad__ClaveMaestraSecretos seteada para el servicio '$ServiceName'."

if ($NoReiniciar) {
    Write-Host "No se reinició el servicio (-NoReiniciar) -- el cambio recién aplica en el próximo arranque."
    return
}

$servicio = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($servicio -and $servicio.Status -eq "Running") {
    Write-Host "Reiniciando '$ServiceName' para que tome la clave nueva..."
    Restart-Service -Name $ServiceName -Force
    Get-Service -Name $ServiceName | Format-Table -AutoSize
}
else {
    Write-Host "El servicio no está corriendo -- va a tomar la clave la próxima vez que arranque."
}
