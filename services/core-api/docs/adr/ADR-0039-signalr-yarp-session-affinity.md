# ADR-0039: SignalR transport con YARP WebSockets y SessionAffinity sin Redis (Fase 1)

**Fecha**: 2026-07-29
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, equipo infra, equipo backend
**Tags**: arquitectura, infra, signalr, yarp, websocket, gateway, multi-replica, feature-11076
**Supersedes**: —
**Relacionado**: ADR-0037 (export jobs async), ADR-0017 (YARP gateway — Aceptado)
**HU origen**: Feature #11076 — Subsistema de Reportería Transaccional V2

---

## Contexto

La arquitectura de export jobs (ADR-0037) utiliza SignalR (`ExportJobsHub`) para entregar
notificaciones de progreso y finalización al cliente en tiempo real. El Hub vive en `Flit.Api`
(puerto interno) y el cliente lo alcanza a través del gateway YARP (`Flit.Gateway`).

SignalR con transporte WebSocket requiere que el cliente esté siempre conectado a la **misma**
réplica backend durante la sesión de WebSocket. Si el cliente se reconecta a una réplica diferente,
esa réplica no tiene en memoria los grupos/conexiones del hub de la sesión anterior.

Existen dos formas de resolver esto:
1. **SessionAffinity** — el proxy garantiza que el cliente siempre llegue a la misma réplica
2. **Redis backplane** — el estado del hub se comparte entre réplicas; el cliente puede reconectarse
   a cualquier réplica y seguir recibiendo mensajes

El stack actual de `flit-reporteria-v2` **no incluye Redis** en ningún ambiente. YARP soporta
nativamente `SessionAffinity` como configuración de cluster.

**Hallazgos en el repositorio:**
- `Flit.Gateway/appsettings.json` ya tiene `signalr-route` para `/hubs/{**catch-all}` → `core-api-cluster`
- `Flit.Gateway/Program.cs` **no tiene** `app.UseWebSockets()` antes de `app.MapReverseProxy()` —
  sin este middleware YARP no negocia el upgrade WebSocket y SignalR cae a long-polling
- `core-api-cluster` tiene `LoadBalancingPolicy: RoundRobin` — sin SessionAffinity, reconnects
  pueden caer a otra réplica
- `core-api-cluster` tiene `ActivityTimeout: 00:00:30` — puede afectar handshake WS en alta latencia

---

## Alternativas evaluadas

### Opción A — Solo REST polling (sin SignalR)

No se implementa SignalR. El frontend hace `GET /api/v1/reporting/exports/{id}` cada 5 s para
obtener el estado del job.

**Pros:**
- Cero complejidad de infra: sin WebSocket, sin SessionAffinity, sin backplane
- Compatible con cualquier topología de red (proxies, firewalls que bloquean WS)
- Sin `UseWebSockets()` en el gateway
- Fácil de testear

**Contras:**
- UX degradada: sin progreso porcentual real-time; el cliente solo ve cambios de estado a intervalos
- Mayor carga en la BD: cada 5 s por cada job activo por cada usuario conectado
- Sin push real: el usuario no recibe notificación inmediata al completar el job si no tiene la
  pestaña activa
- No cumple el requisito de `badge/toast in-app` al completar la exportación sin que el usuario
  esté activamente observando el estado

**Esfuerzo:** S — **Riesgo:** BAJO (técnico), ALTO (requisito UX incumplido)

---

### Opción B — SignalR + YARP SessionAffinity (cookie) sin Redis ✅ RECOMENDADA (Fase 1)

Habilitar WebSocket en YARP con `app.UseWebSockets()`. Crear un cluster dedicado
`core-api-signalr-cluster` con `ActivityTimeout: 00:05:00`. Configurar `SessionAffinity`
de tipo `Cookie` en `signalr-route` para garantizar que el cliente siempre llegue a la misma
réplica backend. Sin Redis backplane.

Cuando la afinidad se pierde (replica caída), el cliente de SignalR hace reconexión automática:
al reconectarse, obtiene el estado actual del job vía REST (`GET /exports/{id}`) y se re-suscribe.

**Pros:**
- YARP SessionAffinity es nativa — sin nueva dependencia de infraestructura
- Sin Redis: el stack actual no requiere cambios de infra
- WebSocket real-time: progreso porcentual, `ExportCompleted` push inmediato
- Cumple el requisito de `badge/toast in-app`
- Si la réplica afín cae, YARP redistribuye con `FailurePolicy: Redistribute` y el cliente
  recupera el estado por REST (`GET /exports/{id}`)
- Válida para escenarios de 1-2 réplicas (caso de uso proyectado para V2)

**Contras:**
- Si hay > 2 réplicas y la réplica afín cae, el cliente puede perder eventos en tránsito (mitigado
  por el REST fallback)
- YARP SessionAffinity por cookie no funciona si el cliente no acepta cookies (casos edge raros
  en navegadores con configuración estricta — mitigado por el REST fallback como comportamiento
  degradado)
- `UseWebSockets()` es un cambio bloqueante en el gateway antes del primer deploy

**Esfuerzo:** M — **Riesgo:** BAJO para 1-2 réplicas, MEDIO si escalan > 2

---

### Opción C — SignalR + Redis backplane

Agregar `Microsoft.AspNetCore.SignalR.StackExchangeRedis` en `Flit.Api`. Desplegar Redis como
nuevo servicio en el `docker-compose.prod.yml`. El estado del hub se comparte entre todas las
réplicas.

**Pros:**
- Reconnects seamless a cualquier réplica: sin pérdida de eventos
- Escala horizontalmente sin límite de réplicas
- Sin SessionAffinity en YARP (simplifica la config del gateway)

**Contras:**
- Nueva infraestructura: Redis debe desplegarse, monitorizarse, respaldarse y actualizarse
- Dependencia operativa nueva: si Redis cae, SignalR hub falla en todas las réplicas (single point
  of failure si no se configura cluster/sentinel)
- Mayor latencia: cada push de SignalR pasa por Redis antes de llegar al cliente
- Desproporcionado para el caso de uso proyectado (< 20 conexiones SignalR simultáneas en V2)
- Añade `StackExchangeRedis` como dependencia de producción sin justificación de escala actual

**Esfuerzo:** L — **Riesgo:** MEDIO (nueva infra, new SPOF sin HA Redis)

---

## Decisión

**Se elige la Opción B para la Fase 1 del Feature #11076.**

Justificación:
- El stack no incluye Redis y añadirlo sería over-engineering para el volumen proyectado
- YARP SessionAffinity es nativa y no requiere nuevas dependencias
- El REST fallback (`GET /exports/{id}`) garantiza que ningún usuario pierde el estado del job,
  incluso si la afinidad se rompe
- La revisión a Opción C se triggerea cuando réplicas de `core-api` superen 2

---

## Consecuencias

### Positivas
- SignalR con push real-time y progreso porcentual sin nuevas dependencias de infra
- Sin Redis en la fase inicial
- YARP SessionAffinity es transparente al cliente (cookie automática)
- REST fallback garantiza resiliencia para reconnects

### Negativas / Constraints
- `app.UseWebSockets()` debe agregarse en `Flit.Gateway/Program.cs` **antes del primer deploy** —
  es una condición previa bloqueante; sin ella SignalR cae a long-polling silenciosamente
- `ActivityTimeout: 00:00:30` en `core-api-cluster` no aplica igual a WebSocket (la sesión WS
  no tiene el mismo timeout que un request HTTP), pero se crea un cluster dedicado para mayor
  claridad y control
- Cuando réplicas de `core-api` superen 2, el Líder Técnico de infra debe activar Redis backplane
  y actualizar este ADR con un nuevo estado o crear ADR-0040 (Supersedes: ADR-0039)

---

## Cambios de configuración necesarios

### `src/Flit.Gateway/Program.cs`

Agregar **antes** de `app.MapReverseProxy()`:

```csharp
app.UseWebSockets();   // OBLIGATORIO para upgrade WebSocket via YARP
```

### `src/Flit.Gateway/appsettings.json`

Modificar `signalr-route` y agregar `core-api-signalr-cluster`:

```json
"signalr-route": {
  "ClusterId": "core-api-signalr-cluster",
  "Match": { "Path": "/hubs/{**catch-all}" },
  "AuthorizationPolicy": "JwtRequired",
  "SessionAffinity": {
    "Enabled": true,
    "Policy": "Cookie",
    "AffinityKeyName": ".Flit.SignalR.Affinity",
    "FailurePolicy": "Redistribute"
  }
},

"core-api-signalr-cluster": {
  "Destinations": {
    "core-api-1": { "Address": "http://core-api:4003/" }
  },
  "HttpRequest": { "ActivityTimeout": "00:05:00" }
}
```

### `src/Flit.Api/Program.cs` (backend-agent)

```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<ExportJobsHub>("/hubs/export-jobs");
```

> `Microsoft.AspNetCore.SignalR` está incluido en el SDK `Microsoft.NET.Sdk.Web` — sin paquete NuGet adicional en `Flit.Api.csproj`.

---

## Trigger de revisión

Este ADR debe revisarse y potencialmente actualizarse (o ser supersedido por ADR-0040) cuando:

1. El número de réplicas concurrentes de `core-api` supere **2**
2. Se detecten pérdidas de eventos SignalR atribuibles a redistribución de afinidad en métricas
   de observabilidad

El responsable de iniciar esa revisión es el **Líder Técnico de infra** (asignado en el gate de
aprobación del Feature #11076).

---

## Archivos que cambia esta decisión

### Modificar
- `src/Flit.Gateway/Program.cs` — `app.UseWebSockets()` (BLOQUEANTE)
- `src/Flit.Gateway/appsettings.json` — cluster dedicado + SessionAffinity

### Crear
- `src/Flit.Infrastructure/Hubs/ExportJobsHub.cs` — Hub SignalR

### No modifica
- `src/Flit.Gateway/Flit.Gateway.csproj` — YARP ya incluye soporte WebSocket; sin paquetes nuevos
- `src/Flit.Api/Flit.Api.csproj` — SignalR incluido en el SDK web; sin paquetes nuevos
