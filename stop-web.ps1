# Detiene cualquier PortalSaas.Host corriendo localmente (dotnet run o el .exe
# apphost) para liberar los DLLs que bloquea -- mismo criterio que ya usa
# publish-all.ps1 internamente (ver el bloque "Deteniendo PortalSaas.Host si está
# corriendo" ahí), separado acá como script standalone para poder detener el sitio
# ANTES de correr publish-all.ps1, dotnet build/test sobre PortalSaas.Host o sobre un
# plugin externo (Modulo.Rendiciones/Modulo.Wms/Modulo.GestionDistribucionGastos, cuyo
# publish-dist.ps1 copia a dist/ del repo del plugin -- el mismo tipo de bloqueo
# MSB3027 "being used by another process" que sufre publish-all.ps1 contra este repo).
#
# Uso: .\stop-web.ps1

$ErrorActionPreference = "Stop"

Write-Host "== Buscando procesos PortalSaas.Host ==" -ForegroundColor Cyan

$procesos = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='PortalSaas.Host.exe'" |
    Where-Object { $_.Name -eq 'PortalSaas.Host.exe' -or ($_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll')) }

if (-not $procesos) {
    Write-Host "No hay ningún PortalSaas.Host corriendo." -ForegroundColor Green
    exit 0
}

foreach ($p in $procesos) {
    Write-Host "Deteniendo PID $($p.ProcessId) ($($p.Name))"
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 1
Write-Host "Listo -- sitio detenido." -ForegroundColor Green
