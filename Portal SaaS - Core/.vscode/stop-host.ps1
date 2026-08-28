Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains('PortalSaas.Host.dll') } |
    ForEach-Object {
        Write-Host "Deteniendo PID $($_.ProcessId)"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
