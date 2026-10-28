<#
.SYNOPSIS
  Levanta EN LOCAL el conjunto completo del portal Persona Natural (HU #14069, Épica #14021) con piezas REALES:
  core-api, core-consultas, core-notificaciones, core-personanatural (BFF), gateway YARP y el front del portal.
  Postgres nativo y RabbitMQ (Docker, deploy/rabbitmq/docker-compose.local.yml) deben estar arriba.

.DESCRIPTION
  Todo externo va simulado: biometría mock, consultas RUNT/Verifik mock, SMTP a consola, captcha deshabilitado y
  file-manager simulado (stub-filemanager.mjs). Todos los secretos de este archivo son FICTICIOS, solo para local.
  Guarda los PID exactos en .pids.json (junto a este script) para que apagar.ps1 los cierre POR PID.

  Uso (desde la raíz del repo):
    powershell -ExecutionPolicy Bypass -File scripts/e2e-personanatural/levantar.ps1 [-Base flit_pn_e2e] [-LogDir <ruta>] [-Compilar]
  Ver docs/suite/local-personanatural.md.
#>
#Requires -Version 7
param(
  [string]$Base = 'flit_pn_e2e',
  [string]$LogDir = (Join-Path $env:TEMP 'flit-pn-e2e-logs'),
  [string]$PgClave = 'flitdb',
  [switch]$Compilar,
  [switch]$SinFront
)

$ErrorActionPreference = 'Stop'
$Raiz = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$PidFile = Join-Path $PSScriptRoot '.pids.json'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# ---------- Valores FICTICIOS de prueba (nunca reales) ----------
$Psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$Cadena = "Host=127.0.0.1;Port=5432;Database=$Base;Username=postgres;Password=$PgClave"
$Sec = @{
  tramites       = 'pn-e2e-secreto-svc-tramites-0000000000001'
  consultas      = 'pn-e2e-secreto-svc-consultas-000000000001'
  notificaciones = 'pn-e2e-secreto-svc-notificaciones-0000001'
  personanatural = 'pn-e2e-secreto-svc-personanatural-00000001'
}
$Mq = @{
  tramites       = 'amqp://tramites:pn-e2e-tramites-clave-local-0001@127.0.0.1:5672/flit'
  personanatural = 'amqp://personanatural:pn-e2e-pn-clave-local-0001@127.0.0.1:5672/flit'
  consultas      = 'amqp://consultas:pn-e2e-consultas-clave-local-0001@127.0.0.1:5672/flit'
  notificaciones = 'amqp://notificaciones:pn-e2e-notif-clave-local-0001@127.0.0.1:5672/flit'
}
# Emisor de los tokens de servicio: el que arma core-api con los Overrides de Suite (hub en 4022, aunque no corra).
$Emisor = 'http://127.0.0.1:4022/'
$PiiHmac = 'pn-e2e-clave-hmac-ficticia-de-prueba-0123456789abcdef'
$PiiAes = [Convert]::ToBase64String([byte[]](1..32))   # 32 bytes ficticios

$Pids = [ordered]@{}
if (Test-Path $PidFile) {   # re-ejecutar no pierde los PID de lo que ya corre
  foreach ($prop in (Get-Content $PidFile -Raw | ConvertFrom-Json).PSObject.Properties) { $Pids[$prop.Name] = [int]$prop.Value }
}

function Set-Entorno([hashtable]$vars) { foreach ($k in $vars.Keys) { [Environment]::SetEnvironmentVariable($k, [string]$vars[$k], 'Process') } }
# Remove-Item (no SetEnvironmentVariable con $null): en Windows esto deja la variable VACIA y una variable vacia pisa el appsettings del siguiente servicio.
function Clear-Entorno([hashtable]$vars) { foreach ($k in $vars.Keys) { Remove-Item -LiteralPath "Env:$k" -ErrorAction SilentlyContinue } }

function Esperar-Http([string]$url, [int]$segundos = 120, [switch]$CualquierRespuesta) {
  $limite = (Get-Date).AddSeconds($segundos)
  while ((Get-Date) -lt $limite) {
    try {
      $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5 -SkipHttpErrorCheck
      if ($CualquierRespuesta -or $r.StatusCode -lt 400) { return $true }
    } catch { }
    Start-Sleep -Seconds 2
  }
  return $false
}

function Iniciar([string]$nombre, [string]$dir, [string]$exe, [string[]]$argumentos, [hashtable]$vars, [string]$ready, [int]$espera = 120) {
  Write-Host "== $nombre" -ForegroundColor Cyan
  if ($Pids.Contains($nombre) -and (Get-Process -Id $Pids[$nombre] -ErrorAction SilentlyContinue)) { Write-Host "   ya corre (PID $($Pids[$nombre]))" -ForegroundColor Yellow; return }
  Set-Entorno $vars
  $out = Join-Path $LogDir "$nombre.out.log"; $err = Join-Path $LogDir "$nombre.err.log"
  $p = Start-Process -FilePath $exe -ArgumentList $argumentos -WorkingDirectory $dir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $out -RedirectStandardError $err
  Clear-Entorno $vars
  $Pids[$nombre] = $p.Id
  ($Pids | ConvertTo-Json) | Set-Content -Path $PidFile -Encoding utf8     # se guarda al instante: apagar.ps1 siempre sabe qué cerrar
  if ($ready) {
    if (-not (Esperar-Http $ready $espera -CualquierRespuesta:($ready -notmatch '/health/'))) {
      Write-Host "   NO respondió en ${espera}s. Mira $out y $err" -ForegroundColor Red
      throw "$nombre no arrancó"
    }
  }
  Write-Host "   PID $($p.Id) listo ($ready)" -ForegroundColor Green
}

if ($Compilar) {
  foreach ($p in 'core-api\src\Flit.Api', 'core-api\src\Flit.Gateway', 'core-consultas\src\Flit.Consultas.Api',
                 'core-notificaciones\src\Flit.Notificaciones.Api', 'core-personanatural\src\Flit.PersonaNatural.Api') {
    Write-Host "dotnet build $p" -ForegroundColor DarkGray
    dotnet build (Join-Path $Raiz "services\$p") -c Debug -v q | Select-Object -Last 3
  }
}

# Base nueva (si no existe) y empresa «Persona Natural» (idempotente) - la empresa necesita las tablas que migra core-api,
# por eso se crea justo después de que core-api arranque (ver más abajo).
$env:PGPASSWORD = $PgClave
$existe = & $Psql -h 127.0.0.1 -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Base'"
if (-not $existe) { & $Psql -h 127.0.0.1 -U postgres -c "CREATE DATABASE $Base" | Out-Null }

# ---------- 0. File-manager simulado (el real es S3 corporativo: PROHIBIDO tocarlo) ----------
Iniciar 'stub-filemanager' $PSScriptRoot 'node' @('stub-filemanager.mjs') @{ STUB_FM_PORT = '4999' } 'http://127.0.0.1:4999/healthz' 30

# ---------- 0b. Observador pasivo del bus (colas efimeras; solo lee) ----------
Iniciar 'observar-bus' $Raiz 'dotnet' @('run', ("`"" + (Join-Path $PSScriptRoot 'observar-bus.cs') + "`""), '--', ("`"" + (Join-Path $LogDir 'bus-mensajes.log') + "`"")) @{} $null

# ---------- 1. core-api (migra la base, emite tokens de servicio, fachada gRPC, consumidor) ----------
$api = "$Raiz\services\core-api\src\Flit.Api\bin\Debug\net10.0"
Iniciar 'core-api' $api 'dotnet' @('Flit.Api.dll') @{
  ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://127.0.0.1:4003'
  ConnectionStrings__Core = $Cadena
  Ict__GrpcPort = '8082'
  Suite__Oidc__Enabled = 'true'
  Suite__Hosts__Overrides__plataforma = 'http://127.0.0.1:4022'
  Suite__Hosts__Overrides__tramites = 'http://127.0.0.1:3000'
  'Suite__Oidc__ServiceClients__svc-tramites__Secret' = $Sec.tramites
  'Suite__Oidc__ServiceClients__svc-consultas__Secret' = $Sec.consultas
  'Suite__Oidc__ServiceClients__svc-notificaciones__Secret' = $Sec.notificaciones
  'Suite__Oidc__ServiceClients__svc-personanatural__Secret' = $Sec.personanatural
  Consultas__Remoto__Address = 'http://127.0.0.1:8084'
  Notificaciones__Remoto__Address = 'http://127.0.0.1:8085'
  Tramites__Bus__Habilitado = 'true'
  Platform__Messaging__Producer = 'tramites'
  Platform__Messaging__ConnectionString = $Mq.tramites
  Platform__ServiceClient__ClientId = 'svc-tramites'
  Platform__ServiceClient__ClientSecret = $Sec.tramites
  Platform__ServiceClient__TokenEndpoint = 'http://127.0.0.1:4003/connect/token'
  Biometrics__Provider = 'mock'
  FileManager__BaseUrl = 'http://127.0.0.1:4999/'
  PersonaNatural__Fachada__Habilitada = 'true'
  PersonaNatural__Consumidor__Habilitado = 'true'
  PersonaNatural__Portal__Address = 'http://127.0.0.1:8087'
} 'http://127.0.0.1:4003/health/ready' 300

# Empresa «Persona Natural» (idempotente) con la base ya migrada. SIEMPRE antes de encender el recorrido.
& $Psql -h 127.0.0.1 -U postgres -d $Base -v ON_ERROR_STOP=1 -q -f (Join-Path $Raiz 'deploy\postgres\crear-empresa-persona-natural.sql')

# ---------- 2. core-consultas (todo en mock) ----------
Iniciar 'core-consultas' "$Raiz\services\core-consultas\src\Flit.Consultas.Api\bin\Debug\net10.0" 'dotnet' @('Flit.Consultas.Api.dll') @{
  ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://127.0.0.1:4026'
  Servicio__GrpcPort = '8084'
  ConnectionStrings__Servicio = $Cadena
  Platform__Auth__JwksUri = 'http://127.0.0.1:4003/.well-known/jwks.json'
  Platform__Auth__Issuers__0 = $Emisor
  Platform__ServiceClient__ClientSecret = $Sec.consultas
  Platform__ServiceClient__TokenEndpoint = 'http://127.0.0.1:4003/connect/token'
  Platform__Messaging__ConnectionString = $Mq.consultas
  VERIFIK_VEHICLE_MODE = 'mock'; VERIFIK_CONDUCTOR_MODE = 'mock'; VERIFIK_SIMIT_MODE = 'mock'; VERIFIK_RNMC_MODE = 'mock'
  VERIFIK_RUES_MODE = 'mock'; FASECOLDA_MODE = 'mock'; INTEMPO_MODE = 'mock'; KYVERUM_FINES_MODE = 'mock'
  # Kyverum RUNT no tiene modo mock y encabeza la cadena por defecto: llave FICTICIA, direccion muerta (nada sale a
  # internet) y cadena que empieza por los proveedores con mock (verifik).
  KYVERUM_RUNT_API_KEY = 'pn-e2e-llave-ficticia-no-real'; KYVERUM_RUNT_BASE_URL = 'http://127.0.0.1:4998/'
  Consultations__DefaultChains__vehicle_vin__0 = 'verifik'
  Consultations__DefaultChains__vehicle_plate__0 = 'verifik'
  Consultations__DefaultChains__conductor__0 = 'verifik_conductor'
} 'http://127.0.0.1:4026/health/ready' 180

# ---------- 3. core-notificaciones (SMTP a consola) ----------
Iniciar 'core-notificaciones' "$Raiz\services\core-notificaciones\src\Flit.Notificaciones.Api\bin\Debug\net10.0" 'dotnet' @('Flit.Notificaciones.Api.dll') @{
  ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://127.0.0.1:4027'
  Servicio__GrpcPort = '8085'
  ConnectionStrings__Servicio = $Cadena
  Platform__Auth__JwksUri = 'http://127.0.0.1:4003/.well-known/jwks.json'
  Platform__Auth__Issuers__0 = $Emisor
  Platform__ServiceClient__ClientSecret = $Sec.notificaciones
  Platform__ServiceClient__TokenEndpoint = 'http://127.0.0.1:4003/connect/token'
  Platform__Messaging__ConnectionString = $Mq.notificaciones
  Smtp__Host = ' '; Smtp__UseConsoleWhenNoHost = 'true'
} 'http://127.0.0.1:4027/health/ready' 180

# ---------- 4. core-personanatural (BFF) ----------
Iniciar 'core-personanatural' "$Raiz\services\core-personanatural\src\Flit.PersonaNatural.Api\bin\Debug\net10.0" 'dotnet' @('Flit.PersonaNatural.Api.dll') @{
  ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://127.0.0.1:4029'
  Servicio__GrpcPort = '8087'
  ConnectionStrings__Servicio = $Cadena
  Platform__ServiceClient__ClientSecret = $Sec.personanatural
  Platform__ServiceClient__TokenEndpoint = 'http://127.0.0.1:4003/connect/token'
  Platform__Auth__JwksUri = 'http://127.0.0.1:4003/.well-known/jwks.json'
  Platform__Auth__Issuers__0 = $Emisor
  Platform__Messaging__ConnectionString = $Mq.personanatural
  Pii__ClaveHmac = $PiiHmac
  Pii__Cifrado__Llaves__1 = $PiiAes
  Captcha__Proveedor = 'deshabilitado'; Captcha__PermitirDeshabilitado = 'true'
  Consultas__Remoto__Address = 'http://127.0.0.1:8084'
  Cotizaciones__CoreApiAddress = 'http://127.0.0.1:8082'
  Cotizaciones__NotificacionesAddress = 'http://127.0.0.1:8085'
  FileManager__BaseUrl = 'http://127.0.0.1:4999/'
  Abuso__ConfiarEnXForwardedFor = 'true'
  # Topes holgados SOLO para repetir el recorrido muchas veces desde una misma IP local (en el servidor mandan los de appsettings).
  Abuso__LimiteIpPorMinuto = '600'; Abuso__LimiteCedulaPorHora = '500'; Abuso__TopeGlobalDiario = '100000'
  Sesion__MaxIntentosFallidos = '1000'
} 'http://127.0.0.1:4029/health/ready' 180

# ---------- 5. gateway YARP ----------
Iniciar 'gateway' "$Raiz\services\core-api\src\Flit.Gateway\bin\Debug\net10.0" 'dotnet' @('Flit.Gateway.dll') @{
  ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = 'http://127.0.0.1:4002'
  'ReverseProxy__Clusters__core-api-cluster__Destinations__core-api-1__Address' = 'http://127.0.0.1:4003/'
  'ReverseProxy__Clusters__core-consultas-cluster__Destinations__core-consultas-1__Address' = 'http://127.0.0.1:4026/'
  'ReverseProxy__Clusters__core-personanatural-cluster__Destinations__core-personanatural-1__Address' = 'http://127.0.0.1:4029/'
  Cors__AllowedOrigins__0 = 'http://127.0.0.1:4031'
} 'http://127.0.0.1:4002/api/v1/persona-natural/organismos' 60

# ---------- 6. front del portal ----------
if (-not $SinFront) {
  $front = Join-Path $Raiz 'frontend-personanatural'
  $next = (Resolve-Path (Join-Path $front 'node_modules\next\dist\bin\next')).Path
  Iniciar 'frontend-personanatural' $front 'node' @("`"$next`"", 'dev', '--port', '4031', '-H', '127.0.0.1') @{
    CORE_API_ORIGIN = 'http://127.0.0.1:4002'; CAPTCHA_PROVIDER = 'dev'; NEXT_TELEMETRY_DISABLED = '1'
  } 'http://127.0.0.1:4031/healthz' 180
}

Write-Host "`nListo. PIDs en $PidFile ; logs en $LogDir" -ForegroundColor Green
Get-Content $PidFile
