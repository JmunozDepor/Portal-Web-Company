# Genera el dump completo de la base Postgres de desarrollo (docker-compose.yml,
# contenedor "portalsaas-saas-dev-db") para usarlo como inicializacion del
# contenedor de PRODUCCION (docker-compose.prod.yml). Ver
# docs/10-DOCKER-POSTGRES-PRODUCCION.md.
#
# Uso: .\scripts\dump-postgres.ps1

$ErrorActionPreference = "Stop"

$container = "portalsaas-saas-dev-db"
$dbUser = "portalsaas"
$dbName = "portalsaas_saas_dev"
$outDir = Join-Path $PSScriptRoot "..\db\init"
$outFile = Join-Path $outDir "01-dump.sql"

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$running = docker ps --filter "name=$container" --format "{{.Names}}"
if ($running -ne $container) {
    Write-Error "El contenedor '$container' no esta corriendo. Levantalo con 'docker compose up -d' primero."
}

Write-Host "Generando dump de '$dbName' desde el contenedor '$container'..."
docker exec -u postgres $container pg_dump -U $dbUser -d $dbName --no-owner --no-privileges --clean --if-exists |
    Out-File -FilePath $outFile -Encoding utf8

Write-Host "Dump generado en: $outFile"
Write-Host "Este archivo NO se versiona (ver .gitignore) -- transferilo al servidor junto con docker-compose.prod.yml/.env.prod."
