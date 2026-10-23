# Auditoría transversal admin / Reportes / Usuarios — HU #12732 (E.4)

> Feature #12722 · Plan `docs/plan-tecnico-ajustes-ui-observaciones-2026-09-07.md` §E.4 · 2026-09-21

Checklist aplicado: (1) superficie plana sin card de layout, (2) `ModuleTitle` + `action`, (3) tablas estándar, (4) tokens de color, (5) botones secundarios azul D7, (6) dark mode en superficies tocadas.

| Página / módulo | Hallazgo | Estado |
|-----------------|----------|--------|
| **OT hub — `OtHubLayout` default** | Card contenedora `bg-card` por defecto | **Corregido** (#12731 + verificado E.4) |
| OT — Usuarios activos | OK previo (`UsersTable` + `ModuleTitle`) | OK |
| OT — Usuarios eliminados | Tabla cruda sin `DataTable` | **Corregido** (#12731) |
| OT — Motor de reglas | Tabla cruda + columnas detalle | **Corregido** (#12731) |
| OT — Mandatos | Columnas detalle en tabla | **Corregido** (#12731) |
| OT — Prelación / Prenda | Switches a 1 columna | **Corregido** (#12731) |
| OT — Reportes | Superficie plano + tablas card-list | OK previo |
| OT — Trámites clientes / Webhooks / etc. | Tablas `<table>` propias sin `DataTable` | **Diferido** — migración >800 líneas; patrón card-list ya alineado en reportes (#10494) |
| **`/admin/transit-offices`** | Card layout `bg-white/60` + back link inline `#557EFF` | **Corregido** |
| **`/admin/companies`** | Card layout + `CreateButton` fuera de `ModuleTitle.action` | **Corregido** |
| **`/admin/companies/[tenantId]`** | Card layout + acción «Panel de red» fuera de `action` | **Corregido** |
| **`/admin/companies/[tenantId]/children`** | Card layout principal | **Corregido** |
| **`/admin/companies/.../children` — dominio MB** | Subsección `bg-white/60` (contenido, no layout) | **Diferido** — card de feature (dominio MB), no contenedor de página |
| **`/admin/companies/[tenantId]` — dominio MB** | Idem subsección dominio SuperAdmin | **Diferido** — idem |
| **`/admin/banners`** | `CreateButton` fuera de `ModuleTitle.action` | **Corregido** |
| **`/admin/documents`** | Card layout + acciones header + botón secundario inline | **Corregido** |
| **`/admin/documents/procedures`** | Card layout envolvente | **Corregido** |
| **`/admin/documents/procedures/[id]`** | Card layout envolvente | **Corregido** |
| **`/admin/generacion-documental/*`** (5 rutas) | Card layout en todas las pestañas | **Corregido** |
| **`/admin/improntas/*`** (2 rutas) | Card layout | **Corregido** |
| **`/admin/plataforma/*`, jobs, quipux, rbac, migracion, causales, improntas layout** | Back links inline `#557EFF` (sin card layout en la mayoría) | **Diferido** — solo tokens back link; sin card de layout detectada; bajo impacto visual |
| **`/admin/companies` — `CompanyListTable`** | `<table>` crudo con cabecera inline `#DFE5ED` | **Diferido** — ya usa `StatusBadge`/`RowActions`/`Pagination`; migrar a `DataTable` = refactor HU aparte |
| **`/admin/banners` — `BannerListTable`** | `<table>` con `table-styles` compartidos | **Diferido** — patrón card-list alineado; no bloqueante E.4 |
| **Módulo global `Reportes`** | Botón «Programación y alertas» sin outline D7; icono `#557EFF` inline | **Corregido** |
| **Módulo global `Usuarios`** | Pestañas con `#557EFF` inline en activo | **Corregido** (tokens `--flit-brand-ink` / `--color-flit-brand`) |
| **Módulo global `Usuarios`** | `ModuleTitle.action` + `UsersTable` | OK previo |
| **Tablas admin restantes** (~25 componentes con `<table>`) | Sin `DataTable` wrapper | **Diferido** — deuda D8/E.4: migración incremental por módulo; riesgo regresión en PR >800 líneas |

## Deuda anotada (motivo)

1. **Migración masiva a `DataTable`**: muchas tablas admin ya comparten `table-styles`, `RowActions`, `Pagination` y `StatusBadge` pero no el wrapper `DataTable`. Migrar todas excede el alcance cerrado de E.4 y el límite de PR.
2. **Subsecciones de feature** (dominio Marca Blanca, branding): cards intencionales de contenido, no contenedor de layout de página.
3. **Back links en consolas plataforma/jobs/quipux**: pendiente aplicar `ADMIN_BACK_LINK_CLS` en oleada siguiente (sin card layout que corregir).
4. **D8 — `WizardAccordion` → primitivo compartido**: sin cambio en esta ola (plan técnico).

## Archivos de utilidades introducidos

- `frontend/components/admin/admin-ui-styles.ts` — `ADMIN_BACK_LINK_CLS`, `ADMIN_BRAND_OUTLINE_BTN_CLS`, `ADMIN_CONTENT_SURFACE_CLS`

## Tests añadidos / actualizados

- `frontend/__tests__/admin-audit-12732-surface.test.tsx` — superficie plana en consolas OT y compañías
- `frontend/__tests__/reportes-superficie-y-tablas.test.tsx` — hub OT plano por defecto (#12731)
