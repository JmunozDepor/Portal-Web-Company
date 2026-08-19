#Requires -RunAsAdministrator
<#
    Instala el Windows Service NX_DEP_TransferenciaAutomatica.

    Convención de nombres de esta familia (ver CLAUDE.md de Servicios SAP): el nombre del
    servicio Windows es siempre "NX_DEP_<NombreDelServicio>" -- acá "TransferenciaAutomatica",
    mismo nombre que el Program.cs del proyecto le pasa a AddWindowsService. Los dos deben
    coincidir exacto o el servicio arranca con un nombre distinto al que administra este
    script.

    Este script vive en la raíz de la carpeta de publish (dotnet publish lo copia junto al
    .exe -- ver el ItemGroup de public\*.ps1 en el .csproj), así que por default asume que
    el ejecutable está al lado suyo, sin pedir una ruta a mano. Basta con copiar toda la
    carpeta de publish al servidor del cliente y correr este script ahí mismo.

    Uso:
      .\NX_DEP_TransferenciaAutomatica_v2-Install.ps1
      .\NX_DEP_TransferenciaAutomatica_v2-Install.ps1 -BinaryPath "C:\otra\ruta\Servicios.TransferenciaAutomatica_v2.exe"
#>
param(
    [string]$BinaryPath = (Join-Path $PSScriptRoot "Servicios.TransferenciaAutomatica_v2.exe"),

    [string]$ServiceName = "NX_DEP_TransferenciaAutomatica_v2",

    [string]$DisplayName = "NX_DEP - Transferencia Automatica de Stock v2 (SAP)",

    [string]$Description = "Transferencia automatica de stock entre bodegas en SAP Business One (Servicios SAP / TransferenciaAutomatica_v2). No modificar la logica de negocio sin revisar CLAUDE.md.",

    [ValidateSet("Automatic", "Manual", "Disabled")]
    [string]$StartupType = "Automatic",

    [string]$RunAsAccount,

    [securestring]$RunAsPassword
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BinaryPath)) {
    throw "No se encontró el ejecutable en '$BinaryPath'. Publicá el proyecto primero (dotnet publish -c Release -r win-x64)."
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "El servicio '$ServiceName' ya existe. Usá NX_DEP_TransferenciaAutomatica_v2-Uninstall.ps1 primero si querés reinstalarlo."
}

$newServiceParams = @{
    Name           = $ServiceName
    BinaryPathName = $BinaryPath
    DisplayName    = $DisplayName
    Description    = $Description
    StartupType    = $StartupType
}

if ($RunAsAccount) {
    if (-not $RunAsPassword) {
        throw "Si especificás -RunAsAccount también tenés que pasar -RunAsPassword."
    }
    $newServiceParams["Credential"] = New-Object System.Management.Automation.PSCredential($RunAsAccount, $RunAsPassword)
}

New-Service @newServiceParams | Out-Null

Write-Host "Servicio '$ServiceName' instalado (StartupType=$StartupType). Iniciándolo..."
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName | Format-Table -AutoSize
