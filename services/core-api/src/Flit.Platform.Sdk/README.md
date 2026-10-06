# Flit.Platform.Sdk

Lo que un servicio de FLIT necesita para atender y llamar a otros sin escribir infraestructura propia (Epic #13316,
ADR-0070, contrato de plataforma v1.3). **No referencia ningún proyecto Flit**: un servicio nuevo no arrastra la base
de Identidad ni Trámites.

| Pieza | Para qué | HU |
|---|---|---|
| `AddFlitPlatformAuthentication` | Valida tokens de la plataforma contra el JWKS de Identidad: firma, emisor y la audiencia del servicio | #13336 |
| `PlatformTenantContext` / `ForTenant` | Empresa de la petición y filtro que **falla cerrado**: sin empresa, ninguna fila; el SuperAdmin ve todas | #13336 |
| `PlatformPrincipal` | Token de servicio (`sub = svc-…`) o de usuario, SuperAdmin, scopes | #13336 |
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
