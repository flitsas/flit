# Planificación — Feature #10133
## Administración de Organismos de Tránsito e Inteligencia Documental

**Actualizado:** 2026-06-23  
**Orquestador:** orchestrator-agent  
**PR:** https://github.com/flitsas/flit/pull/28 → `develop`

---

## HUs entregadas (incremental)

| HU | Capa | Alcance | Ruta UI principal |
|----|------|---------|-------------------|
| #10215 | BE | Perfil OT, Dashboard/QX, feature flags | API `/api/v1/admin/ot/profile` |
| #10216 | BE | Webhooks + bitácora API | API webhooks / api-logs |
| #10217 | BE | Aprobación trámites clientes | API client-procedures |
| #10218 | FE | Súper-sección Trámites | `/admin/transit-offices/[id]/tramites` |
| #10219 | FE | Webhooks + bitácora | `/admin/transit-offices/[id]/webhooks` |
| #10220 | FE | Client-procedures | `/admin/transit-offices/[id]/client-procedures` |
| #10221 | BE | Motor reglas AND/OR | API `/api/v1/admin/ot/rules` |
| #10222 | BE | Prelación + etiquetas | API precedence / document-tags |
| #10223 | FE | Constructor reglas | `/admin/transit-offices/[id]/rules` |
| #10224 | FE | Prelación DnD + etiquetas | `/admin/transit-offices/[id]/documents` |

## HU pendiente — Hub consola OT

| HU | Capa | Alcance |
|----|------|---------|
| **#10236** | FE | Hub `/admin/transit-offices` + `OtHubLayout` con navegación a los 5 módulos | **Implementado** — pendiente commit en PR #28 |

## API base OT

Prefijo: `GET|PATCH|POST|DELETE /api/v1/admin/ot/*`  
Catálogo OT (SuperAdmin): `GET /api/v1/admin/transit-offices`

## Código clave

- Frontend: `frontend/app/admin/transit-offices/`, `frontend/components/admin/transit-offices/`, `frontend/lib/api/admin-ot.ts`
- Backend: `Flit.Api/Endpoints/AdminOtEndpoints.cs`, `Flit.Admin.Application/Ot*`

## Gates pendientes

- [ ] Review formal PR #28 (code-review + security)
- [ ] Activar e implementar HU #10236
- [ ] Merge PR (Líder Técnico)
- [ ] Resolved HUs post Deploy DEV
