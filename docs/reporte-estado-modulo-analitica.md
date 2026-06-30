# Reporte de Estado — Módulo de Analítica / Reportes (Feature #10139)

> **Fecha:** 2026-06-30
> **Alcance:** Estado del módulo de Reportes y su integración con el módulo de Trámites (procedures).
> **Veredicto resumido:** El módulo está **implementado, integrado en DEV y funcional con datos de _seed_**, pero **NO está conectado a los datos reales de trámites en runtime**. Faltan 2 piezas para que los reportes reflejen la operación real: (1) un mecanismo de _refresh_ de los agregados, y (2) trazabilidad de autoría (`changed_by`) en la radicación. Detalle abajo.

---

## 1. Resumen ejecutivo

El Feature #10139 entregó un dashboard analítico completo (8 HU, PR #57 mergeado a `develop`) con frontend, API, agregados materializados y exports (Excel/PDF). La cadena técnica está bien construida y la UI consume el backend real (sin mocks).

Sin embargo, el módulo tiene una **arquitectura de datos híbrida** que hoy NO está cerrada para producción:

| Capa | Fuente de datos | ¿Refleja trámites reales hoy? |
|------|-----------------|-------------------------------|
| Donuts por categoría (`/overview`) | Tabla agregada `analytics.procedure_metrics_daily` | ❌ Solo si se corre el refresh; en PROD queda vacía |
| Top 5 productividad (`/productivity/top`) | Tabla agregada `analytics.user_productivity_daily` | ❌ Vacía + depende de `changed_by` que no se puebla en Submit |
| Tabla de detalle (`/procedures`) | **En vivo** desde `tramites.procedure_instances` | ✅ Sí |
| Export Excel (`/export/excel`) | **En vivo** desde `tramites.procedure_instances` | ✅ Sí |
| Export PDF ejecutivo (`/export/executive-pdf`) | Agregados | ❌ Mismo problema que `/overview` |

**Consecuencia visible para el usuario:** en un tenant real, los **donuts y el Top 5 aparecerán vacíos o desactualizados**, mientras que **la tabla de detalle y el Excel SÍ mostrarán trámites reales**. Esto produce una incoherencia: el usuario hace clic en un donut "en cero" pero el panel lateral lista trámites — o al revés.

---

## 2. Arquitectura actual (cadena de datos)

```
Frontend (Next.js)                 API (.NET)                          PostgreSQL
─────────────────                  ──────────                          ──────────
Reportes.tsx                       AnalyticsEndpoints.cs
  │ lib/api/analytics.ts             /api/v1/analytics/*
  │                                       │
  ├─ fetchAnalyticsOverview ──────► /overview ──► GetAnalyticsOverviewHandler ──┐
  │                                                                              ▼
  │                                                          AnalyticsReadRepository
  │                                                              │  SELECT … FROM
  │                                                              ▼
  ├─ fetchTopProducers ───────────► /productivity/top ──────►  analytics.procedure_metrics_daily   ◄─┐  (AGREGADOS
  │                                                             analytics.user_productivity_daily   ◄─┤   materializados)
  │                                                                                                   │
  │                                                       ┌───── refresh_procedure_aggregates() ──────┘
  │                                                       │      (función SQL — NUNCA se invoca en runtime)
  │                                                       │
  ├─ fetchProcedureDetails ───────► /procedures ─────►  tramites.procedure_instances   ◄─┐
  ├─ exportAnalyticsExcel ────────► /export/excel ───►  tramites.procedure_types        ◄─┤  (LECTURA EN VIVO)
  │                                                      identity.users                  ◄─┘
  └─ exportExecutivePdf ──────────► /export/executive-pdf ─► agregados (mismo problema)
```

**El núcleo del problema:** la función SQL `analytics.refresh_procedure_aggregates(tenant, from, to)` es el "puente" que copia datos de `tramites.*` a `analytics.*`. Existe y es correcta, pero **solo se ejecuta una vez, en el seed de desarrollo**. No hay job, scheduler, `BackgroundService`, trigger ni endpoint que la dispare con datos reales.

---

## 3. Inventario de componentes (con evidencia)

### Backend — Módulo de analítica (`Flit.Analytics.Application`, read-side/CQRS)
- **Endpoints:** `services/core-api/src/Flit.Api/Endpoints/Analytics/AnalyticsEndpoints.cs`
  - `GET /overview` · `GET /productivity/top` · `GET /procedures` · `GET /export/excel` · `POST /export/executive-pdf`
  - Policy `AdminCompany`; tenant del claim JWT `tenant_id`; SuperAdmin puede pasar `tenantId`; Tenant Admin pidiendo otro tenant → 403; rango inválido → 400.
- **Repositorio:** `services/core-api/src/Flit.Infrastructure/Persistence/Repositories/AnalyticsReadRepository.cs`
  - `GetOverviewAsync` / `GetTopProducersAsync` → leen `analytics.*` (agregados).
  - `GetProcedureDetailsAsync` / `ExportProcedureDetailsAsync` → leen `tramites.*` (en vivo, con CTE `base`).
- **Agregados (DDL):** `…/Persistence/Sql/Ddl/10-HU10153-analytics.sql` (tablas `procedure_metrics_daily`, `user_productivity_daily`, RLS, triggers).
- **Función de refresh:** `…/Persistence/Sql/Ddl/20-HU10240-analytics-refresh.sql` → `analytics.refresh_procedure_aggregates()`.
- **Seed DEV:** `…/Persistence/Sql/Ddl/21-HU10240-analytics-dev-seed.sql` (única invocación del refresh, solo en Development).

### Backend — Módulo de trámites (origen de datos)
- **Schema:** `tramites` (`…/Persistence/Schemas/SchemaNames.cs`).
- **Tablas clave para analítica:**
  - `tramites.procedure_instances` (id, tenant_id, procedure_type_id, status, created_by_user_id, submitted_at, completed_at, created_at, deleted_at).
  - `tramites.procedure_types` (family = `MATRICULAS` | `TRASPASO` | `OTROS`).
  - `tramites.procedure_instance_status_history` (from_status, to_status, changed_at, **changed_by**).

### Frontend — Módulo de reportes
- **Componente:** `frontend/components/atom/modules/Reportes.tsx` + `_reportes/` (DateRangeFilter, CompanySelector, CategoryDonut, ProductivityCards, ProcedureDetailPanel, ExportButtons).
- **API client:** `frontend/lib/api/analytics.ts` (5 funciones tipadas), tipos en `frontend/lib/api/types.ts`.
- **Sin mocks**: todo consume el backend real vía `apiFetch` con JWT. Recharts para donuts.

---

## 4. Hallazgos / Brechas (gaps) para conectar con datos reales

### 🔴 GAP-1 (CRÍTICO) — No existe refresh de agregados en runtime
La función `analytics.refresh_procedure_aggregates()` solo se invoca en el seed DEV (`21-HU10240-analytics-dev-seed.sql`). No hay `BackgroundService`/`IHostedService` para analítica (los únicos hosted services del repo son `IdentityValidationOutboxProcessor` y `IdentityValidationSendRetryProcessor`, ajenos a analítica), ni endpoint de admin, ni trigger, ni cron.
- **Impacto:** en QA/PROD las tablas `analytics.*` quedan **vacías** → `/overview`, `/productivity/top` y `/export/executive-pdf` devuelven cero/vacío para tenants reales.
- **Evidencia:** `20-HU10240-analytics-refresh.sql` (define la función), búsqueda de `IHostedService`/`refresh_procedure_aggregates` sin coincidencias de invocación en runtime.

### 🟠 GAP-2 (ALTO) — La radicación (Submit) no registra `changed_by`
`SubmitProcedureInstanceHandler` escribe el `status_history` de `draft → submitted` **sin** asignar `ChangedBy` (queda NULL). El refresh de productividad filtra `WHERE h.changed_by IS NOT NULL`.
- **Impacto:** `user_productivity_daily.submitted_count` **nunca se poblará** con radicaciones reales → la columna "Enviados" del Top 5 quedará en cero aunque haya operación.
- **Evidencia:** `SubmitProcedureInstanceCommand.cs:67-77` (objeto `ProcedureInstanceStatusHistory` sin `ChangedBy`) vs. `OtClientProcedureRepository.cs:187` (el flujo OT **sí** asigna `ChangedBy = resolvedChangedBy`).

### 🟠 GAP-3 (ALTO) — Embudo de estados incompleto en `status_history`
Solo se escriben transiciones a `draft`, `submitted`, `approved_ot` y `rejected_ot`. **No** se escriben `in_review`, `pending_ot`, `completed` ni `cancelled`.
- **Impacto en analítica:** el refresh de productividad cuenta `approved_count = approved_ot + completed` y `rejected_count = rejected_ot + cancelled`, pero `completed`/`cancelled` nunca se registran → esos conteos quedan subvalorados. Además, las aprobaciones por **webhook** Quipux pasan `approvedBy: null` (`ProcessOtWebhookCallbackHandler.cs:180`) → tampoco suman a productividad.
- **Nota colateral (fuera de analítica):** nada en el código transiciona a `pending_ot`, pero `ApproveAsync`/`RejectAsync` exigen ese estado (`OtClientProcedureRepository.cs:112,127`). Conviene verificar la integridad del flujo OT end-to-end.
- **Evidencia:** únicos puntos de escritura de `status_history` → `CreateProcedureInstanceCommand.cs:94`, `SubmitProcedureInstanceCommand.cs:67`, `OtClientProcedureRepository.cs:179`.

### 🟡 GAP-4 (MEDIO) — Doble fuente de verdad / incoherencia donut vs. detalle
`/overview` lee agregados y `/procedures` lee en vivo. Con los agregados desactualizados, el total del donut y las filas del panel de detalle **no cuadran**.
- **Impacto:** incoherencia de UX y pérdida de confianza en el reporte.
- **Resolución:** se cierra al resolver GAP-1 (o al unificar todo a lectura en vivo — ver §6).

### 🟡 GAP-5 (MEDIO) — Normalización de categorías colapsa todo lo no-MATRICULAS/TRASPASO a "otros"
El SQL normaliza `family` a `matriculas` / `traspasos` / `otros`. Cualquier familia nueva (p. ej. `VEHICULAR`) cae en "otros" silenciosamente.
- **Acción:** confirmar los valores reales de `procedure_types.family` en QA/PROD y decidir si "otros" debe desglosarse.
- **Evidencia:** `AnalyticsReadRepository` (CASE de normalización en `GetOverviewAsync` y CTE `base`).

### ⚪ GAP-6 (BAJO) — `completed_at` posiblemente nunca se setea
`/procedures` y el detalle exponen `completed_at`, pero no se encontró transición que lo asigne (relacionado con GAP-3). La columna "Completado" del detalle saldría siempre vacía.

---

## 5. Mapa de transiciones de estado e impacto en analítica

| Transición | Dónde se escribe `status_history` | `changed_by` | ¿Cuenta en productividad? |
|------------|-----------------------------------|--------------|----------------------------|
| `null → draft` | `CreateProcedureInstanceCommand.cs:94` | NULL | No (no aplica) |
| `draft → submitted` | `SubmitProcedureInstanceCommand.cs:67` | **NULL** ❌ | **No** (debería contar como `submitted`) |
| `submitted → in_review` | — (no se escribe) | — | No |
| `→ pending_ot` | — (no se escribe) | — | No |
| `pending_ot → approved_ot` (manual) | `OtClientProcedureRepository.cs:179` | ✅ sí | Sí, si hay usuario real |
| `pending_ot → approved_ot` (webhook) | `OtClientProcedureRepository.cs:179` | NULL (`approvedBy: null`) | No |
| `pending_ot → rejected_ot` (manual) | `OtClientProcedureRepository.cs:179` | ✅ sí | Sí, si hay usuario real |
| `→ completed` | — (no se escribe) | — | No (pero el refresh lo cuenta) |
| `→ cancelled` | — (no se escribe) | — | No (pero el refresh lo cuenta) |

**Lectura clave:** los **donuts de `/overview` SÍ funcionarían** con solo resolver GAP-1 (se agregan por estado actual de la instancia, no dependen de `changed_by`). El **Top 5 de productividad** necesita además GAP-2 y GAP-3.

---

## 6. Recomendaciones — Cómo conectar la analítica con los trámites reales

Hay tres estrategias para cerrar GAP-1. Recomendación destacada primero.

### ✅ Opción A (RECOMENDADA) — Refresh programado + refresh on-demand
1. **`BackgroundService`** (`AnalyticsRefreshHostedService`) que, cada N minutos, itera tenants activos y llama a `analytics.refresh_procedure_aggregates(tenant, hoy-1, hoy)` (ventana incremental). Reutiliza el patrón ya presente en `IdentityValidationOutboxProcessor`.
2. **Endpoint admin** `POST /api/v1/analytics/refresh` (policy SuperAdmin/AdminCompany) para forzar recálculo bajo demanda, útil tras cargas o para QA.
- **Pros:** datos casi en tiempo real, costo de consulta bajo (lecturas sobre agregados), encaja con la arquitectura existente.
- **Contras:** ventana de desfase (minutos); requiere job operativo.

### Opción B — Vista materializada con refresh incremental o triggers
Convertir los agregados en _materialized views_ o poblarlos vía triggers `AFTER INSERT/UPDATE` sobre `procedure_instances` / `status_history`.
- **Pros:** consistencia más fuerte.
- **Contras:** triggers añaden latencia a la escritura del flujo de trámites; más complejidad de mantenimiento y RLS.

### Opción C — Lectura 100% en vivo (eliminar agregados)
Reescribir `GetOverviewAsync` y `GetTopProducersAsync` para consultar directamente `tramites.*` (como ya hace `/procedures`), y eliminar las tablas `analytics.*`.
- **Pros:** una sola fuente de verdad → elimina GAP-1 y GAP-4 de raíz; menos infraestructura.
- **Contras:** mayor costo por consulta en dashboards con muchos datos; requiere índices adecuados (`created_at`, `tenant_id`, `status`). Mitigable con caché corta.

> **Sugerencia:** dado que `/procedures` ya lee en vivo sin problemas, la **Opción C** es la de menor riesgo conceptual y elimina la incoherencia donut↔detalle; la **Opción A** es preferible si se espera alto volumen y se quiere proteger la BD operacional. Decisión de arquitectura → candidata a **ADR**.

### Independiente de la opción elegida (para que la productividad sea fiel):
- **Resolver GAP-2:** propagar el usuario autenticado al `SubmitProcedureInstanceHandler` y asignar `ChangedBy` en el `status_history` de la radicación.
- **Resolver GAP-3:** registrar `status_history` en TODAS las transiciones (`in_review`, `pending_ot`, `completed`, `cancelled`) con su `changed_by`, y decidir cómo atribuir productividad a aprobaciones por webhook (`approvedBy: null`).
- **Validar GAP-5/6** contra datos reales de QA.

---

## 7. Plan de acción propuesto (borrador de HUs)

| # | Tipo | Título sugerido | Cierra |
|---|------|------------------|--------|
| 1 | BACKEND | Decisión de arquitectura (ADR): agregados con refresh vs. lectura en vivo | GAP-1, GAP-4 |
| 2 | BACKEND | `AnalyticsRefreshHostedService` + endpoint `POST /analytics/refresh` (si Opción A) | GAP-1 |
| 3 | BACKEND | Propagar `changed_by` en la radicación (Submit) | GAP-2 |
| 4 | BACKEND | Completar `status_history` en transiciones in_review/pending_ot/completed/cancelled | GAP-3, GAP-6 |
| 5 | BACKEND | Atribución de productividad para aprobaciones por webhook | GAP-3 |
| 6 | DATA/QA | Verificar valores reales de `procedure_types.family` y desglose de "otros" | GAP-5 |
| 7 | QA | Pruebas de coherencia donut↔detalle con datos reales en QA | GAP-4 |

> **Gate FLIT:** estas HUs deben crearse en ADO (Sprint siguiente, no el activo), pasar DoR y activarse con confirmación humana antes de implementar. Este documento es solo el diagnóstico — no inicia implementación.

---

## 8. Conclusión

El módulo de reportes está **bien construido y entregado**, pero opera hoy sobre **datos sintéticos**. Para "conectarlo con la información de los trámites" se requiere:

1. **Cerrar el puente de agregados** (GAP-1) — es el bloqueante principal y el de mayor impacto visible.
2. **Dar trazabilidad de autoría y completar el embudo de estados** (GAP-2, GAP-3) para que la productividad y los conteos de aprobación/rechazo sean fieles.
3. **Definir la fuente de verdad** (agregados vs. en vivo) vía ADR para eliminar la incoherencia estructural (GAP-4).

Con esos cambios, los donuts, el Top 5, el detalle y los exports reflejarán la operación real del módulo de trámites de forma consistente.
