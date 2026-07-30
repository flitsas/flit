# ADR-0037: Exportaciones asíncronas con LISTEN/NOTIFY + Worker + SignalR

**Fecha**: 2026-07-29
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, equipo core-api, equipo frontend
**Tags**: arquitectura, backend, frontend, infra, reporteria, exportaciones, async, signalr, feature-11076
**Supersedes**: —
**Relacionado**: ADR-0039 (SignalR transport / YARP session affinity), ADR-0021 (analítica fuente de datos)
**HU origen**: Feature #11076 — Subsistema de Reportería Transaccional V2

---

## Contexto

El exportador de reportes actual (`DetailedReportExcelExporter`, `ExportProceduresExcelHandler`) opera
síncronamente en el mismo hilo HTTP. Con un límite práctico de ~5k filas antes de causar timeout en YARP
(`ActivityTimeout: 00:00:30`), no es viable para el requisito de 50k registros por exportación.

Se necesita una arquitectura asíncrona que:
1. Acepte la solicitud y devuelva inmediatamente un `jobId` (202 Accepted)
2. Procese el archivo en background
3. Notifique al cliente cuando el archivo esté listo (push real-time)
4. Permita descarga segura con URL de acceso temporal
5. Sea resiliente ante reinicios del worker, desconexiones SignalR y fallos del file-manager
6. Sea segura en escenarios multi-réplica (sin doble procesamiento)

El sistema tiene PostgreSQL y no tiene Redis ni un bus de mensajes externo.
El gateway YARP ya tiene una ruta `signalr-route` para `/hubs/{**catch-all}` planificada.

**Constraint de storage:** El file-manager existente (`FileManagerAttachmentStorage` / `IAttachmentStorage`)
usa `Delete()` como NO-OP — el provider no expone borrado. Los archivos persisten 30 días por el
lifecycle de cold storage del file-manager. Este constraint es inmutable en el scope de V2 y está
reflejado en el diseño como condición explícita (sin endpoint de delete para export files).

---

## Alternativas evaluadas

### Opción A — Exportación síncrona con timeout extendido

Extender el `ActivityTimeout` de YARP a 5 minutos y procesar el Excel en el hilo HTTP.

**Pros:**
- Cero infra nueva
- Sin cambios en el frontend más allá de un spinner de larga duración
- Implementación trivial (1 día)

**Contras:**
- Un timeout de 5 minutos en YARP afecta a **todos** los endpoints del cluster, no solo exportaciones
- Los archivos de 50k registros generan Excel de 5–15 MB en memoria; si 5 usuarios exportan simultáneamente
  se consumen > 75 MB de memoria heap en un solo request batch — presión de GC severa
- El navegador puede cerrar la conexión antes de que el servidor termine (keepalive de browser)
- Sin indicador de progreso; UX bloqueante
- No hay retry automático si el proceso muere a mitad de la generación

**Esfuerzo:** S — **Riesgo:** ALTO (timeout productivo, UX degradada, sin retry)

---

### Opción B — export_jobs durable + LISTEN/NOTIFY + Worker + SignalR + REST fallback ✅ RECOMENDADA

Tabla `analytics.export_jobs` como fuente durable de estado. Worker `BackgroundService` despertado
por `LISTEN/NOTIFY` de PostgreSQL con polling cada 30 s como fallback. `FOR UPDATE SKIP LOCKED` para
multi-réplica. `ExportJobsHub` (SignalR) para push en tiempo real. REST fallback (`GET /exports/{id}`)
cuando SignalR está desconectado. Storage a través de `IAttachmentStorage` vía nuevo adaptador
`IExportFileStorage` con configuración dedicada (`"ExportFileManager"`).

**Pros:**
- Exportaciones de 50k registros sin bloquear el hilo HTTP ni el YARP timeout
- `export_jobs` es la única fuente de verdad — resiliente ante reinicios del worker
- NOTIFY es solo wake-up; el worker siempre consulta la BD para tomar el job (correcto bajo failover)
- `FOR UPDATE SKIP LOCKED` garantiza que dos réplicas no procesen el mismo job
- SignalR push da UX en tiempo real con progreso porcentual
- REST fallback (`GET /exports/{id}`) funciona sin WebSocket (reconnects, proxies sin WS)
- Storage reutiliza `IAttachmentStorage` existente sin duplicar código de S3/file-manager
- No requiere Redis, RabbitMQ ni ningún broker externo — solo PostgreSQL que ya existe

**Contras:**
- Mayor complejidad que Opción A (3 componentes: listener, worker, hub)
- Requiere `UseWebSockets()` en gateway (cambio bloqueante pero trivial)
- YARP SessionAffinity necesaria para multi-réplica (ver ADR-0039)
- Gestión de jobs `processing` > tiempo límite (cron de reset)
- `Delete()` NO-OP en el file-manager — los archivos no se pueden borrar on-demand
  (constraint documentado; mitigado por TTL de URL ≤15 min y lifecycle 30 días)

**Esfuerzo:** L — **Riesgo:** MEDIO

---

### Opción C — Bus de mensajes externo (RabbitMQ o AWS SQS)

Worker externo consume mensajes de un broker; exporta y sube el archivo. Notificación por polling
REST o webhook.

**Pros:**
- Máxima escalabilidad horizontal; el worker puede ser un servicio independiente
- Sin contención en la BD para el queue
- Reintentos nativos del broker

**Contras:**
- Infraestructura nueva: RabbitMQ o SQS no existen en el stack actual
- Complejidad operativa desproporcionada: gestión de colas, dead-letter, bindings
- El volumen proyectado (< 100 exportaciones simultáneas) no justifica esta complejidad
- La observabilidad se fragmenta entre el broker, el worker y la API
- Sin ganancia práctica sobre Opción B para el caso de uso actual

**Esfuerzo:** XL — **Riesgo:** MEDIO (over-engineering para el caso de uso)

---

## Decisión

**Se elige la Opción B.**

Justificación:
- PostgreSQL `LISTEN/NOTIFY` ya está disponible; añadir un bus externo es over-engineering
- `IAttachmentStorage` y `FileManagerAttachmentStorage` ya implementan el acceso a S3 — no hay que
  duplicar nada; el adaptador `IExportFileStorage` añade clean architecture sin nueva dependencia de infra
- El timeout de Opción A es un riesgo productivo real, documentado en el stack actual
- Opción C tiene un coste de infra y operación desproporcionado para el volumen proyectado

---

## Consecuencias

### Positivas
- UX de exportación mejorada: progreso en tiempo real, notificación email + badge in-app al completar
- Sin timeout HTTP para exportaciones de hasta 50k registros
- Resiliente: ante fallo del worker, reinicio de PG o desconexión SignalR, el job se recupera
- Multi-réplica seguro con `FOR UPDATE SKIP LOCKED`
- Sin nuevas dependencias de infraestructura (solo PG + file-manager ya existentes)

### Negativas / Constraints
- `Delete()` es NO-OP — los archivos de export persisten 30 días en S3; no hay limpieza on-demand
  posible. Los download links expiran en ≤ 15 min (presigned URL TTL) — esto protege el acceso
  no autorizado aunque el archivo siga en S3
- Los jobs en estado `processing` por más de 10 min deben resetearse a `failed` por un cron periódico
  (responsabilidad del `infra-agent` o del worker mismo como self-healing)
- `UseWebSockets()` en `Flit.Gateway/Program.cs` es condición previa al primer deploy con SignalR

---

## Detalles de implementación (diseño — no código de producción)

### Tabla `analytics.export_jobs`

Ver DDL de referencia completo en `docs/design/FEATURE-11076-reporteria-transaccional-v2.md` §7.

Campos clave: `id`, `tenant_id`, `owner_user_id`, `status` (pending|processing|completed|failed),
`report_type`, `format` (excel|csv|pdf), `filters_json`, `progress_pct`, `file_storage_path`,
`file_size_bytes`, `file_sha256`, `error_message`, `expires_at` (now() + 30 days), `started_at`,
`completed_at`, `created_at`, `updated_at`, `deleted_at`.

Índices críticos:
- `(tenant_id, owner_user_id)` WHERE `deleted_at IS NULL` — listar mis jobs
- `(status, created_at)` WHERE `status = 'pending'` — worker SELECT

### Puerto `IExportFileStorage`

```
SaveExportAsync(Guid jobId, string format, string filename, Stream content, CancellationToken)
  → Task<string> storagePath

GetDownloadUrlAsync(string storagePath, CancellationToken)
  → Task<(string Url, DateTimeOffset ExpiresAt)?>
```

Adaptador `FileManagerExportStorage` inyecta `IAttachmentStorage` (puerto tramites) y lo llama
con `tipo = $"export_{format}"` y categoría `"exports"` desde `IOptions<ExportFileManagerOptions>`
(sección config `"ExportFileManager"` — separada de `"FileManager"` para evitar mismatch de BaseUrl).

### Worker — lógica de resiliencia

```
Loop:
  1. Esperar señal (canal en memoria) ← ExportJobsChannelListener lo señaliza al recibir NOTIFY
  2. SELECT * FROM analytics.export_jobs WHERE status='pending' ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED
  3. Si no hay job: volver a esperar (el canal tiene timeout de 30 s → polling fallback)
  4. UPDATE status='processing', started_at=now()
  5. Generar archivo (progreso vía SignalR)
  6. IExportFileStorage.SaveExportAsync() → storagePath
  7. UPDATE status='completed', file_storage_path=storagePath, progress_pct=100
  8. SignalR push ExportCompleted + email SMTP
  En error: UPDATE status='failed', error_message=ex.Message
  Cron externo o self-healing: UPDATE status='failed' WHERE status='processing' AND updated_at < now() - interval '10 minutes'
```

### Límites de negocio

| Límite | Valor | Aplicación |
|--------|-------|------------|
| Registros por export | máx. 50 000 | Validado en `RequestExportHandler` antes del INSERT |
| Jobs pending por usuario | máx. 3 | Validado en `RequestExportHandler` → 409 si se supera |
| TTL download URL | ≤ 15 min | `GetPresignedViewUrlAsync` (`PreviewUrlTtlMinutes ≤ 15`) |
| Retención archivo en storage | 30 días | Lifecycle del file-manager (no configurable en V2) |
| Retención registro `export_jobs` | 30 días | Soft-delete por cron; `expires_at` campo de referencia |

### Notificación (G7)

Al completar el job, el worker:
1. Envía `ExportCompleted` por SignalR al usuario propietario
2. Envía email SMTP (ya configurado en `Smtp` section) con link a `GET /exports/{jobId}/download-url`
3. El frontend obtiene la URL de descarga por REST y la muestra como link/botón

Si SignalR está desconectado, el frontend hace polling `GET /exports/{jobId}` cada 5 s y al detectar
`status = "completed"`, llama a `GET /exports/{jobId}/download-url` para el enlace de descarga.
El badge in-app (`ExportController`) también carga `GET /exports?status=completed&limit=10` al
montar el layout para mostrar exportaciones recientes.

---

## Archivos que cambia esta decisión

### Crear
- `src/Flit.Analytics.Application/ExportJobs/IExportFileStorage.cs`
- `src/Flit.Analytics.Application/ExportJobs/IExportJobRepository.cs`
- `src/Flit.Analytics.Application/ExportJobs/Commands/RequestExportCommand.cs`
- `src/Flit.Analytics.Application/ExportJobs/Commands/RequestExportHandler.cs`
- `src/Flit.Analytics.Application/ExportJobs/Queries/GetExportJobHandler.cs`
- `src/Flit.Analytics.Application/ExportJobs/Queries/GetDownloadUrlHandler.cs`
- `src/Flit.Infrastructure/Storage/FileManagerExportStorage.cs`
- `src/Flit.Infrastructure/Workers/ExportJobsChannelListener.cs`
- `src/Flit.Infrastructure/Workers/ExportJobsWorker.cs`
- `src/Flit.Infrastructure/Hubs/ExportJobsHub.cs`
- `src/Flit.Api/Endpoints/Reporting/ExportJobsEndpoints.cs`
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/40-F11076-reporting-v2.sql`

### Modificar
- `src/Flit.Api/Program.cs` — `AddSignalR()` + `MapHub<ExportJobsHub>("/hubs/export-jobs")`
- `src/Flit.Infrastructure/InfrastructureExtensions.cs` — registro de servicios
- `src/Flit.Gateway/Program.cs` — `app.UseWebSockets()` (BLOQUEANTE)
- `src/Flit.Gateway/appsettings.json` — SessionAffinity + cluster SignalR

### No modifica
- `src/Flit.Infrastructure/Storage/FileManagerAttachmentStorage.cs` — reutilizado sin cambio
- `src/Flit.Tramites.Application/Storage/IAttachmentStorage.cs` — reutilizado sin cambio
- `src/Flit.Infrastructure/Documents/DetailedReportExcelExporter.cs` — reutilizado como base
