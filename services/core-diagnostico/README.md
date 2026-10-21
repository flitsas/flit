# core-diagnostico — Diagnóstico

Servicio (API .NET) de **Diagnóstico Vehicular** en la FLIT Suite, producto `diagnostico` del
[contrato de plataforma v1](../../docs/suite/contrato-plataforma-v1.md). Generado con la plantilla `flit-servicio`; valida los
tokens de la suite por JWKS sin llamar a otro servicio y guarda sus datos en su propio schema `diagnostico`, que migra al
arrancar. Diseño: [plan v2](../../docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md).

## Estado

REST bajo `/api/v1/diagnostico` (contrato: [diagnostico.v1.yaml](../../contracts/openapi/diagnostico.v1.yaml)). Toda ruta de
negocio pasa por el guard de suscripción (fail-closed) y exige un permiso del manifiesto `diagnostico.*`.

| Módulo | Rutas | Qué hace |
|---|---|---|
| Sesion | `GET /sesion` | Usuario, compañía, roles, permisos y capacidades, leídos del token. |
| Configuracion | `GET/PUT /configuracion` | Configuración de la compañía en dos niveles (contractual y operativo). |
| Backoffice | `/backoffice/...` | Lectura entre compañías del backoffice FLIT (empresa explícita y auditada) y edición del nivel contractual. |
| Carga | `GET /plantilla`, `POST /diagnosticos/{id}/carga` | Plantilla .xlsx y análisis local del archivo (.xlsx o .csv) en un resumen de validación, sin guardar filas ni consultar fuentes; tope `Diagnostico:Carga:TamanoMaximoBytes` (5 MB). |
| Vehiculos | `POST /diagnosticos/{id}/carga/validar-filas`, `POST /diagnosticos/{id}/vehiculos` | Revalida filas corregidas con el mismo análisis de la carga (sin guardar) y confirma el lote: el servidor revalida todo, guarda solo los válidos (documento del locatario cifrado), el estimado en la cabecera, y entrega la lista al motor de consultas en la misma transacción. |
| Catalogos | `GET /tipos-tramite` | Tipos de trámite de la etapa, por el gRPC `ListarTiposTramite` de core-api (`Diagnostico:Tramites:Address`), con caché de 5 min y 503 + `Retry-After` si Trámites no responde. |

## Puertos

| | Local | DEV / QA / PDN |
|---|---|---|
| API REST | 4029 | interna, detrás del gateway |
| gRPC (solo HTTP/2) | 8087 | interno, sin publicar |
| App web `frontend-diagnostico` | 4024 | 4024 / 5024 / 6024 |

Configuración local: copiar `src/Flit.Diagnostico.Api/appsettings.Development.json.example` a `appsettings.Development.json`.

### Certificado de Data Protection (obligatorio)

Los documentos de propietario y locatario se cifran con ASP.NET Data Protection (HU #13751). El anillo de llaves vive en
`diagnostico.data_protection_keys`, cifrado con un certificado que **no** está en la base ni en ningún `appsettings*.json`.
Sin él el servicio no arranca, en cualquier ambiente. Se configura con dos variables de entorno:

| Variable | Qué es |
|---|---|
| `Diagnostico__DataProtection__CertificadoRuta` | Ruta del `.pfx` (RSA, con llave privada), montado como secreto. |
| `Diagnostico__DataProtection__CertificadoClave` | Clave del `.pfx`. |

En local, genera uno fuera del repo y exporta las variables antes de `dotnet run`:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=flit-diagnostico-local" -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out ~/.flit/diagnostico-dp.pfx -passout pass:<clave-local>
export Diagnostico__DataProtection__CertificadoRuta=~/.flit/diagnostico-dp.pfx
export Diagnostico__DataProtection__CertificadoClave=<clave-local>
```

Perder o cambiar el certificado deja ilegibles los documentos ya cifrados: en los ambientes es un secreto con respaldo, y
al rotarlo el certificado anterior debe seguir disponible para descifrar. Las pruebas generan uno efímero por proceso
(`tests/Flit.Diagnostico.Tests/Seguridad/CertificadoDePrueba.cs`).

### Fuente RUNT del vehículo (simulada o no conectada)

La fuente RUNT (`IFuenteRunt`, HU #13725) se elige **solo por configuración**; no se mira `ASPNETCORE_ENVIRONMENT`, porque los
tres ambientes corren como `Development`.

| Variable | Por defecto | Qué hace |
|---|---|---|
| `Diagnostico__Simulacion__Habilitada` | `false` | `true` usa la fuente simulada de Intempo (fixtures inventados en `Modulos/Consultas/Fuentes/Fixtures/`). Con `false` responde la fuente **no conectada**: error definitivo `NoConectado`, sin datos ni llamadas a proveedores. |
| `Diagnostico__Ambiente` | vacío | `LOCAL`, `DEV`, `QA` o `PDN`. La simulación solo se admite con `LOCAL` o `DEV`; con otro valor, o vacío, el servicio **no arranca**. |

En QA y PDN no se define `Diagnostico__Simulacion__Habilitada` hasta que llegue el adaptador real sobre core-consultas (C-PER).
La simulación es determinista por el último carácter de la placa o del VIN: 1 persona natural, 2 NIT, 3 leasing con
locatario C, 4 leasing con locatario N, 5 servicio público, 6 organismo sin departamento soportado, 7 error transitorio
(Timeout), 8 error definitivo (RespuestaInvalida); cualquier otro, persona natural. Los fixtures solo usan placas `TST###`,
VIN `9ZZPRUEBA########`, documentos `99999` + 4 o 5 dígitos y nombres de una lista ficticia (`FixturesSinPiiTests`).

## Pruebas

Corren `Program.Build` real sobre una base efímera de PostgreSQL que crean y borran. Necesitan `ConnectionStrings__Core`
apuntando al servidor (sin base): usa las credenciales de desarrollo del servicio `postgres` de
[docker-compose.yml](../../docker-compose.yml). Sin esa variable se omiten en local y fallan en CI.

```bash
export ConnectionStrings__Core="Host=127.0.0.1;Port=5432;Username=<usuario de docker-compose.yml>;Password=<contraseña de docker-compose.yml>"
dotnet build tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj
dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~TiposTramite"
```

Esa variable es solo para estas pruebas: no la exportes al correr las de core-api.

## Decisiones y despliegue

- [ADR-0072](../../docs/decisions/ADR-0072-diagnostico-aislamiento-por-tenant.md): aislamiento por compañía (filtro global y prueba de fuga por GET).
- [ADR-0073](../../docs/decisions/ADR-0073-diagnostico-acceso-backoffice.md): acceso del backoffice FLIT.
- [ADR-0074](../../docs/decisions/ADR-0074-diagnostico-puerta-minima-de-plataforma.md): puerta mínima de plataforma.
- [Handoff del VPS](../../docs/suite/handoff-vps-diagnostico.md): despliegue en los ambientes.
