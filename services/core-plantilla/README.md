# core-plantilla — plantilla de servicio de plataforma

Servicio de referencia que compila y se prueba como cualquier otro, y a la vez es la plantilla `dotnet new` de la que
nacen los servicios de plataforma (Epic #13316, HU #13340). No se despliega.

```bash
cd services
dotnet new install ./core-plantilla
dotnet new flit-servicio -n Consultas -o core-consultas
```

`-n` es el nombre en PascalCase; la plantilla deriva el código en minúsculas (esquema, `aud`, `svc-<código>`,
exchange `flit.<código>`) y en mayúsculas (variables del `.env`).

Qué trae: SDK de plataforma (tokens por JWKS, gRPC con token de servicio, outbox e inbox), esquema propio con su
primera migración, `/health`, `/health/ready` (base alcanzable y sin migraciones pendientes), `grpc.health.v1` en su
puerto gRPC interno, Dockerfile y pruebas. Si falta configuración no arranca y nombra las variables.

Cómo se conecta un servicio nuevo (contrato, CI, CD, compose, base y puertos): [`docs/suite/servicio-nuevo.md`](../../docs/suite/servicio-nuevo.md).
