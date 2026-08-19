#Requires -RunAsAdministrator
<#
    Desinstala el Windows Service NX_DEP_TipoCambioBancoCentral.

    Detiene el servicio (si está corriendo) y lo elimina con sc.exe -- Remove-Service
    recién existe desde PowerShell 6+, y estos servicios corren sobre Windows PowerShell
    5.1 en los servidores del cliente, así que se usa sc.exe para no depender de la
    versión de PowerShell instalada.

    Uso:
      .\Uninstall-NX_DEP_TipoCambioBancoCentral.ps1
#>
param(
    [string]$ServiceName = "NX_DEP_TipoCambioBancoCentral"
)

$ErrorActionPreference = "Stop"

$servicio = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $servicio) {
    Write-Host "El servicio '$ServiceName' no está instalado -- nada que hacer."
    return
}

if ($servicio.Status -ne "Stopped") {
    Write-Host "Deteniendo '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    $servicio.WaitForStatus("Stopped", (New-TimeSpan -Seconds 30))
}

Write-Host "Eliminando '$ServiceName'..."
sc.exe delete $ServiceName | Out-Null

Write-Host "Servicio '$ServiceName' desinstalado."
