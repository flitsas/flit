# Ajuste visual — Dock / menú inferior (sin HU ADO)

**Fecha:** 2026-08-03 (actualizado: agrupadores + scroll invertido + favicon)  
**Estado:** Documentación local — **no hay Feature/HU en Azure DevOps**  
**Alcance:** Solo el menú (dock)  
**Fuera de alcance:** Backend, DB, topbar

## Comportamiento scroll

| Posición | Menú |
|---|---|
| Arriba (inicio de scroll) | Más pequeño + nombres visibles |
| Abajo (al bajar) | Más grande + solo iconos (`sr-only` conserva el nombre accesible) |

## Agrupadores

| Agrupador | Opciones |
|---|---|
| Trámites / Identidad / Reportes / Usuarios | Píldoras SPA (RBAC) |
| Administración | Admin OT: Reglas, Documentos, Requisitos |
| Administradores | Compañías, Tránsito, Documental, Improntas, Quipux, RBAC Admin, Auditoría (SuperAdmin). AdminCompany: píldora “Administración”. |
| Plataforma | Mandatos (submenú forzado; SuperAdmin) |
| Integraciones | Log QX, Log ICT |
| Ayuda | Ayuda |

Regla: 1 opción visible → píldora directa con el label del ítem; 2+ → panel hacia arriba.

## FAB

Icono central: `/assets/favicon.svg` (copiado desde `favicon.svg` del repo).

## Archivos

- `frontend/components/atom/dock/*`
- `frontend/components/atom/Shell.tsx`
- `frontend/public/assets/favicon.svg`
- `frontend/app/globals.css`
