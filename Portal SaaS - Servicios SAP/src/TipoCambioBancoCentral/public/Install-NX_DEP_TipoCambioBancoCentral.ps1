#Requires -RunAsAdministrator
<#
    Instala el Windows Service NX_DEP_TipoCambioBancoCentral.

    Convención de nombres de esta familia (ver CLAUDE.md de Servicios SAP): el nombre del
    servicio Windows es siempre "NX_DEP_<NombreDelServicio>" -- acá "TipoCambioBancoCentral",
    mismo nombre que el Program.cs del proyecto le pasa a AddWindowsService. Los dos deben
    coincidir exacto o el servicio arranca con un nombre distinto al que administra este
    script.

    Uso:
      .\Install-NX_DEP_TipoCambioBancoCentral.ps1 -BinaryPath "C:\ruta\publish\Servicios.TipoCambioBancoCentral.exe"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BinaryPath,

    [string]$ServiceName = "NX_DEP_TipoCambioBancoCentral",

    [string]$DisplayName = "NX_DEP - Tipo de Cambio Banco Central (SAP)",

    [string]$Description = "Sincroniza el Tipo de Cambio USD del Banco Central de Chile con SAP Business One (Servicios SAP / TipoCambioBancoCentral). No modificar la logica de negocio sin revisar CLAUDE.md.",

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
    throw "El servicio '$ServiceName' ya existe. Usá Uninstall-NX_DEP_TipoCambioBancoCentral.ps1 primero si querés reinstalarlo."
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
