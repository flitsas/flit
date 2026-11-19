<#
.SYNOPSIS
  Apaga los procesos que arrancó levantar.ps1, POR PID EXACTO (nunca por nombre ni por puerto).
  RabbitMQ (Docker) y Postgres NO se tocan.
#>
$PidFile = Join-Path $PSScriptRoot '.pids.json'
if (-not (Test-Path $PidFile)) { Write-Host 'No hay .pids.json: nada que apagar.'; return }
$mapa = Get-Content $PidFile -Raw | ConvertFrom-Json
foreach ($prop in $mapa.PSObject.Properties) {
  $id = [int]$prop.Value
  if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
    # /T cierra los hijos de ESE PID (el servidor de Next arranca un proceso hijo); sigue siendo por PID.
    & taskkill.exe /PID $id /T /F | Out-Null
    Write-Host "Cerrado $($prop.Name) (PID $id)"
  } else { Write-Host "$($prop.Name) (PID $id) ya no corría" }
}
Remove-Item $PidFile -Force
