# ADR-0038: Navegación de reportería unificada — eliminación big-bang de reportes-detallados

**Fecha**: 2026-07-29
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, PO, equipo frontend
**Tags**: arquitectura, frontend, navegacion, modulos, reporteria, big-bang, feature-11076
**Supersedes**: —
**Relacionado**: ADR-0021 (analítica fuente de datos), Feature #10139 (Dashboard analítico original)
**HU origen**: Feature #11076 — Subsistema de Reportería Transaccional V2

---

## Contexto

El dock de navegación actual expone **dos entradas** bajo el dominio de reportería:

1. **`reportes`** (ModuleId: `"reportes"`) — dashboard con overview, productividad, tendencias mensuales,
   scheduling y alertas
2. **`reportes-detallados`** (ModuleId: `"reportes-detallados"`) — listado paginado de trámites con
   exportación Excel síncrona

Esta bifurcación produce:
- Experiencia de usuario fragmentada: dos iconos distintos para el mismo dominio funcional
- Duplicación de lógica de filtros entre ambos módulos
- El módulo `reportes-detallados` usa `DetailedReportEndpoints` con un exportador síncrono que tiene
  un límite práctico de ~5k filas (timeout YARP) y no tiene indicador de progreso
- Permisos duplicados: `detailed-report.*` vs `analytics.*`

El Feature #11076 unifica toda la reportería bajo un único icono `reportes`, con tabs que absorben
las funcionalidades de `reportes-detallados` y añaden las capacidades V2 (SLA, auditoría, consultas
guardadas, preferencias de dashboard).

La decisión central es: **¿cómo se gestiona la transición del módulo `reportes-detallados`?**

---

## Alternativas evaluadas

### Opción A — Absorción progresiva con redirect y componente muerto temporal

Mantener `ReportesDetallados.tsx` como componente existente durante N sprints. Agregar un redirect
302 en `page.tsx` de `?m=reportes-detallados` hacia `?m=reportes&tab=tramites`. Eliminar el
componente en un sprint futuro.

**Pros:**
- Riesgo de regresión reducido (el módulo sigue existiendo durante la transición)
- Los bookmarks de usuario siguen funcionando con redirect automático
- Rollback sencillo (eliminar la lógica de redirect)

**Contras:**
- Deuda técnica deliberada: el componente muerto debe mantenerse y no romperse durante el período
  de convivencia
- Los tests existentes del módulo siguen ejecutándose, potencialmente contra código obsoleto
- El redirect puede mascará problemas en el módulo destino que QA no descubriría hasta eliminarlo
- Complejidad de gestión de permisos: dos conjuntos de slugs activos para el mismo dominio
- El PO debe confirmar explícitamente cuándo eliminar el componente (dependencia humana)

**Esfuerzo:** M — **Riesgo:** BAJO inicial, MEDIO a largo plazo (deuda + olvido de cleanup)

---

### Opción B — Eliminación big-bang completa (sin redirect) ✅ RECOMENDADA Y APROBADA

Eliminar `ReportesDetallados.tsx`, `detailed-report.ts`, la entrada del dock y el ModuleId
`reportes-detallados` en un único PR. Sin redirect en `page.tsx`. Marcando `[Obsolete]` los
endpoints backend y eliminándolos en el sprint +1 tras confirmación de QA.

Los bookmarks anteriores (`?m=reportes-detallados`) resultan en comportamiento no definido
(el módulo simplemente no existe en el dock; el router puede mostrar el dashboard por defecto
o ignorar el parámetro — comportamiento correcto dado que el módulo ya no existe).

**Pros:**
- Sin deuda técnica: el código eliminado no puede romperse ni olvidarse
- Tests E2E del módulo eliminado también se eliminan (sin mantenimiento de código obsoleto)
- El conjunto de permisos queda limpio desde el día del PR (sin slugs `detailed-report.*` activos)
- Fuerza a QA a validar el módulo `reportes` V2 como única fuente de verdad desde el inicio
- No hay dependencia de PO para cleanup futuro

**Contras:**
- Bookmarks existentes dejan de funcionar sin aviso previo al usuario
  (mitigación: comunicación a usuarios en el release notes del sprint)
- Cualquier enlace externo a `?m=reportes-detallados` (emails, documentación) deja de funcionar
- No hay rollback inmediato del módulo eliminado sin revertir el PR completo

**Esfuerzo:** M — **Riesgo:** BAJO (dado que se aprueba una comunicación previa al sprint)

---

### Opción C — Coexistencia indefinida con tabs independientes

Mantener ambos módulos en el dock de forma indefinida. El módulo V2 se lanza como un tercer
icono o se añade como una pestaña dentro de `reportes`.

**Pros:**
- Cero riesgo de regresión para usuarios de `reportes-detallados`
- Máxima compatibilidad con flujos existentes

**Contras:**
- Contradice el objetivo del Feature #11076 (unificación)
- Tres entidades de reportería en el dock (o dos si se fuerza como tab)
- Duplicación permanente de lógica de filtros y permisos
- No resuelve el problema de exportaciones síncronas

**Esfuerzo:** M — **Riesgo:** ALTO a largo plazo (deuda estructural)

---

## Decisión

**Se elige la Opción B — eliminación big-bang, sin redirect.**

Justificación:
- El PO y el Líder Técnico aprobaron explícitamente la estrategia big-bang sin redirect en la
  sesión de revisión arquitectónica del 2026-07-29
- La deuda técnica de Opción A tiene riesgo real de quedar indefinidamente (precedente en el repo:
  varios `// TODO` sin resolver en módulos de transición anteriores)
- La comunicación a usuarios en release notes es suficiente como mitigación de bookmarks rotos

---

## Consecuencias

### Positivas
- Codebase limpio: sin componente zombie, sin redirect, sin mantenimiento de código obsoleto
- Un único módulo `reportes` como dueño de toda la reportería
- Permisos RBAC unificados bajo prefijo `reporting.*` desde el primer día del sprint
- Los tests E2E se reescriben exclusivamente para el módulo unificado

### Negativas
- Bookmarks existentes (`?m=reportes-detallados`) dejan de funcionar. Comunicación proactiva
  en release notes es obligatoria antes del deploy a QA y PDN

### Constraint de gestión
- Los endpoints backend `/api/v1/detailed-report/*` se marcan `[Obsolete]` en el PR del big-bang
  y se eliminan en el sprint siguiente, tras confirmación de QA de que el frontend V2 no los llama
- La eliminación de los endpoints backend es responsabilidad del `backend-agent` en ese sprint +1

---

## Artefactos eliminados en este PR

### Frontend — DELETE completo

| Artefacto | Acción |
|-----------|--------|
| `frontend/components/atom/modules/ReportesDetallados.tsx` | DELETE |
| `frontend/lib/api/detailed-report.ts` | DELETE |
| Tests E2E que navegan a `m=reportes-detallados` | DELETE |

### Frontend — MODIFICAR

| Artefacto | Cambio |
|-----------|--------|
| `frontend/components/atom/Shell.tsx` | Eliminar dock entry con `moduleId: "reportes-detallados"` |
| `frontend/lib/nav/modules.ts` | Eliminar `"reportes-detallados"` de `ALL_MODULE_IDS` y listas relacionadas |
| `frontend/app/page.tsx` | **Sin cambio** — no se agrega redirect |

### Backend — MARCAR OBSOLETO (eliminar sprint +1)

| Artefacto | Acción |
|-----------|--------|
| `src/Flit.Api/Endpoints/Analytics/DetailedReportEndpoints.cs` | Marcar con `[Obsolete("Eliminado en Feature #11076 big-bang. Eliminar en sprint post-QA.")]` |

### RBAC seed — ELIMINAR slugs legados

Los siguientes slugs deben eliminarse del INSERT seed en el mismo PR:
- `detailed-report.read`
- `detailed-report.export`
- Cualquier otro slug con prefijo `detailed-report.*`

El módulo `"reportes-detallados"` en `security.modules` debe marcarse `is_active = false` o
eliminarse del seed si no tiene registros hijos que lo referencien.

---

## Criterio de aceptación para QA

- `GET /?m=reportes-detallados` no carga el componente `ReportesDetallados` (componente no existe)
- El dock no muestra entrada `reportes-detallados`
- `GET /api/v1/detailed-report/*` retorna 200 (con `[Obsolete]`) o 404 (si ya eliminado)
- El módulo `reportes` muestra correctamente el tab `tramites` con la funcionalidad que antes
  estaba en `reportes-detallados`
- No existen slugs `detailed-report.*` activos en la tabla `security.permissions`
