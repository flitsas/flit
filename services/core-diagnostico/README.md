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
| Catalogos | `GET /tipos-tramite` | Tipos de trámite de la etapa, por el gRPC `ListarTiposTramite` de core-api (`Diagnostico:Tramites:Address`), con caché de 5 min y 503 + `Retry-After` si Trámites no responde. |

## Puertos

| | Local | DEV / QA / PDN |
|---|---|---|
| API REST | 4029 | interna, detrás del gateway |
| gRPC (solo HTTP/2) | 8087 | interno, sin publicar |
| App web `frontend-diagnostico` | 4024 | 4024 / 5024 / 6024 |

Configuración local: copiar `src/Flit.Diagnostico.Api/appsettings.Development.json.example` a `appsettings.Development.json`.

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
