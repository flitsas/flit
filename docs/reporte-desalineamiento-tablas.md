# Reporte — Por qué las tablas de Trámites, OT y RBAC no se alinean al diseño

## Resumen ejecutivo

No existe un **componente de tabla compartido** en el frontend. Cada superficie construye su tabla "a mano" (cabecera + filas + paginación), y con el tiempo aparecieron **tres familias visuales distintas**. Las tablas que "se ven bien" siguen un patrón canónico (**card-list**: `<table>` con cabecera-píldora gris y filas-tarjeta blancas separadas). Trámites, RBAC y la tabla de Usuarios de OT se construyeron con paradigmas diferentes (grid CSS, filas con divisor, cabecera distinta), por eso desentonan.

**Causa raíz:** ausencia de un primitivo `DataTable`/`TableShell` reutilizable + un **split histórico** entre el área admin (construida con `<table>` card-list, HU #10194) y los módulos de la SPA "atom" + operación (construidos con `grid` CSS de forma independiente).

---

## 1. El patrón canónico ("alineado")

Referencia: `components/admin/companies/CompanyListTable.tsx`.

| Elemento | Definición canónica |
|---|---|
| **Estructura** | `<table className="w-full border-separate border-spacing-y-2 text-xs">` (tabla semántica) |
| **Cabecera** | Píldora gris: cada `<th>` con `background:#DFE5ED`, `px-4 py-2.5`, `text-[10px] font-semibold uppercase`, color `#162744`, y `rounded-l-xl` / `rounded-r-xl` **en los extremos** |
| **Fila** | Tarjeta blanca: `<tr className="bg-white dark:bg-[#0B0F14]">`, celdas con `border-y` (+`border-l`/`border-r` en extremos), `px-4 py-3`, esquinas `rounded-l/r-xl`. Separadas por el gap de `border-spacing-y-2` |
| **Componentes** | `StatusBadge`, `RowActions`, `Pagination` compartidos (`@/components/atom/`) |

Lo siguen (alineadas): `CompanyListTable`, `DocumentTypeListTable`, y **la mayoría de tablas de OT**: `ClientProceduresTable`, `RulesSection`, `WebhooksSection`, `MandatariosSection` (idéntico patrón, migrado a tokens `bg-card`/`bg-muted`/`text-foreground`).

---

## 2. Las tres divergentes — qué rompe la alineación

### 2.1 Trámites — `components/operacion/TramitesTable.tsx` (la más alejada)

| # | Divergencia | Evidencia | Canónico |
|---|---|---|---|
| 1 | **Grid CSS, no `<table>`** — `<div grid gridTemplateColumns>` + `<ul space-y-2>` | `:581-615` | `<table>` semántica |
| 2 | **Cabecera `rounded-xl` completa** (banda redondeada entera) | `:582` | píldora `rounded-l/r-xl` por extremos |
| 3 | Texto de cabecera `text-[11px] tracking-wider` | `:582` | `text-[10px]` |
| 4 | Filas con **`shadow-[…]` + hover-ring y SIN `border`** (estética "flotante") | `TramiteRow :722` | tarjeta con `border-y/l/r` |
| 5 | **`Pagination` reimplementada localmente** | `:633-686` | `@/components/atom/Pagination` |
| 6 | Dark mode `dark:bg-[#162744]` en filas | `:722` | `dark:bg-[#0B0F14]` |

### 2.2 RBAC — `components/atom/modules/RbacAdmin.tsx`

| # | Divergencia | Evidencia | Canónico |
|---|---|---|---|
| 1 | **Grid CSS, no `<table>`** | módulos `:154`, roles `:433` | `<table>` |
| 2 | **Filas con `border-b` dentro de un contenedor único** `rounded-2xl border` — divisor, no tarjetas separadas, sin gap ni `rounded` por fila | módulos `:188`, roles `:460` | tarjetas separadas por `border-spacing-y-2` |
| 3 | **Cabecera plana sin píldora** (fondo `#DFE5ED` pero sin `rounded-l/r-xl`; el redondeo es del contenedor) | `:154`, `:433` | píldora por extremos |
| 4 | **Inconsistencia interna**: la tabla de Módulos usa badge inline sólido; la de Roles usa `StatusBadge` | módulos `:211-216` vs roles `:472` | `StatusBadge` en ambas |

### 2.3 OT — Usuarios — `components/admin/transit-offices/OtUsersSection.tsx`

| # | Divergencia | Evidencia | Canónico |
|---|---|---|---|
| 1 | **`<table>` real pero SIN `border-spacing` y con filas `border-b`** (divisor dentro de un `rounded-xl border` único), no tarjetas | `:304`, `:322` | filas-tarjeta separadas |
| 2 | **Cabecera distinta**: `<th>` color **`#557EFF` (azul)**, `text-xs`, **sin uppercase**; fondo `bg-muted` sin píldora | `:304+` | `#162744`, `text-[10px]`, uppercase, píldora `#DFE5ED` |
| 3 | **Badge local `OtUserStatusBadge`** (chip propio), no `StatusBadge` ni el `OtStatusBadge` que usan las demás tablas OT | `:529-567` | `StatusBadge` compartido |

> **Matiz importante sobre "OT":** de todas las tablas del hub de Organismos de Tránsito, **la única desalineada es la de Usuarios** (`OtUsersSection`). Las demás (Client-Procedures, Reglas, Webhooks, Mandatarios) **sí siguen el patrón canónico** (con tokens). Si el desalineamiento que notaste es en otra pantalla OT (p. ej. el listado principal `TransitOfficesList`), indícamelo y la reviso puntualmente.

---

## 3. Causa raíz (por qué pasó)

1. **No hay un componente `DataTable`/`TableShell` compartido.** Sí se comparten piezas sueltas (`StatusBadge`, `RowActions`, `Pagination`), pero **la "cáscara" de la tabla (cabecera + filas + paginación) se reescribe en cada pantalla** → deriva inevitable.
2. **Dos linajes de construcción:**
   - *Área admin* (`components/admin/**`): nació con el patrón `<table>` card-list (HU #10194) → alineada.
   - *SPA "atom" + operación* (`components/atom/modules/**`, `components/operacion/**`): se construyó con **`grid` CSS** de forma independiente (Trámites, RBAC, Usuarios, Validaciones, Auditoría) → otro paradigma.
   - `OtUsersSection` es un **tercer híbrido** (tabla real pero estilo divisor + cabecera azul), construido con un criterio propio.
3. **Filas divisor vs filas-tarjeta:** RBAC y OT-Usuarios usan `border-b` (lista con líneas); el canónico usa tarjetas separadas → la diferencia visual más evidente.
4. **Cabecera sin estandarizar:** unos redondean la banda entera (Trámites, Validaciones, Auditoría con `rounded-t-xl`), otros no la redondean (RBAC), y OT-Usuarios además le cambia color/tamaño/caso. Ninguno replica la píldora `rounded-l-xl`+`rounded-r-xl` del canónico.
5. **Paginación duplicada:** Trámites y Validaciones reimplementan `Pagination` localmente; RBAC y OT-Usuarios no paginan; solo Auditoría y las tablas OT `<table>` usan la compartida.
6. **Hex vs tokens:** las tablas OT `<table>` migraron a tokens (`bg-card`/`bg-muted`/`text-foreground`); las grid de la SPA y el canónico siguen con hex hardcodeado (`#DFE5ED`/`#162744`/`#0B0F14`).

### Comparativa rápida

| Tabla | Impl. | Filas | Cabecera | Paginación | Veredicto |
|---|---|---|---|---|---|
| CompanyListTable (canónica) | `<table>` | tarjeta | píldora | compartida | ✅ alineada |
| ClientProcedures / Rules / Webhooks / Mandatarios (OT) | `<table>` | tarjeta | píldora (tokens) | compartida/—  | ✅ alineada |
| Auditoría | grid | tarjeta | banda `rounded-t` | compartida | 🟡 casi (grid) |
| Validaciones | grid | tarjeta | banda `rounded-t` | **local** | 🟡 parcial |
| **Trámites** | **grid** | **flotante (shadow, sin border)** | **`rounded-xl` completa** | **local** | ❌ divergente |
| **RBAC** | **grid** | **divisor `border-b`** | **plana** | — | ❌ divergente |
| **OT-Usuarios** | `<table>` | **divisor `border-b`** | **azul `#557EFF`, no uppercase** | — | ❌ divergente |

---

## 4. Recomendación (para alinear)

1. **Crear un primitivo compartido** `components/atom/DataTable.tsx` (o `TableShell`) que encapsule: cabecera-píldora (`#DFE5ED`/`bg-muted`, `rounded-l/r-xl`, `text-[10px] uppercase`), filas-tarjeta (`bg-card`/`bg-white`, `border-y/l/r`, `rounded-l/r-xl`, `border-spacing-y-2`), y la `Pagination` compartida. API por columnas (`{ key, header, render, align, width }`).
2. **Migrar las 3 divergentes** a ese primitivo — o, como mínimo (menor esfuerzo, sin refactor estructural):
   - **RBAC** y **OT-Usuarios:** cambiar filas `border-b` → tarjetas separadas y cabecera a píldora estándar; OT-Usuarios además normalizar el color de cabecera a `#162744`/`text-[10px] uppercase`.
   - **Trámites:** cabecera a píldora por extremos, filas a `border` en vez de `shadow+ring`, y reemplazar la `Pagination` local por la compartida.
3. **Unificar el badge de estado:** consolidar `StatusBadge` / `OtStatusBadge` / `OtUserStatusBadge` / badges inline en un único componente (translúcido con borde) para que el estado se vea igual en toda la app.
4. **Adoptar tokens** (`bg-card`/`bg-muted`/`text-foreground`/`border`) en las tablas grid de la SPA, como ya hicieron las tablas OT.

> Este reporte es diagnóstico. La corrección (crear `DataTable` + migrar) sería una HU aparte; encaja como continuación natural de la HU #10844 (UI transversal), pero excede su alcance actual (responsive + íconos).
