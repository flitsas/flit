# Flit.Platform.Sdk

Lo que un servicio de FLIT necesita para atender y llamar a otros sin escribir infraestructura propia (Epic #13316,
ADR-0070, contrato de plataforma v1.3). **No referencia ningún proyecto Flit**: un servicio nuevo no arrastra la base
de Identidad ni Trámites.

| Pieza | Para qué | HU |
|---|---|---|
| `AddFlitPlatformAuthentication` | Valida tokens de la plataforma contra el JWKS de Identidad: firma, emisor y la audiencia del servicio | #13336 |
| `PlatformTenantContext` / `ForTenant` | Empresa de la petición y filtro que **falla cerrado**: sin empresa, ninguna fila; el SuperAdmin ve todas | #13336 |
| `PlatformPrincipal` | Token de servicio (`sub = svc-…`) o de usuario, SuperAdmin, scopes | #13336 |
| `AddFlitGrpcServer` / `RequireServiceToken<T>` / `MapFlitGrpcPlatform` | Servidor gRPC: errores del §10 con `ErrorInfo`, token de servicio + scope + audiencia + empresa, `grpc.health.v1`, reflection solo en DEV | #13337 |
| `AddFlitGrpcClient<T>` | Cliente gRPC: token de servicio por scope (pedido y renovado), empresa, correlación y `traceparent` en la metadata, deadline 3 s, reintentos solo ante `UNAVAILABLE` y circuito por destino | #13337 |
| `PlatformException` / `PlatformRpcErrors.Reason` | Lanzar un error del §10 en un handler y leerlo del lado del cliente | #13337 |
| `AddFlitOutbox<TContext>` / `IPlatformOutbox` / `AddFlitOutbox(schema)` | Evento guardado en la misma transacción que el cambio y publicado a RabbitMQ (`flit.<productor>`, sobre §7 en JSON) al confirmar; con el broker caído espera y sale en orden | #13338 |
| `AddFlitConsumer<TContext, THandler, TData>` / `IEventConsumer<T>` / `AddFlitInbox(schema)` | Consumidor: bandeja `(event_id, consumer)` (un evento repetido no repite el efecto), reintentos a 10 s, 1 min y 10 min, luego `<cola>.dlq` y la métrica `flit.messaging.dead_lettered` | #13339 |
| `AddFlitTelemetry` / `UseFlitCorrelationId` | Trazas y logs OTLP (solo con `OTEL_EXPORTER_OTLP_ENDPOINT`) e id de correlación | #13332 |
| `OidcDefaults`, `ServiceAudiences`, `AdminAuthorization` | Scopes de servicio, audiencias por servicio destino, claims y roles | #13333 |

## Uso en un servicio nuevo

```csharp
builder.Services.AddFlitPlatformAuthentication(builder.Configuration);
builder.AddFlitTelemetry("flit-core-consultas");

var app = builder.Build();
app.UseFlitCorrelationId();
app.UseAuthentication();
app.UseAuthorization();
```

```json
"Platform": {
  "Auth": {
    "Audience": "consultas",
    "Issuers": [ "https://dev.flitsas.online/" ],
    "JwksUri": "http://core-identity:4025/.well-known/jwks.json"
  }
}
```

Atender y llamar por gRPC:

```csharp
builder.Services.AddFlitGrpcServer().RequireServiceToken<ConsultasGrpcService>("platform.consultas", ServiceAudiences.Consultas);
builder.Services.AddFlitGrpcClient<IdentidadService.IdentidadServiceClient>(
    builder.Configuration, new Uri("http://core-identity:8083"), "platform.identidad.read");

app.MapGrpcService<ConsultasGrpcService>();
app.MapFlitGrpcPlatform(app.Environment);
```

```json
"Platform": {
  "ServiceClient": { "TokenEndpoint": "http://gateway:4002/connect/token", "ClientId": "svc-consultas", "ClientSecret": "<SVC_CONSULTAS_CLIENT_SECRET>" }
}
```

En un handler, `PlatformServiceCaller.From(context)` da el cliente y la empresa ya validados. Desde un proceso sin
petición en curso (un job), la empresa se pasa explícita en la metadata `x-flit-tenant-id`.

Publicar un evento (se confirma con el `SaveChanges` del cambio; `modelBuilder.AddFlitOutbox("consultas")` en el
`DbContext` y `builder.Services.AddFlitOutbox<ConsultasDb>(builder.Configuration)` con `Platform:Messaging`
{`ConnectionString`, `Producer`}):

```csharp
outbox.Enqueue("consultas.consulta.realizada", 1, tenantId, new { proveedor, fuente, exito, latenciaMs });
await db.SaveChangesAsync(ct);
```

Consumir (solo se escribe el efecto; corre en la misma transacción que la bandeja):

```csharp
builder.Services.AddFlitConsumer<NotificacionesDb, CorreoPorCambioDeEstado, CambioDeEstado>(
    builder.Configuration, queue: "notificaciones.correo", producer: "tramites", ["tramites.procedure.state_changed"]);

public sealed class CorreoPorCambioDeEstado(NotificacionesDb db) : IEventConsumer<CambioDeEstado>
{
    public Task HandleAsync(EventEnvelope envelope, CambioDeEstado data, CancellationToken ct) { /* el efecto */ }
}
```

Leer por empresa siempre con el filtro del SDK:

```csharp
var filas = await db.Consultas.ForTenant(tenant.Current, c => c.TenantId).ToListAsync(ct);
```

## Reglas

- La empresa de un usuario sale de su token (`tenant_id`); la cabecera `X-Flit-Tenant-Id` (metadata
  `x-flit-tenant-id` en gRPC) solo cuenta para tokens de servicio (contrato §3).
- core-api y core-identity **no** usan `AddFlitPlatformAuthentication`: guardan las llaves en su base y validan con
  `Flit.Suite.AspNetCore`, que referencia este SDK.
- Los tipos que salieron de `Flit.Suite.AspNetCore` conservan su espacio de nombres `Flit.Api.*`.
