# Contratos gRPC entre servicios

Fuente única de los contratos con los que un servicio de FLIT llama a otro y espera respuesta
([ADR-0070](../../docs/decisions/ADR-0070-grpc-entre-servicios-rest-hacia-afuera.md), contrato de plataforma
v1.3 §6.1). Lo que entra o sale de FLIT sigue en REST (`contracts/openapi`) y lo asíncrono en AsyncAPI
(`contracts/asyncapi`).

## Estructura

```
contracts/proto/
├── buf.yaml                       # lint STANDARD, breaking FILE
└── flit/
    ├── platform/v1/               # tipos comunes: Placa, Vin, DocumentoIdentidad, CodigoError
    ├── identidad/v1/              # IdentidadService (lo atiende core-identity)
    └── consultas/v1/              # ConsultasService (lo atiende core-consultas)
```

- Paquete `flit.<servicio>.v<n>` en la carpeta `flit/<servicio>/v<n>/`; servicios con sufijo `Service`;
  mensajes `<Metodo>Request` / `<Metodo>Response`. Lo exige `buf lint`.
- Nombres en español, como el dominio. Los términos técnicos estándar (`page_token`, `page_size`) quedan
  en inglés.
- La empresa **no** viaja en los mensajes: va en la metadata `x-flit-tenant-id`, junto con `authorization`,
  `x-correlation-id` y `traceparent` (contrato §3 y §6.1).
- Los errores llevan `google.rpc.ErrorInfo` con `domain = "flitsas.com"` y `reason` igual al `code` del
  contrato §10 (ver `flit/platform/v1/errores.proto`).

## Cambiar un contrato

- Agregar campos, mensajes o métodos es compatible.
- Borrar o renumerar un campo, cambiar su tipo o renombrar un paquete **rompe**: `buf breaking` lo detiene en
  CI. Si de verdad hace falta, se crea `v2` y convive con `v1` hasta que nadie use la anterior.
- Un número de campo borrado se marca `reserved`, nunca se reutiliza.

Verificación local (sin instalar nada):

```bash
cd contracts/proto
npx -y @bufbuild/buf@1.73.0 lint
npx -y @bufbuild/buf@1.73.0 breaking . --against "../../.git#ref=origin/develop,subdir=contracts/proto"
```

## Código C#

`buf` solo valida. Cada servicio genera su código con `Grpc.Tools` en el build, desde un proyecto
`Flit.<Servicio>.Grpc.Contracts` que apunta a esta carpeta:

| Proyecto | Paquete | Dónde vive |
|---|---|---|
| `Flit.Platform.Grpc.Contracts` | `flit.platform.v1` | `services/core-identity/src/` |
| `Flit.Identidad.Grpc.Contracts` | `flit.identidad.v1` | `services/core-identity/src/` |
| `Flit.Consultas.Grpc.Contracts` | `flit.consultas.v1` | `services/core-consultas/src/` |

Los proyectos de un servicio que importan `flit.platform.v1` referencian `Flit.Platform.Grpc.Contracts` y no
vuelven a generar esos tipos.

**Docker.** Las imágenes .NET se construyen hoy con contexto `./services` (`./services/core-ict` en ICT), que no
incluye esta carpeta. El primer servicio que meta uno de estos proyectos en su imagen amplía su contexto a la raíz
del repo en `cd.yml` y en su `Dockerfile`.
