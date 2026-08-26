# Ajuste UX — Representantes legales (Admin)

- **Fecha:** 2026-08-03 (actualizado — pase Design Guardian)
- **Alcance:** Admin compañías → Representantes legales
- **Fuera de alcance:** Radicación / wizard de trámites

## Flujo vigente

### 1. Crear RL
Panel lateral (`OtSidePanel` `2xl` + superficie modal `#EEF5FF`) con persona, trámites y firma/identidad opcionales. Sin empresas en el alta.

### 2. Listado — acciones por fila (iconos lineales)

| Botón | Icono | Qué hace |
|-------|-------|----------|
| **Editar** | Pencil | Panel: persona + trámites + firma/identidad |
| **Empresas** | Building2 | Panel: NITs + escrituras (si empresa persistida) |
| **Eliminar** | Trash2 | Confirmación y baja |

> **Ver** oculto del grid (decisión de producto). El modo `view` permanece en código.

### 3. Jerarquía

```
Persona (+ trámites + firma/identidad)
  └─ Empresas (NIT) — se pueden crear sin escritura
       └─ Escrituras — solo con company.id persistido
```

## Pase Design Guardian (2026-08-03)

| Hallazgo | Corrección |
|----------|------------|
| CTA sólido sin gradiente | `RL_GRADIENT.primary` (`#557EFF` → `#00DBD5`) |
| Hex ad hoc dispersos | Centralizados en `rl-flit-styles.ts` (tokens FLIT + CTA admin) |
| Acciones solo texto | Iconos lineales + label (sm+) + `aria-label` |
| Ver oculto | Rehabilitado |
| `dark:` en filas | Eliminado |
| Título sin tarjeta | `RepresentativesAndVaultTab` en card blanca con sombra FLIT |
| Modal sin fondo prototipo | `OtSidePanel surface="modal"` + `FullScreenShell` `#EEF5FF` |

## Roles

| Rol | Qué hizo |
|-----|----------|
| UX/UI (`flit-design-guardian`) | Auditoría + correcciones de fidelidad |
| Dev front | Aplicación de tokens/CTAs/iconos |
| Test | Vitest del módulo RL actualizado |
| QA formal | Gap (sin HU / sin E2E) |
