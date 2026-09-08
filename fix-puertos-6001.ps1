# Libera / reserva 6001-6002 para Portal SaaS.
#
# Por qué: al arrancar Docker Desktop / WSL, Hyper-V (servicio "winnat") reserva
# rangos de puertos TCP dinámicos y uno de ellos (5940-6139) se quedó pisando
# 6001/6002 -> Kestrel falla con SocketException 10013 "acceso no permitido" y
# build-all.ps1 aborta con "Central no levantó en http://localhost:6001".
#
# Qué hace: para winnat, agrega 6001-6002 como exclusión PERSISTENTE (reservados
# para la app, así la asignación dinámica de Windows nunca más los toma, aunque
# vuelvas a levantar Docker o reinicies), y vuelve a arrancar winnat.
#
# Uso: clic derecho -> "Ejecutar con PowerShell" como administrador, o desde una
# consola PowerShell elevada:  .\fix-puertos-6001.ps1

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

Write-Host "== Antes ==" -ForegroundColor Cyan
netsh interface ipv4 show excludedportrange protocol=tcp

Write-Host "`n== net stop winnat ==" -ForegroundColor Cyan
net stop winnat

Write-Host "`n== Reservando 6001-6002 (persistente) ==" -ForegroundColor Cyan
netsh int ipv4 add excludedportrange protocol=tcp startport=6001 numberofports=2 store=persistent

Write-Host "`n== net start winnat ==" -ForegroundColor Cyan
net start winnat

Write-Host "`n== Después ==" -ForegroundColor Cyan
netsh interface ipv4 show excludedportrange protocol=tcp

Write-Host "`nListo. 6001-6002 deberían aparecer arriba como exclusión propia." -ForegroundColor Green
Write-Host "Ahora corré  build-all.ps1  normalmente." -ForegroundColor Green
