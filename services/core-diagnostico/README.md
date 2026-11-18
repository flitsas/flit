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
| API REST | 4032 | interna, detrás del gateway |
| gRPC (solo HTTP/2) | 8088 | interno, sin publicar |
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

### Consultas por RabbitMQ (ADR-0075)

Las consultas de las celdas (RUNT, RUNT persona, RUES, SIMIT, avalúo e impuesto) no llaman a nadie desde Diagnóstico: cada
celda publica por la outbox un trabajo `consultas.diagnostico[-express].<fuente>` con el `*Request` proto mínimo (placa o VIN,
tipo y número de documento cuando la fuente lo exige; nunca nombres ni direcciones), core-consultas lo atiende una sola vez y
publica `consultas.resultado.diagnostico`, y el consumidor de la cola `diagnostico.consultas-resultados` cierra la celda (los
reintentos de los transitorios los decide Diagnóstico: 5, 10 y 20 s). Un reconciliador cada 60 s republica una vez las celdas
huérfanas y luego las cierra en ERROR, y se pausa si core-consultas no devuelve resultados. La simulación de LOCAL y DEV vive en
core-consultas (U3): en local se levantan core-consultas con su simulación y el broker de `deploy/rabbitmq/docker-compose.local.yml`.
No se mira `ASPNETCORE_ENVIRONMENT`, porque los tres ambientes corren como `Development`.

| Variable | Por defecto | Qué hace |
|---|---|---|
| `Diagnostico__Consultas__Bus__ConsumidorHabilitado` | `true` (appsettings) | Registra el consumidor de resultados y el reconciliador. Las pruebas lo apagan y entregan los resultados con su bus de prueba. |
| `Diagnostico__Consultas__Bus__Concurrencia` / `__Prefetch` | `4` / `8` | Resultados atendidos a la vez y en vuelo; el prefetch no puede ser menor que la concurrencia (si no, no arranca). |
| `Diagnostico__Consultas__MaxReintentos` | `1` | Reintentos automáticos de un transitorio por celda, en [0, 3]. |
| `Diagnostico__Consultas__Address` | vacío | URL gRPC de core-consultas (p. ej. `http://core-consultas:8084`). Solo para la réplica diaria de departamentos con impuesto (`ListarDepartamentosImpuesto`); vacía, la réplica queda como está. Un valor que no es URL impide arrancar. |
| `Diagnostico__Consultas__DeadlineSegundos` | `90` | Deadline de esa llamada a core-consultas; debe ser mayor que 0. |

### Crear trámite desde el diagnóstico (ADR-0084)

Apagado por defecto (`Diagnostico:Derivacion:Habilitada=false`): sin él no hay rutas, la grilla trae `derivacion: null` y no se
registran consumidor ni reconciliador. Se enciende junto con `Tramites:Derivacion:Habilitada` de core-api. Diseño:
`docs/diagnosticos/diseno-crear-tramite-desde-diagnostico.md`.

- **Individual** (`POST /diagnosticos/{id}/vehiculos/{vid}/tramite`): síncrono, rpc `CrearBorradorDesdeDiagnostico`.
- **Masivo** (`POST /diagnosticos/{id}/tramites`, progreso en `GET …/tramites/operaciones[/{opId}]`, `POST …/reintentar-fallidas`):
  la operación y sus ítems quedan en `diagnostico.derivacion_operaciones` / `diagnostico.derivaciones`. Cada compañía tiene como
  mucho `CreditoPorCompania` (2) ítems en vuelo: al encolar y al cerrar cada ítem, `DespachadorCredito` libera el siguiente y
  publica `diagnostico.derivacion.solicitada` (solo ids) a la cola propia `diagnostico.derivaciones`. El consumidor atiende
  `Concurrencia` (4) mensajes a la vez, revalida el vehículo, pasa por el freno (`TasaPorSegundo` 10, pausa de `PausaSegundos` 30
  tras `PausaTrasFallos` 5 transitorios seguidos) y reintenta los transitorios a los 30 s y 2 min conservando el crédito
  (`MaxIntentos` 3; luego FALLIDO, reintentable a mano). `ReconciliadorDerivacion` (60 s) cierra o re-publica una vez los ítems
  ENVIADO colgados más de 15 min. `ConsumidorHabilitado=false` deja las rutas sin consumidor (lo usan las pruebas).
- Prueba de carga real (QA): `docs/diagnosticos/prueba-carga-derivacion.md` y `tests/load/diagnostico-derivacion.js`.

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
- [ADR-0086](../../docs/decisions/ADR-0086-diagnostico-ajuste-flit-dictamen-y-notificaciones.md) (Propuesto, HU #14563):
  - **B7, ajuste FLIT del dictamen con versiones.** `PUT /backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen` crea una
    versión nueva del dictamen (motivo obligatorio de 10 a 500 caracteres); la compañía solo ve `ajustado_por_flit` y
    `ajustado_en`. La cadena completa sale en `GET …/dictamen/versiones` (backoffice, `diagnostico.auditoria.ver`).
    Migraciones `R7_AjusteDictamen` y `R1_VersionFilaTablero` (versión de fila para que el tablero vea el cambio).
  - **B8, bandeja de notificaciones.** `GET /notificaciones` con ETag débil y 304, `POST /notificaciones/{nid}/leida` y
    `POST /notificaciones/leer-todas`. `TrabajadorPurgaNotificaciones` borra las de más de 90 días
    (`Diagnostico__Notificaciones__RetencionDias`). Migración `R8_Notificaciones`.
- [Handoff del VPS](../../docs/suite/handoff-vps-diagnostico.md): despliegue en los ambientes.
