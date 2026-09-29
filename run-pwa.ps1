# Levanta el servidor de desarrollo (Vite) de la PWA de Auditoría de
# Inventario y deja a la vista las credenciales de prueba para loguearse
# contra la compañía DEPOR_TEST -- evita tener que ir a buscarlas cada vez.
#
# Requiere el Host corriendo aparte (ver build-all.ps1, instancia "Comercial
# Depor (OnPremise)" en localhost:6002) -- esta PWA proxea /api contra ese
# puerto (ver vite.config.ts).
#
# IMPORTANTE: corré esto SIEMPRE desde una terminal de Windows nativa
# (PowerShell, Windows Terminal, cmd) -- nunca desde una terminal WSL/Linux
# integrada en VS Code. Ahí "localhost"/127.0.0.1 apunta al loopback de la
# VM de Linux, no al de Windows donde corre el Host, y el login revienta con
# "AggregateError [ECONNREFUSED]" aunque el Host esté arriba y respondiendo
# (encontrado 2026-09-22, costó una sesión entera de diagnóstico hasta
# confirmarlo probando la misma terminal fuera de VS Code).
#
# Uso: .\run-pwa.ps1

$ErrorActionPreference = "Stop"
$pwaDir = Join-Path $PSScriptRoot "Portal SaaS - Plugins\Modulo.AuditoriaInventario.PWA"

if (-not (Test-Path $pwaDir)) {
    throw "No se encontró la carpeta de la PWA en $pwaDir"
}

Set-Location $pwaDir

# node_modules puede faltar en un checkout fresco o recién mergeado.
if (-not (Test-Path "node_modules")) {
    Write-Host "== Instalando dependencias (node_modules no existía) ==" -ForegroundColor Cyan
    npm install
    if ($LASTEXITCODE -ne 0) { throw "Falló npm install" }
}

# vite.config.js/.d.ts y los *.tsbuildinfo son artefactos que deja `npm run
# build` (tsc -b compila vite.config.ts a .js de paso) -- Vite resuelve .js
# ANTES que .ts, así que un vite.config.js viejo pisa en silencio cualquier
# cambio real hecho en vite.config.ts (mismo bug real de 2026-09-22 de
# arriba: la PWA seguía apuntando al puerto 5270 en vez de 6002 sin ningún
# error visible). Se limpian ANTES de cada arranque para que esto no vuelva
# a pasar.
@("vite.config.js", "vite.config.d.ts", "tsconfig.node.tsbuildinfo", "tsconfig.tsbuildinfo") | ForEach-Object {
    if (Test-Path $_) {
        Write-Host "Borrando artefacto de build viejo: $_" -ForegroundColor DarkYellow
        Remove-Item $_ -Force
    }
}

Write-Host ""
Write-Host "== PWA Auditoría de Inventario -- credenciales de prueba (DEPOR_TEST) ==" -ForegroundColor Green
Write-Host "  Empresa:     DEPOR_TEST"
Write-Host "  Usuario:     capturatest"
Write-Host "  Contraseña:  Captura-Test-2026!"
Write-Host ""
Write-Host "Si el Host todavía no está arriba, corré esto en OTRA terminal:" -ForegroundColor Yellow
Write-Host "  .\build-all.ps1"
Write-Host ""

npm run dev
