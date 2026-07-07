# ADR-0024 — Endurecimiento de la auditoría de configuración OT (IP, resultado, operación) y garantía de campos oficiales

- **Estado**: Propuesto · 2026-07-07
- **Módulo**: Admin OT (auditoría de configuración) + perfil del OT
- **Requerimientos**: RNF01 (auditoría mínima completa), RF05 (impedir modificar campos oficiales RUNT)
- **Decide**: Líder Técnico

## Contexto

**RNF01** exige que la auditoría registre, como mínimo: **usuario, fecha, hora, IP, operación y
resultado**. Hoy `admin.tenant_config_audit_logs` (entidad `TenantConfigAuditLog`) captura:

- `TenantId`, `EntityName`, `FieldName`, `OldValue`/`NewValue` (jsonb), `ChangedAt`
  (fecha+hora), `ChangedBy` (usuario), `CorrelationId`.

Faltan **tres** de los seis elementos exigidos:

1. **IP del cliente** — no hay columna ni `IHttpContextAccessor` en la capa de auditoría. La IP
   solo se obtiene hoy vía `Connection.RemoteIpAddress` en el portal público y el gateway
   (rate-limit), nunca en la auditoría de configuración.
2. **Resultado (éxito/fallo)** — la fila de auditoría se agrega **en la misma unidad de trabajo**
   que el cambio y se persiste con `SaveChangesAsync`. Por tanto solo existe rastro cuando la
   operación tuvo éxito; **un fallo (validación, concurrencia, permiso) no deja rastro**.
3. **Operación explícita** — el verbo (CREATE/UPDATE/DELETE) hoy se **infiere** de
   `EntityName`/`FieldName` + presencia de `OldValue`/`NewValue`; no hay campo dedicado.

Writers actuales de auditoría (todos con el patrón `_context.TenantConfigAuditLogs.Add(...)` +
`SaveChangesAsync` en la misma transacción del cambio):
`TenantSettingsRepository`, `TransitGrantRepository` (alta y baja), `WhitelistRepository`,
`TransitOfficeTenantWriteRepository`.

**RF05** exige impedir modificar los campos oficiales RUNT del OT (razón social, NIT, código).
Estado real: **cumplido por omisión** — `PATCH /ot/profile` (`UpdateOtProfileRequest`) solo acepta
`OperationMode` y `QuipuxReadOnly`; los campos oficiales viven en `identity.tenants`, se fijan al
crear el OT y **no tienen endpoint de edición**. No existe una regla explícita ni un test que
garantice esa inmutabilidad si alguien amplía el DTO en el futuro.

## Decisiones de producto ya acordadas (entradas)

1. **Alcance:** RF05 **y** RNF01 en el mismo esfuerzo.
2. **Resultado:** auditar **éxito y fallo** (no solo el camino feliz).
3. **Operación:** añadir **columna de operación explícita** (create/update/delete) además de IP y
   resultado.

## Decisión

### 1. Esquema: nuevas columnas en `admin.tenant_config_audit_logs`

| Columna | Tipo | Nota |
|---|---|---|
| `client_ip` | `text` (nullable) | IP de origen; nullable para filas históricas y procesos sin HTTP |
| `result` | `text` (nullable) | `success` \| `failure` |
| `operation` | `text` (nullable) | `create` \| `update` \| `delete` |
| `error_code` | `text` (nullable) | código de error en filas `failure` (p. ej. `operation_mode_invalido`) |

Nullable + migración aditiva → sin backfill obligatorio ni ruptura de filas existentes. `client_ip`
se guarda como `text` (no `inet`) para admitir listas `X-Forwarded-For` normalizadas y valores
degradados sin fallar. Se añade índice opcional `(tenant_id, result, changed_at desc)` para las
consultas de auditoría por resultado.

### 2. Propagación de la IP sin acoplar Application/Domain a HTTP

Se introduce una abstracción **`IAuditContextAccessor`** (en `Flit.Admin.Application`, o el
proyecto de contratos compartidos) que expone lo transversal de la petición:

```csharp
public interface IAuditContextAccessor
{
    string? ClientIp { get; }     // ya normalizada (X-Forwarded-For respetado)
    Guid? UserId { get; }         // opcional; hoy el ChangedBy llega por parámetro
}
```

- Implementación en `Flit.Infrastructure` (o API) sobre **`IHttpContextAccessor`**
  (`AddHttpContextAccessor()` en DI), resolviendo la IP con prioridad
  `X-Forwarded-For` (primer hop) → `Connection.RemoteIpAddress`, porque hay un **gateway
  delante** (`Flit.Gateway`).
- Los repositorios/writers reciben la IP desde este accessor (inyectado) al construir la fila de
  auditoría. Application y Domain **no** referencian `HttpContext`.

### 3. Modelo transaccional para auditar fallos (decisión clave)

Hoy el log vive en la misma transacción que el cambio → un rollback lo borraría. Para que las
filas `failure` **sobrevivan** al rollback:

- **Éxito:** se mantiene el registro **in-transaction** (atómico con el cambio), ahora con
  `result = success`, `operation` e `client_ip`.
- **Fallo:** se escribe una fila `result = failure` mediante un **writer de auditoría
  independiente** (`IAuditFailureWriter`) que usa su **propio scope/transacción** (o conexión
  nueva), de modo que persista aunque la operación principal haga rollback. Se dispara en el
  **filtro de excepciones/resultado de la API** (endpoint filter en `Flit.Api`), que ya conoce el
  desenlace (2xx vs 4xx/5xx), el `tenant`, el usuario y la IP. Grano: por operación (no por
  campo), con `entity_name` + `operation` + `error_code`.

Esto evita reescribir los 4 writers para el camino de fallo y centraliza la captura del resultado
donde el desenlace es inequívoco.

### 4. RF05: garantía explícita + test de contrato

- Definir una constante/documentación de **campos oficiales protegidos** (`legal_name`, `tax_id`,
  `code`) y una comprobación explícita: el flujo de actualización del perfil **nunca** escribe esos
  campos. Como el DTO no los expone, la garantía se materializa sobre todo como **test de
  contrato**: un intento de actualizar campos oficiales (vía payload con propiedades extra) no los
  modifica y, si se envía explícitamente, se responde `422 campos_oficiales_no_editables` y se
  audita como `failure`.
- No se añade endpoint de edición de campos oficiales (siguen siendo inmutables post-creación).

## Alternativas consideradas

### Alternativa A — Columnas nuevas + accessor de IP + writer de fallo independiente + endpoint filter (RECOMENDADA)
- (+) Cubre los 6 elementos de RNF01 sin acoplar Application a HTTP; fallos auditados aunque haya
  rollback; centraliza el resultado donde es inequívoco.
- (+) Migración aditiva, sin backfill; reusa `IHttpContextAccessor` estándar.
- (−) Introduce una abstracción nueva y un endpoint filter; dos rutas de escritura (éxito
  in-tx, fallo out-of-tx).
- Esfuerzo: **medio**. Riesgo: bajo-medio.

### Alternativa B — Auditoría de fallo también in-transaction (best-effort)
Escribir la fila `failure` en el mismo `catch` del handler, en la misma conexión.
- (+) Menos piezas (sin writer independiente).
- (−) Si el fallo fue por `DbUpdateException`/rollback, la fila de auditoría **se pierde** → no
  cumple RNF01 de forma fiable.
- Esfuerzo: bajo. Riesgo: **alto** (auditoría no confiable).

### Alternativa C — Outbox de auditoría (tabla intermedia + procesador)
Encolar eventos de auditoría (éxito y fallo) en un outbox y procesarlos async.
- (+) Desacople total; patrón ya usado en Trámites (`identity_validation_outbox`).
- (−) Sobreingeniería para el volumen de configuración OT; latencia de visibilidad; más infra.
- Esfuerzo: **alto**. Riesgo: medio.

## Consecuencias por agente

- **Backend:** migración aditiva (`client_ip`, `result`, `operation`, `error_code`);
  `AddHttpContextAccessor()` + `IAuditContextAccessor`; enriquecer los 4 writers de éxito con
  IP/operation/result=success; `IAuditFailureWriter` + endpoint filter para fallos; guardia y
  código `campos_oficiales_no_editables` en el perfil.
- **Frontend:** sin cambios funcionales; la vista de auditoría puede sumar columnas IP/resultado
  si se desea (opcional, fuera del alcance mínimo).
- **QA:** casos — la fila de auditoría trae IP/operation/result en éxito; un fallo (p. ej.
  `OperationMode` inválido, concurrencia) genera fila `failure` con `error_code`; intento de editar
  campo oficial → 422 + auditoría failure; regresión de las auditorías existentes.
- **Security:** la IP es dato de tráfico; registrar respetando `X-Forwarded-For`. No introducir
  PII adicional. La auditoría de fallos no debe filtrar valores sensibles en `error_code`.
- **Infra:** una migración aditiva; `AddHttpContextAccessor` es estándar. Sin cambios de deploy.

## Nota de tamaño (regla FLIT 9, PR ≤ 800 líneas)

El alcance combinado (schema + accessor + 4 writers + filtro de fallo + RF05 + tests) puede
acercarse al límite. Si el PR supera ~800 líneas, **dividir** en dos HUs: (1) RNF01 éxito
(columnas + IP + operation + result=success en writers) y (2) RNF01 fallo + RF05 (writer de fallo,
endpoint filter, guardia de campos oficiales). Decisión operativa del orquestador al ver el diff.

## Requisito vs decisión (trazabilidad)

| RF | Estado con esta decisión |
|----|--------------------------|
| RNF01 | **Cubierto** — usuario, fecha/hora, IP, operación y resultado (éxito y fallo) |
| RF05 | **Cubierto** — garantía explícita + test de contrato; 422 `campos_oficiales_no_editables` |

## Estado y aceptación

Este ADR queda en **Propuesto**. Pasa a **Aceptado** solo mediante PR de aceptación del Líder
Técnico humano (regla FLIT 15).
