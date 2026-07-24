# Descomposición en HUs — Features de Trámites (#10862 / #10863 / #10864)

> **Estado:** CREADAS en Azure DevOps (Sprint 2) — #10864: HUs #10865–#10869 · #10863: HUs #10870–#10875 · #10862: HUs #10876–#10887 (23 en total).
> **Convención:** HUs por capa `[BACKEND]` / `[FRONTEND]` · Story Points Fibonacci (1/2/3/5/8) · máx. 8 HUs por Feature · assignee humano · Sprint 2.
> **Fecha:** 2026-07-22

---

## Feature #10862 — Reglas transversales del ciclo de vida (6 criterios)

> ⚠️ **Excede el máximo de 8 HUs** al separar por capa (12 HUs). Requiere decisión: dividir el Feature en Backend/Frontend (como F08) o usar HUs fullstack. Ver pregunta al final.

### Backend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| BE-01 | [BACKEND] Duplicidad por familia: cablear `InitialProcedureValidationGate` en consulta de vehículo (MI→VIN, Traspaso→placa; otros permiten múltiples) | CF-01 | 5 | — |
| BE-02 | [BACKEND] Precondición registral doble fuente + bloqueo duro (endurecer `estado_vehiculo` + `VinPolicyEvaluator`; RUNT sin dato bloquea) | CF-03 | 5 | — |
| BE-03 | [BACKEND] Caché de consultas cross-trámite + TTL configurable por fuente (precarga; + Habeas Data) | CF-04 | 8 | — |
| BE-04 | [BACKEND] Persistencia del paso de borrador / soporte de autosave | CF-02 | 2 | — |
| BE-05 | [BACKEND] Reenvío de validación al editar correo + invalidar enlace anterior | CF-05 | 3 | — |
| BE-06 | [BACKEND] Override de documento de prenda por OT (gate `prenda_documento_requerido`) | CF-06 | 3 | — |

### Frontend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| FE-01 | [FRONTEND] Duplicidad: UI de bloqueo + botón "Retomar" en la consulta de vehículo | CF-01 | 2 | BE-01 |
| FE-02 | [FRONTEND] Borrador: autosave por paso + reposición al primer paso incompleto al reabrir | CF-02 | 3 | BE-04 |
| FE-03 | [FRONTEND] Precondición registral: mensajes de bloqueo (ya matriculado / RUNT sin dato) | CF-03 | 2 | BE-02 |
| FE-04 | [FRONTEND] Precarga: prellenado + origen/fecha + acción "Actualizar / volver a consultar" | CF-04 | 3 | BE-03 |
| FE-05 | [FRONTEND] Editar correo: aviso de reenvío + "Copiar enlace" y estado en módulo de identidad | CF-05 | 3 | BE-05 |
| FE-06 | [FRONTEND] Documento de prenda: toggle "obligatorio" en el Admin de OT | CF-06 | 2 | BE-06 |

**Total: 12 HUs · ~41 SP**

---

## Feature #10863 — Gestión del trámite: subsanación + tracking (2 criterios)

### Backend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| BE-01 | [BACKEND] Estado `subsanacion` en la máquina de estados (reactivar `SttWorkflow`) + reapertura de edición post-envío | CF-01 | 5 | — |
| BE-02 | [BACKEND] Observaciones híbridas (motivo + checklist de ítems) + disparadores OT y Quipux/RNMC | CF-01 | 3 | BE-01 |
| BE-03 | [BACKEND] Re-radicación selectiva: diff de campos + re-evaluación de gates de lo corregido + gate final | CF-01 | 5 | BE-01 |
| BE-04 | [BACKEND] Alertas y recordatorios de identidad (eventos + notificaciones in-app a operador/supervisor) | CF-02 | 5 | — |

### Frontend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| FE-01 | [FRONTEND] Subsanación: UI de corrección + checklist de observaciones + acción "Re-radicar" | CF-01 | 3 | BE-01, BE-02 |
| FE-02 | [FRONTEND] Tracking: vista consolidada de identidad por trámite + alertas/recordatorios in-app | CF-02 | 5 | BE-04 |

**Total: 6 HUs · ~26 SP** ✅ (≤8)

---

## Feature #10864 — Prevalidación de identidad (3 criterios)

### Backend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| BE-01 | [BACKEND] Entidad persona/sujeto a nivel tenant + migración + `ProcedureInstanceId` nullable (database-agent) | CF-00 | 8 | — |
| BE-02 | [BACKEND] Crear prevalidación standalone: endpoint sin `{instanceId}` + `IdentitySubject` desde persona/body | CF-01 | 5 | BE-01 |
| BE-03 | [BACKEND] Reutilización automática por referencia (relajar `ProcedureInstance != null`; vincular por persona) | CF-02 | 3 | BE-01 |

### Frontend
| HU | Título | CF | SP | Depende |
|----|--------|----|----|---------|
| FE-01 | [FRONTEND] Pantalla dedicada de prevalidación (crear/gestionar) + enlace desde `Validaciones.tsx` | CF-01 | 5 | BE-02 |
| FE-02 | [FRONTEND] Vista transversal tolera validaciones sin trámite (columnas Trámite/Modalidad nullable) | CF-02 | 2 | BE-03 |

**Total: 5 HUs · ~23 SP** ✅ (≤8)

---

## Resumen y gate

| Feature | HUs | SP | ¿≤8 HUs? |
|---------|-----|----|----------|
| #10862 Reglas transversales | 12 | ~41 | ❌ excede |
| #10863 Gestión del trámite | 6 | ~26 | ✅ |
| #10864 Prevalidación identidad | 5 | ~23 | ✅ |

Las AC en Gherkin (positivas/negativas/borde) se adjuntan a cada HU al momento de crearla con `flit-crear-hu`.
