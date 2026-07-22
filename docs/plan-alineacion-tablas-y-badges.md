# Plan — Alineación de tablas + unificación de badges de estado

| Campo | Valor |
|---|---|
| **Origen** | `docs/reporte-desalineamiento-tablas.md` (diagnóstico) |
| **Objetivo** | (A) Unificar los badges de estado en una paleta semántica consistente; (B) crear un componente de tabla compartido y migrar las tablas divergentes (Trámites, RBAC, OT‑Usuarios) al patrón canónico |
| **Alcance** | `frontend/` — capa de componentes compartidos + 3 tablas divergentes (+ 2 grid parciales opcionales) |
| **No incluye** | Cambios de datos/lógica de negocio; reescritura de tablas ya alineadas (se migran en una fase posterior opcional) |
| **Rama sugerida** | `feature/AB-XXXXX-alineacion-tablas-badges` desde `develop` |

---

## Parte A — Sistema de badges de estado unificado

### A.1 Problema actual
- **Dos formas**: `StatusBadge` (tintado translúcido + borde) vs `OtStatusBadge`/`OtUserStatusBadge` (relleno sólido, texto blanco).
- **Mismo estado, colores distintos**: "positivo" aparece como teal `#00DBD5` **y** tres verdes (`#8CC63F`, `#5B8A1F`, `#5a8a1f`).
- **Semántica rota**: `OtStatusBadge` mapea `warning → #557EFF` (azul), no ámbar.
- **Colores dispersos** como hex/rgba en cada dominio (`StatusBadge` recibe `bg/color/border` crudos), sin tokens.

### A.2 Paleta semántica (5 tones) — la unificación de colores

Un único set de 5 tones, con valores tintados (fondo translúcido + texto + borde) para claro y oscuro. Se definen como **variables CSS en `globals.css`** para ser theme‑aware.

| Tone | Uso (vocabulario) | Base marca | Claro (bg / texto / borde) | Oscuro (bg / texto / borde) |
|---|---|---|---|---|
| **success** | activo, aprobado, publicado, listo, validado | `flit-tech` teal `#00DBD5` | `rgba(0,219,213,.15)` / `#0f766e` / `rgba(0,219,213,.35)` | `rgba(0,219,213,.18)` / `#5eead4` / `rgba(0,219,213,.35)` |
| **warning** | pendiente, en revisión, pendiente firma/validación | ámbar `#F59E0B` | `rgba(245,158,11,.15)` / `#b45309` / `rgba(245,158,11,.35)` | `rgba(245,158,11,.20)` / `#fbbf24` / `rgba(245,158,11,.40)` |
| **danger** | rechazado, inactivo, vencido, error | `flit-alert` `#FF4E00` | `rgba(255,78,0,.12)` / `#c2410c` / `rgba(255,78,0,.32)` | `rgba(255,78,0,.20)` / `#fca574` / `rgba(255,78,0,.40)` |
| **info** | en proceso, en curso, informativo | `flit-brand` `#557EFF` | `rgba(85,126,255,.14)` / `#3b4fd6` / `rgba(85,126,255,.35)` | `rgba(85,126,255,.22)` / `#a5b8ff` / `rgba(85,126,255,.40)` |
| **neutral** | borrador, archivado, sin estado | `flit-gray` `#DFE5ED` | `rgba(223,229,237,.55)` / `#445569` / `rgba(223,229,237,.9)` | `rgba(255,255,255,.08)` / `#cbd5e1` / `rgba(255,255,255,.15)` |

> Decisión de consolidación: **"positivo" = teal** (token de marca `flit-tech`). Los tres verdes se retiran del vocabulario de estado. La forma del chip es única (tintado + borde, `rounded-full`), sin variante de relleno sólido.

### A.3 Refactor de `StatusBadge` (API por tone)
- `components/atom/StatusBadge.tsx`: añadir prop `tone: "success" | "warning" | "danger" | "info" | "neutral"` que aplica las variables CSS (`var(--badge-<tone>-bg/-fg/-border)`).
- Mantener temporalmente la API cruda (`bg/color/border`) marcada `@deprecated` para migración incremental; eliminarla al final.
- Definir las variables en `app/globals.css` (`:root` y `.dark`) según A.2.

### A.4 Consolidar los badges duplicados
- **Eliminar** `OtStatusBadge` y `OtUserStatusBadge`; reemplazar sus usos por `<StatusBadge tone=… label=… />`.
- Reemplazar los chips **inline** (RbacAdmin módulos, DocumentTypeListTable, ProcedureTypeList, TramitesTable, Validaciones, etc.) por `StatusBadge` con `tone`.
- Crear **helpers de mapeo estado→tone por dominio** (una función pequeña por vocabulario), p. ej.:
  - `tramiteTone(estado)`: rechazado→`danger`, pendiente firma→`info`, listo→`success`, validada→`success`, pendiente validación→`warning`.
  - `companyTone(activo)`, `rbacTone(activo)`, `otUserTone(estado)`, `procedureTypeTone(draft|published|archived)`.
- Estos helpers viven junto al `StatusBadge` (`components/atom/status/`) para reuso.

**Archivos afectados (badges):** `StatusBadge.tsx`, `app/globals.css`, `OtStatusBadge.tsx` (elim.), `OtUsersSection.tsx`, `CompanyListTable.tsx`, `DocumentTypeListTable.tsx`, `ClientProceduresTable.tsx`, `RulesSection.tsx`, `WebhooksSection.tsx`, `QuipuxQueueList.tsx`, `TramitesProcedureList.tsx`, `TramitesTable.tsx`, `RbacAdmin.tsx`, `Usuarios.tsx`, `Validaciones.tsx`, `Auditoria.tsx`, `LogQx.tsx`, `superadmin/ProcedureTypeList.tsx`.

---

## Parte B — Componente de tabla compartido (`DataTable`)

### B.1 API propuesta
`components/atom/DataTable.tsx` (+ tipos):
```tsx
type Column<T> = {
  key: string;
  header: ReactNode;
  render: (row: T) => ReactNode;
  align?: "left" | "right" | "center";
  minWidth?: number;          // px, para el min-w total responsive
  headerClassName?: string;
};
type DataTableProps<T> = {
  columns: Column<T>[];
  rows: T[];
  getRowKey: (row: T) => string;
  onRowClick?: (row: T) => void;
  status?: "loading" | "error" | "ready";  // integra UiStateBoundary
  emptyMessage?: string;
  pagination?: { page; pageSize|totalPages; totalCount; onPageChange };
  minWidth?: number;          // ancho mínimo antes de scroll horizontal
};
```

### B.2 Estilo canónico (encapsulado)
- Estructura `<table className="w-full border-separate border-spacing-y-2 text-xs">` envuelta en `<div className="overflow-x-auto">` (responsive nativo, ya no hay que recordarlo en cada pantalla).
- **Cabecera píldora**: `<th>` con token `bg-muted` (== `#DFE5ED`), `rounded-l-xl`/`rounded-r-xl` en extremos, `text-[10px] font-semibold uppercase text-foreground`.
- **Filas‑tarjeta**: `<tr className="bg-card">`, celdas `border-y` (+`border-l/r` extremos), `px-4 py-3`, `rounded-l/r-xl`.
- **Tokens** (`bg-card`/`bg-muted`/`text-foreground`/`border`) en vez de hex → theme‑aware por defecto.
- **Paginación**: usa la `Pagination` compartida (`@/components/atom/Pagination`) — se elimina toda paginación local.
- Estados loading/error/empty vía `UiStateBoundary`.

### B.3 Migrar las 3 divergentes → `DataTable`
| Tabla | Trabajo | Notas |
|---|---|---|
| **Trámites** `operacion/TramitesTable.tsx` | Reemplazar el grid + `<ul>` por `DataTable`; mover estrella de prioridad y acciones a `render` de columnas; **eliminar `Pagination` local**; badges → `StatusBadge tone`. | La más grande; conserva filtros/toolbar existentes, solo cambia la tabla. |
| **RBAC** `atom/modules/RbacAdmin.tsx` | Tabla de módulos y de roles → `DataTable`; filas `border-b` → tarjetas; badge de módulos inline → `StatusBadge tone`. Fila expandible de permisos: usar `render` con detalle bajo la fila o un panel. | Ojo con la fila expandible de permisos (patrón especial). |
| **OT‑Usuarios** `admin/transit-offices/OtUsersSection.tsx` | Las 2 tablas (activos/eliminados) → `DataTable`; normalizar cabecera (`#557EFF`→token, uppercase, `text-[10px]`); badge local → `StatusBadge tone`. | — |

### B.4 (Opcional) Grid parciales
- `Validaciones` y `Auditoria` (grid con filas‑tarjeta, ya "casi" alineadas): migrarlas a `DataTable` para consistencia total y eliminar la `PaginationBar`/`Pagination` local de Validaciones. **Fase posterior**, no bloqueante.
- Tablas ya alineadas (`CompanyListTable`, OT `<table>`): migrar a `DataTable` como limpieza incremental (no urgente; ya se ven bien).

---

## Orden de ejecución y fases

1. **Fase A1 — Fundación de badges**: paleta en `globals.css` + `StatusBadge` con `tone` + helpers de mapeo. (No rompe nada; API cruda queda deprecada.)
2. **Fase A2 — Migración de badges**: reemplazar `OtStatusBadge`/`OtUserStatusBadge`/inline por `StatusBadge tone` en todos los dominios; eliminar los componentes duplicados.
3. **Fase B1 — `DataTable`**: crear el componente + tests.
4. **Fase B2 — Migrar Trámites**.
5. **Fase B3 — Migrar RBAC**.
6. **Fase B4 — Migrar OT‑Usuarios**.
7. **(Opcional) Fase B5 — Validaciones/Auditoría**.

> Cada fase = un commit; PR(s) contra `develop` ≤ 800 líneas (probablemente 2 PRs: badges y tablas).

## Riesgos
- **Trámites/RBAC** tienen interacciones propias (estrella de prioridad, fila expandible de permisos): el `DataTable` debe soportar acciones por fila y contenido expandible sin perder el comportamiento actual.
- Cambiar el color de "positivo" (verde→teal) es un cambio visible: validar con diseño que teal es el estándar deseado.
- Dark mode: verificar contraste de cada tone en claro y oscuro (checklist con capturas).

## Criterios de aceptación
1. Existe una única fuente de verdad de color de estado (5 tones en `globals.css`); no quedan hex/rgba de estado dispersos ni `OtStatusBadge`/`OtUserStatusBadge`.
2. Todos los badges de estado usan `<StatusBadge tone=…>`; el mismo estado se ve idéntico en toda la app (forma, tamaño, color) en claro y oscuro.
3. Trámites, RBAC y OT‑Usuarios renderizan con `DataTable` (cabecera píldora + filas‑tarjeta + `Pagination` compartida), visualmente iguales a `CompanyListTable`.
4. No hay paginaciones locales duplicadas en las tablas migradas.
5. typecheck + lint sin errores; tests de `DataTable` y de mapeo estado→tone; capturas claro/oscuro y 375/768/1440.

## Descomposición sugerida en HUs (FRONTEND)
| HU | Alcance | Pts (Fib) |
|---|---|---|
| HU‑A | Sistema de badges: paleta en `globals.css` + `StatusBadge tone` + helpers + migración de todos los usos (elimina duplicados) | 5 |
| HU‑B | `DataTable` compartido + tests | 3 |
| HU‑C | Migrar Trámites, RBAC y OT‑Usuarios a `DataTable` | 5 |
| HU‑D | (Opcional) Migrar Validaciones/Auditoría + limpieza de paginaciones locales | 2 |

**Total núcleo:** ~13 pts (A+B+C). Con capturas claro/oscuro en cada PR como evidencia.
