# core-impuestos — Impuestos

Servicio (API .NET) del producto **Impuestos** de la FLIT Suite, producto `impuestos` del
[contrato de plataforma v1](../../docs/suite/contrato-plataforma-v1.md). Generado con la plantilla `flit-servicio`; valida los
tokens de la suite por JWKS y guarda sus datos en su propio schema `impuestos`, que migra al arrancar. Integra directo el API de
liquidación de impuesto vehicular ([ADR-0093](../../docs/decisions/ADR-0093-impuestos-integracion-directa-api-liquidacion.md)).
Diseño: [plan de implementación](../../docs/impuestos/plan-implementacion.md).

Es independiente de Diagnóstico (EP-CF1): ningún proyecto referencia `core-diagnostico`, `Flit.Diagnostico.*` ni el API de
`core-consultas`; lo vigila `IndependenciaTests`.

## Estado (M05)

REST bajo `/api/v1/impuestos`. Toda ruta de negocio pasa por el guard de suscripción (fail-closed, 403 `PRODUCTO_NO_HABILITADO`)
y exige un token del producto; lo que no existe responde 404.

| Módulo | Rutas | Qué hace |
|---|---|---|
| Sesion | `GET /sesion` | Usuario, compañía, roles, permisos `impuestos.*`, `esSuperAdmin` y el estado de aprovisionamiento de la compañía. |

Además, desde M05:

- **Modelo completo** del plan §4.1 en la migración `R0_Modelo` (13 tablas + `data_protection_keys`). Toda tabla con `tenant_id`
  va por empresa (filtro global y validación al guardar); las globales son `avisos_recibidos`, `organismos` y `fuentes_api`.
- **Dominio** (`Flit.Impuestos.Domain`): máquinas de estado del aprovisionamiento, del ítem y del lote (plan §3), placa normalizada
  (S16) y clase de error del API (§9 de la guía).
- **Manifiesto** de permisos (plan §5.2) registrado al arrancar: roles `gestor_impuestos` y `admin_impuestos`.
- **gRPC** `flit.impuestos.v1.ImpuestosService` ([contrato](../../contracts/proto/flit/impuestos/v1/impuestos.proto)). El servicio
  existe pero no se expone hasta que el contrato de plataforma publique su scope.

## Puertos

| | Local | DEV / QA / PDN |
|---|---|---|
| API REST | 4033 | 4033 / 5033 / 6033, internos, detrás del gateway |
| gRPC (solo HTTP/2) | 8089 | interno, sin publicar |
| App web `frontend-impuestos` | 4034 | 4034 / 5034 / 6034 |

Configuración local: copiar `src/Flit.Impuestos.Api/appsettings.Development.json.example` a `appsettings.Development.json`.

### Certificado de Data Protection (obligatorio)

Las credenciales del API (api_key y webhook_secret por compañía), la PII del propietario y el resultado completo se cifran con
ASP.NET Data Protection. El anillo de llaves vive en `impuestos.data_protection_keys`, cifrado con un certificado que **no** está
en la base ni en ningún `appsettings*.json`. Sin él el servicio no arranca, en cualquier ambiente.

| Variable | Sale de (`.env`) | Qué es |
|---|---|---|
| `Impuestos__DataProtection__CertificadoRuta` | `IMPUESTOS_SVC_DP_CERTIFICADO_RUTA` | Ruta del `.pfx` (RSA, con llave privada), montado como secreto. |
| `Impuestos__DataProtection__CertificadoClave` | `IMPUESTOS_SVC_DP_CERTIFICADO_CLAVE` | Clave del `.pfx`. |

## Pruebas

```bash
export ConnectionStrings__Core="Host=localhost;Port=5433;Database=<bd>;Username=postgres;Password=postgres"
dotnet build tests/Flit.Impuestos.Tests/Flit.Impuestos.Tests.csproj -m:2 -nodeReuse:false
dotnet test tests/Flit.Impuestos.Tests/Flit.Impuestos.Tests.csproj --no-build --no-restore -m:2 -nodeReuse:false
```

Las pruebas con base crean una base efímera en el servidor de `ConnectionStrings__Core`; sin Postgres se omiten en local y fallan
en CI.
