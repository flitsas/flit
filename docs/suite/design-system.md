# FLIT Suite — Design System

| Campo | Valor |
|---|---|
| **Estado** | **Propuesto** — aprobación pendiente de humano (PO / Líder Técnico) |
| **Autor** | `ui-agent` (modo A) |
| **Fecha** | 2026-11-12 |
| **Reemplaza a** | `flit-design-guardian` + prototipo PDF v4 (solo cuando este documento pase a **Aprobado**) |
| **Mientras tanto** | El PDF v4 / `flit_design_tokens.json` **siguen rigiendo**. Ninguna pantalla mezcla ambas fuentes. |
| **Alcance** | `packages/ui`, `packages/shell`, `packages/brand` y los productos `frontend-hub`, `frontend-comparendos` (y los que vengan) |

> Este documento **no modifica código ni tokens**. Es la propuesta; los deltas de la §4 son la lista de cambios a ejecutar (modo D / frontend-agent) tras la aprobación.

---

## 0. Cómo se levantó este documento (hechos verificados)

**Fuente de verdad real hoy: el código**, no el JSON heredado. Hallazgos que condicionan la propuesta:

1. **Los tokens viven en `packages/ui/src/styles/tokens.css`** (Tailwind v4 `@theme inline` + `:root` + `.dark`). Dos capas conviven: variables shadcn (`--background`, `--primary`, `--border`…) y la capa «Diseño Inteligente» `--di-*` (HU #14474). Los componentes nuevos (`Button`, shell) consumen `--di-*`; los viejos, Tailwind semántico (`bg-secondary`, `border`).
2. **`tokens.ts` (`FLIT`) es un espejo parcial y ya divergió del CSS**: `danger #C10000` y `warning #FF4E00` frente a `--di-danger #c23c00`; `textos` y `border.input #74839F` coinciden, pero faltan `--di-*`, estados de trámite y sombras.
3. **El JSON heredado (`flit_design_tokens.json` v2.1.0) contradice al código** en puntos clave: cabecera de tabla `#DFE5ED` (código: `#162744`, HU #14477); `border.input #DFE5ED` (código: `#74839F`); radio base 10 px (código: `0.75rem` = 12 px); CTA degradado `#557EFF→#00DBD5` (código: `--gradient-action` `#3D5FD9→#0F6FA8`); botón de consulta sólido `#557EFF` (no existe en `Button`); badges de éxito verde tintado (código: tinta azul `#e4edfb`).
4. **Los estados semánticos de badge se colapsaron**: en claro `--badge-success`, `--badge-warning`, `--badge-pending` y `--badge-neutral` usan el **mismo** fondo `#e4edfb` (solo cambia el `fg` de neutral). Éxito, advertencia y pendiente **no se distinguen por color**; hoy dependen de icono/texto. Esto tensiona la regla 5 («estados semánticos consistentes») y la lectura rápida en tablas. Además el comentario de `--flit-success-ink` dice `#4F7A12` pero el valor es `#162744`.
5. **Dos tintas de estado conviven**: `--badge-*` (5–7 tonos genéricos) y `--estado-*` (11 estados del trámite RNA con `bg/fg/border/acento` y variante oscura). El segundo es el más completo y accesible; el primero es el que se degradó.
6. **Theming ya está resuelto a medias y bien**: Marca Blanca vía `--brand-primary|secondary|accent|bg|tint|ink` inyectados por `BrandStyle` (`packages/brand`), con fallback al hex FLIT; tema claro/oscuro por clase `dark` en `<html>` (`useSuiteTheme`, `THEME_INIT_SCRIPT`, clave `flit-theme`, `DARK_MODE_ENABLED = true`).
7. **Hub y Comparendos consumen idéntico**: `globals.css` es byte-a-byte equivalente (importa `tokens.css`, `cursor.css`, `shell.css`, 3 `@source`), mismos `layout.tsx` (Poppins 400–700 + Barlow Condensed 700 locales, `BrandStyle`, script de tema). Sin hex sueltos en esos archivos. **No hay hoy mecanismo de acento por producto**: la diferencia entre productos es solo el nombre en la barra (`.suite-barra__producto`).
8. **Disciplina de hex en `packages/ui/src`**: 0 hex en estilos activos (los 5 hallazgos son comentarios). En `shell.css` quedan literales: `#ffffff` (píldora activa del dock, fab pastilla) y `#0b182d` (avatar en oscuro).
9. **Pisos incumplidos frente al heredado**: `shell.css` usa `0.625rem` (10 px) en `.suite-menu-productos__pastilla` (piso heredado: 12 px); `UiStateBoundary` pinta «Reintentar» con `<button>` propio (`text-xs`, `py-2` ≈ 32 px) en vez de `Button` y por debajo del objetivo táctil de 44 px que sí cumple `Button md`.
10. **Documentación**: `packages/ui/README.md` y `packages/shell/README.md` **no existen**. No hay catálogo ni guía de uso; el conocimiento está en comentarios de HU dentro del CSS/TSX.

---

## 1. Principios

1. **Claridad operativa antes que ornamento.** Los usuarios radican, consultan y corrigen trámites/comparendos de alto volumen: jerarquía, legibilidad y velocidad > decoración.
2. **Un patrón, una solución en toda la suite.** La diferencia entre productos va por **tema** (tokens), nunca por copia de componente.
3. **Tokens o nada.** Todo color, radio, sombra, tipografía y espaciado sale de un token; cero HEX/RGB en componentes.
4. **Estado nunca solo por color.** Color + icono + texto, siempre.
5. **Cuatro estados siempre**: vacío, cargando, error, lleno (§7).
6. **Accesible por defecto**: WCAG 2.1 AA, teclado completo, foco visible, objetivos ≥ 44 px (36 px solo en barras densas).
7. **Marca blanca y oscuro son ciudadanos de primera**: un componente no existe hasta que funciona con `--brand-*` y `.dark`.
8. **Una sola fuente de verdad.** El CSS de `packages/ui` es el dato; este documento es la norma; `tokens.ts` y el JSON se **derivan** (o se eliminan), no se mantienen a mano.
9. **Datos personales solo como placeholder** en ejemplos y capturas (Ley 1581).

---

## 2. Dirección visual — alternativas

### Alternativa A — «Diseño Inteligente» consolidado (evolución)
Conserva lo que ya existe y se ve en producción: píldoras, degradado de acción azul→petróleo, cabecera de tabla tinta, tarjetas con sombra suave, dock flotante, ilustraciones, Poppins. Se **sanean** incoherencias (tonos colapsados, divergencias de token, pisos de tamaño) y se completa el sistema (acento por producto, escalas, catálogo).

| + | − |
|---|---|
| Menor costo y riesgo; ya es lo que consumen hub y comparendos | Hereda decisiones de HU puntuales (HU #14474–#14482) con deuda de coherencia |
| Marca blanca y oscuro ya funcionan | Estética «amigable» puede quedar lúdica en pantallas muy densas |
| Migración incremental, sin ruptura visual | No responde a un eventual deseo de rediseño profundo |

### Alternativa B — «Consola operativa» (sobria y densa)
Reduce protagonismo de degradados e ilustración; superficies planas, bordes finos, tabla compacta, radios menores (8–12 px), botones rectangulares redondeados (no píldora) para acciones primarias; el color se reserva para estado y acción.

| + | − |
|---|---|
| Mejor densidad y escaneo en listados/bandejas largas | Cambio visual notorio: toca `Button`, dock, tarjetas, modales |
| Estados semánticos destacan más (menos ruido) | Pierde carácter de marca del Diseño Inteligente ya validado con el negocio |
| Menos coste de mantenimiento (menos efectos) | Dock/píldoras (patrón de navegación) habría que repensarlos |

### Alternativa C — «Suite multi-identidad» (un hue por producto)
Base común + **acento propio por producto** (Hub, Trámites, Comparendos…), con barra, dock activo y degradado de acción teñidos por producto. Reutiliza el mecanismo `--brand-*` a otro nivel (`data-product`).

| + | − |
|---|---|
| Orientación inmediata: la persona sabe en qué producto está | Riesgo de incoherencia y de combinatoria con Marca Blanca (¿gana la red o el producto?) |
| Escala bien al agregar productos | Multiplica verificación de contraste (producto × tema × marca) |
| Refuerza la idea de «suite» | Requiere decisión de marca/PO sobre paleta por producto |

### Recomendación
**A como base, con dos injertos:** (1) de **B**, la *densidad* — escala de espaciado/tabla «compacta» y reglas de ornamento mínimo en pantallas operativas, sin tocar la píldora ni el dock; (2) de **C**, un **acento de producto limitado** (solo indicador en barra y chip de producto), **desactivado cuando hay Marca Blanca**. Razón: máximo retorno con mínimo riesgo; corrige lo roto (§0.4, 0.9) antes de añadir variedad. *Si el PO quiere un rediseño profundo, la alternativa B es la candidata y exige rehacer §3–§5 antes de aprobar.*

> El resto del documento describe la **recomendación (A + injertos)**.

---

## 3. Tokens propuestos

Leyenda: ✅ se conserva · ✏️ cambia · ➕ falta/nuevo · 🗑️ se retira.

### 3.1 Arquitectura de capas (➕ decisión de estructura)

| Capa | Prefijo | Rol | Quién la consume |
|---|---|---|---|
| 1. Primitivos | `--flit-*` (paleta cruda) | Valores base, **no** usables en componentes | Solo `tokens.css` |
| 2. Semánticos de suite | `--fs-*` (propuesto) | Significado: `text`, `surface`, `border`, `action`, `success`, `warning`, `danger`, `info` | Componentes `packages/ui` y `shell` |
| 3. Puente Tailwind | `--color-*` | `@theme inline`, apunta a la capa 2 | Productos |
| 4. Marca/tema | `--brand-*`, `.dark`, `data-product` | Sobrescriben capa 2 por red/tema/producto | `BrandStyle`, `useSuiteTheme` |

Hoy `--di-*` hace el papel de capa 2 y `--color-*`/shadcn el de capa 3, mezclados. **Propuesta:** mantener `--di-*` como capa 2 en esta etapa (renombrar a `--fs-*` es opcional y solo si se aprueba, por costo de churn), y **congelar** las variables shadcn sin uso real.

### 3.2 Color — marca y texto

| Token (actual) | Valor claro | Valor oscuro | Decisión |
|---|---|---|---|
| `--di-text-primary` | `#162744` | `#f3f6ff` | ✅ |
| `--di-text-secondary` | `#4a5878` | `#c3cde6` | ✅ |
| `--di-text-tertiary` | `#5d6b86` | `#97a6c8` | ✏️ verificar ≥ 4.5:1 sobre `--di-surface` y `--di-surface-sunken`; si no cumple, oscurecer |
| `--di-action` / `--flit-brand-ink` | `#3d5fd9` (5.47:1 s/blanco, dato del código) | `#8fb0ff` | ✅ único azul permitido para texto/enlace |
| `--color-flit-brand` | `#557eff` (3.61:1 s/blanco) | — | ✅ solo relleno, icono, borde, decor (nunca texto pequeño) |
| `--color-flit-tech` / `--di-highlight-decor` | `#00dbd5` | `#00dbd5` | ✅ decorativo; nunca texto |
| `--di-accent` (tinta) | `var(--brand-primary, #162744)` | `#b9cdff` | ✅ |
| `--gradient-action` | `#3d5fd9→#0f6fa8` | igual | ✅ (el JSON `#557EFF→#00DBD5` queda 🗑️ por no cumplir AA con texto blanco) |
| `--di-gradient-action-hover` | `#304fc4→#0a5c8c` | — | ➕ definir variante oscura explícita |
| `--di-on-action` | `#ffffff` | `#ffffff` | ✅ (comentario del código: texto blanco fijo en ambos temas) |

### 3.3 Superficies, bordes, foco

| Token | Claro | Oscuro | Decisión |
|---|---|---|---|
| `--di-surface` (`--color-flit-bg`) | `#eef5ff` | `#0b182d` | ✅ |
| `--di-surface-elevated` | `#ffffff` | `#132544` | ✅ |
| `--di-surface-sunken` / `--surface-sunken` | `#e4edfb` | `#0f1f3a` | ✅ **duplicado**: consolidar en uno |
| `--di-border` | `#dfe5ed` | `#22375c` | ✅ separadores de contenedor |
| `--di-border-strong` / `--border-strong` | `#c9d3e3` | `#2e4673` | ✅ **duplicado**: consolidar |
| `--di-border-control` | `#74839f` | `#6b7ca6` | ✅ bordes de input (cumple 3:1 de componente UI) |
| `--di-divider` | `#e9eef6` | `#1a2c4d` | ✅ |
| `--ring` / foco | `#3d5fd9` (`--ring` `#3d5fd9`) vs `--di-action` | `#8fb0ff` | ✏️ **unificar**: foco = `--di-action`, 2 px + offset 2 px, en todo interactivo |
| `--overlay` | `rgba(22,39,68,.45)` | `rgba(4,9,20,.7)` | ✅ |

### 3.4 Estados semánticos genéricos (✏️ prioridad alta — corrige §0.4)

Hoy colapsados. **Propuesta**: 5 tonos realmente distintos, forma tintada (bg + fg + borde) **y siempre icono/texto**:

| Tono | Uso | Propuesta claro (bg / fg / borde) | Nota |
|---|---|---|---|
| `success` | Válido, completo, activo | verde tintado `~#F3FBE8 / #4F7A12 / #CDEB9C` | `#4F7A12` = 5.09:1 s/blanco (dato del código); alinear `--flit-success-ink` |
| `warning` | Atención, expira, parcial | ámbar tintado `rgba(249,172,0,.15) / #B45309 / rgba(249,172,0,.40)` | ➕ distinto de `pending` |
| `danger` | Error, rechazo, destructivo | naranja-rojo `rgba(255,78,0,.12) / #C2410C|#c23c00 / rgba(255,78,0,.32)` | decidir un solo `fg`; hoy hay `#c23c00`, `#C10000` y `#C2410C` |
| `info` | Informativo, programado, en curso | azul `#3d5fd91a / #3d5fd9 / #3d5fd91a` | ✅ |
| `neutral` | Inactivo, borrador | `#e4edfb / #4a5878 / #e4edfb` | ✅ |
| `pending` | «Pendiente por acción» | **fusionar con `warning`** o dar color propio | decisión abierta (§10) |

Valores oscuros: derivar y verificar 4.5:1 sobre `--di-surface-elevated`. Retirar 🗑️ `badge-purple` y `badge-tinta` si no tienen uso (auditar).

### 3.5 Estados de trámite (`--estado-*`)
✅ **Se conservan íntegros** (11 estados × `bg/fg/border/acento`, claro y oscuro; ya con contraste calculado). Cambio: ➕ documentarlos como **segunda capa semántica de dominio RNA**, no genérica; los productos que no sean RNA (p. ej. comparendos) definen su propia familia con el mismo contrato de 4 variables en vez de reutilizar badges genéricos. ✏️ `rechazado` usa rojo puro `#ff0000` como acento: revisar frente a `danger` para no tener dos rojos.

### 3.6 Tipografía

| Token | Valor | Decisión |
|---|---|---|
| `--font-sans` | Poppins (400/500/600/700, local) | ✅ |
| `--font-mono` | JetBrains Mono | ➕ el CSS la declara pero los layouts **no la cargan**: cargar o retirar |
| `--font-placa` | Barlow Condensed 700 | ✅ uso exclusivo `Placa` |
| Escala | Tailwind por defecto sin tokens propios | ➕ definir escala semántica: `display 28–36`, `h1 24`, `h2 18–20`, `h3 16`, `body 14–16`, `caption 12`, `cifra 1.75rem` (ya existe `--di-text-cifra`) |
| Piso de tamaño | heredado 12 px | ✅ **12 px absoluto**; ✏️ corregir `0.625rem` en `.suite-menu-productos__pastilla` |
| Pesos | 400/500/600/700 | ✅ (el código usa también `650`: ✏️ Poppins local no trae 650, normalizar a 600/700) |

### 3.7 Radios, sombras, espaciado, movimiento

| Grupo | Actual | Decisión |
|---|---|---|
| Radios | `--radius .75rem`; escala `sm..4xl`; `--di-radius-chip 10`, `-modal 24`, `-header 28`, pill 999 | ✅ conservar; ➕ documentar uso: control 12, tarjeta 16–20, modal/menú 24, píldora (botón, chip, buscador) |
| Sombras | `--shadow-card`, `-card-hover`, `-modal`, `-button`, `--nav-sombra-*` | ✏️ `--nav-sombra-panel` y `--shadow-modal` casi idénticas: consolidar |
| Espaciado | Tailwind 4 px; sin tokens | ➕ escala de densidad: `compact / default / comfortable` (injerto B) como variable `--fs-density` que afecta alto de fila y padding de tarjeta |
| Movimiento | `--di-motion-base 180ms`, `--di-ease`, `--nav-duracion` | ✅ consolidar `--nav-*` en `--di-motion-*`; reduced-motion ya respetado en shell e ilustración |
| Z-index | literales (`z-30`, `z-50`) | ➕ escala: `dock 20 · menú 30 · overlay 40 · modal 50 · toast 60` |

### 3.8 Fuentes de verdad derivadas
- ✏️ `tokens.ts` (`FLIT`): **generarlo desde `tokens.css`** o reducirlo a lo que JS realmente necesita (gráficas, canvas, PDFs). Hoy es riesgo de divergencia.
- ✏️ `flit_design_tokens.json`: al aprobar, regenerar desde el CSS o reemplazar; hoy contradice al código (§0.3).

---

## 4. Deltas de tokens (resumen ejecutable tras aprobación)

| # | Delta | Archivo | Riesgo |
|---|---|---|---|
| T1 | Restaurar 5 tonos distintos de badge (claro/oscuro) y corregir `--flit-success-ink` | `tokens.css` | Medio: cambia píxeles de toda tabla |
| T2 | Consolidar duplicados (`surface-sunken`, `border-strong`, sombras de panel) | `tokens.css` | Bajo |
| T3 | Unificar foco en `--di-action` | `tokens.css`, `shell.css` | Bajo |
| T4 | Sustituir literales `#ffffff`/`#0b182d` en shell por tokens (`--di-on-action`, `--di-on-accent`) | `shell.css` | Bajo |
| T5 | Elevar `0.625rem` a 12 px | `shell.css` | Bajo |
| T6 | Escala tipográfica, z-index, densidad | `tokens.css` | Bajo (aditivo) |
| T7 | Cargar o retirar JetBrains Mono; normalizar peso 650 | layouts, CSS | Bajo |
| T8 | `data-product` + `--product-accent` (opt-in) | `tokens.css`, shell | Medio (§8) |
| T9 | Regenerar `tokens.ts` y JSON desde CSS | `tokens.ts`, skill | Bajo |

---

## 5. Inventario de componentes compartidos

### 5.1 `packages/ui` (exporta `index.ts`)

| Componente | Estado | Observación |
|---|---|---|
| `Button` (`primary/secondary/navy/ghost`; `sm 36 / md 44`) | ✅ referente | Falta variante **`danger`** y **`consult` (sólido)** si el negocio mantiene la distinción CTA/consulta del JSON; falta estado `loading` (hoy solo `aria-busy:cursor-progress`) |
| `DataTable`, `table-styles`, `Pagination`, `usePaginacion` | ✅ | Cabecera tinta (HU #14477); falta variante compacta y selección de filas |
| `StatusBadge` + `statusTones` | ✏️ | `statusTones.ts` contiene mapas **de dominio** (banner, tipo de trámite) en un paquete genérico → mover a cada producto |
| `InlineAlert` | ✅ | Revisar que `warning/pending` usen tonos de §3.4 |
| `Modal` | ✅ | Verificar trampa de foco, `Esc`, retorno de foco (auditoría pendiente) |
| `UiStateBoundary` | ✏️ | Cubre 4 estados; usa `<button>` propio (32 px) → usar `Button`; skeleton genérico de filas |
| `Loader` (+ `loader.css`) | ✅ | Unificar con skeleton y `aria-busy` |
| `SearchableSelect`, `ActionsMenu`, `ChipsFiltro`, `PageNav`, `CabeceraModulo`, `TarjetaMetrica`, `Placa`, `Ingreso` | ✅ | `Ingreso` y `Placa` son de dominio/pantalla específica |
| `ilustraciones`, `cn`, `cursor.css` | ✅ | — |

### 5.2 `packages/shell`
`SuiteShell`, `Dock`/`DockMobile`/`DockGlyph`, `ProductMenu`, `AccountMenu`, `CommandPalette` (Ctrl K), `ThemeToggle`, `ShellNavContext`, `nav.ts`, `search.ts`. ✅ Estilos con prefijos `suite-*`, `dock-*`. ✏️ `.suite-menu-productos__*` y `.suite-menu-usuario__*` duplican estructura: extraer **`MenuFlotante`** compartido.

### 5.3 `packages/brand`
`BrandStyle`, `BrandProvider`, `BrandLogo`, `derive-tokens`, `derive-dark`, `contrast`, `hosts`. ✅ Ya existe cálculo de contraste: **reutilizarlo** como puerta de calidad del tema (§9).

### 5.4 Huecos (componentes que faltan)

| Prioridad | Componente | Motivo |
|---|---|---|
| Alta | **Campos de formulario** (`Input`, `Select`, `Textarea`, `Checkbox`, `Radio`, `Switch`, `Field` con etiqueta/ayuda/error) | No existen en el paquete; cada producto los resuelve a mano (riesgo de inconsistencia y a11y) |
| Alta | **Toast / notificación** | Sin patrón de confirmación no bloqueante |
| Alta | **`EmptyState` y `ErrorState` independientes** | Hoy embebidos en `UiStateBoundary` |
| Alta | **`Skeleton`** (fila, tarjeta, métrica) | Hoy `animate-pulse` ad hoc |
| Media | `Tabs`, `Tooltip`, `Popover`, `Drawer/Sheet`, `ConfirmDialog` (destructivo) | Patrones repetidos |
| Media | `Stepper` / wizard | Wizard de trámite referenciado en el JSON; no compartido |
| Media | `DatePicker`, `FileUpload` (con estados) | Comparendos y trámites los necesitan |
| Baja | `Breadcrumb`, `Avatar`, `Tag removible` | — |
| Docs | **`packages/ui/README.md` y `packages/shell/README.md`** | Inexistentes |

---

## 6. Patrones de pantalla

1. **Shell de producto**: barra superior (logo/marca, nombre de producto, buscador Ctrl K, tema, productos, usuario) + dock inferior flotante (navegación principal) + contenido sobre `--di-surface`. Móvil: `DockMobile`, buscador colapsa a icono (≤ 640 px).
2. **Listado / bandeja**: `CabeceraModulo` → fila de filtros (`ChipsFiltro` + búsqueda) → `TarjetaMetrica`s opcionales → `DataTable` → `Pagination`. Acción primaria única arriba a la derecha.
3. **Detalle (modal o página)**: cabecera con identificador y estado (`--estado-*`), stepper/tracking, secciones en tarjetas, acciones al pie. (Especificación detallada en skill `flit-detalle-tramite`; se reconcilia al aprobar.)
4. **Formulario / wizard**: una columna de campos, etiquetas visibles, errores en línea junto al campo + resumen arriba, CTA «Continuar» primario y «Anterior» secundario.
5. **Ingreso / identidad**: patrón `Ingreso` (panel de marca tinta + tarjeta); único lugar donde hay escena ilustrada grande.
6. **Confirmación destructiva**: `ConfirmDialog` con acción `danger` y foco inicial en «Cancelar».
7. **Jerarquía de acciones**: 1 primaria (degradado) · secundarias (contorno) · terciarias (ghost). Nunca dos primarias en la misma vista.

---

## 7. Los 4 estados de UI (contrato obligatorio)

| Estado | Contenedor | Contenido mínimo | A11y |
|---|---|---|---|
| **Cargando** | Skeleton con la **forma** del contenido (≥ 400 ms de retardo antes de mostrarlo para evitar parpadeo) | Sin spinner a pantalla completa en listados | `role="status"`, `aria-busy`, texto SR «Cargando…» |
| **Vacío** | Tarjeta con icono/ilustración, mensaje accionable y CTA contextual | Distinguir «sin datos aún» de «sin resultados del filtro» (con «Limpiar filtros») | Texto, no solo imagen |
| **Error** | Tarjeta con mensaje humano, causa si es útil, **Reintentar** (`Button`) | Error de red ≠ error de permiso (403) ≠ error de validación | `role="alert"`; foco al resumen de error en formularios |
| **Lleno** | Contenido real | Incluir variante **parcial/larga** (paginado, truncado con tooltip, desbordes) | Orden de foco = orden visual |

`UiStateBoundary` es el wrapper canónico; se extiende (§5.4) para aceptar `skeleton` por componente y `error` con tipo.

---

## 8. Estrategia de theming por producto

Orden de precedencia (de menor a mayor): **FLIT por defecto → tema claro/oscuro → acento de producto (opt-in) → Marca Blanca (`--brand-*`)**.

- **Tema claro/oscuro**: clase `dark` en `<html>`, preferencia `flit-theme`. ✅ se conserva. Requisito: todo token nuevo define su valor oscuro **en el mismo PR**.
- **Marca Blanca**: ✅ se conserva (`BrandStyle` deriva `--brand-ink/accent/bg/tint`). Regla: **la red gana al producto**; el acento de producto se desactiva si hay marca de red.
- **Acento de producto (➕, injerto C)**: atributo `data-product="hub|tramites|comparendos"` en `<html>` que fija solo `--product-accent` (indicador de producto en barra, pastilla «aquí» del `ProductMenu`, borde superior de tarjetas de métrica). **No** altera `--di-action` ni el degradado de acción, para no multiplicar combinaciones de contraste.
- **Qué NO varía por producto**: texto, superficies, estados semánticos, componentes, tipografía.
- **Cómo se consume**: cada app solo importa `@flit/ui/tokens.css`, `cursor.css`, `@flit/shell/shell.css` y declara `@source` (ya es así en hub y comparendos); la única línea propia por producto es `data-product`. Un producto nuevo copia ese `globals.css` y `layout.tsx` tal cual.

---

## 9. Accesibilidad (WCAG 2.1 AA) — requisitos del sistema

| Tema | Requisito |
|---|---|
| Contraste | Texto normal ≥ 4.5:1; texto grande y componentes UI/iconos informativos ≥ 3:1; verificado en **claro, oscuro y Marca Blanca**. Reutilizar `packages/brand/contrast.ts` como prueba automática de los pares de tokens |
| Color | Estado = color **+** icono/texto; sin información solo por tono |
| Foco | `:focus-visible` 2 px `--di-action` + offset 2 px, visible sobre cualquier superficie; prohibido `outline: none` sin sustituto |
| Teclado | Todo operable; menús y dock con flechas/`Esc`; `CommandPalette` con trampa de foco y retorno |
| Objetivos | ≥ 44 × 44 px (excepción 36 px en barras densas con separación) |
| Texto | Piso 12 px; reflow a 320 px; zoom 200 % sin pérdida |
| Nombres | Iconos sin texto con `aria-label`; `aria-pressed/expanded/current` ya usados en shell ✅ |
| Movimiento | `prefers-reduced-motion` respetado (ya en shell e ilustraciones; extender a skeletons y toasts) |
| Formularios | Etiqueta visible, ayuda y error asociados (`aria-describedby`), `aria-invalid` |
| Idioma | `lang="es"` ✅ |

---

## 10. Decisiones abiertas (requieren humano)

1. **Dirección visual**: ¿A + injertos (recomendada), B o C?
2. **Origen del «nuevo diseño»**: ¿existe prototipo/Figma/marca nueva que deba alimentar esta propuesta? Sin él, este documento solo saneó y completó lo existente.
3. **Tonos semánticos**: ¿`pending` se fusiona con `warning` o conserva color propio? ¿Un solo rojo-naranja para `danger` (`#c23c00` vs `#C2410C` vs `#C10000`)?
4. **`#2E3192`** (azul propuesto en PDF 20-ago): confirmar que sigue **descartado** como marca.
5. **CTA vs Consulta**: ¿se mantiene la distinción «Consultar» (sólido) vs CTA (degradado) del PDF 20-ago? Hoy no existe en `Button`.
6. **Acento por producto**: ¿se aprueba (injerto C)? ¿Quién define la paleta por producto? ¿La red (Marca Blanca) siempre gana?
7. **Densidad**: ¿se acepta la escala `compact/default/comfortable` o se fija una sola densidad?
8. **Renombrar `--di-*` → `--fs-*`**: ¿vale el churn o se conserva el prefijo?
9. **`tokens.ts` y JSON**: ¿generar o eliminar? (afecta skill `flit-design-guardian`).
10. **Estados de trámite no RNA**: comparendos requiere su propia familia de estados; ¿la define el PO de Comparendos?
11. **Peso tipográfico 650 y JetBrains Mono**: normalizar/cargar o retirar.
12. **Aprobador formal** del documento (PO vs Líder Técnico) y fecha.

---

## 11. Plan de migración / adopción

| Fase | Contenido | Responsable | Gate |
|---|---|---|---|
| **0 — Aprobación** | Responder §10; marcar este documento **Aprobado** | PO / Líder Técnico | Humano |
| **1 — Saneamiento de tokens** (T1–T7) | Sin cambios de layout; diff visual acotado | ui-agent (modo D) | Revisión visual + tests de tokens |
| **2 — Documentación** | `README.md` de `ui` y `shell`; catálogo vivo (galería/Storybook o ruta interna) | ui-agent | — |
| **3 — Componentes faltantes** (alta prioridad §5.4) | `Field/Input…`, `Toast`, `EmptyState/ErrorState/Skeleton`, `ConfirmDialog` | ui-agent + frontend-agent | Test de componente + a11y |
| **4 — Acento de producto y densidad** (T8) | Solo si §10.6–7 se aprueban | ui-agent | — |
| **5 — Adopción por producto** | Reemplazar implementaciones ad hoc en hub y comparendos; mover `statusTones` de dominio | frontend-agent | Auditoría modo C por pantalla |
| **6 — Cierre de la herencia** | Regenerar/retirar `flit_design_tokens.json`, `prototype_rules.md`, `acceptance_checklist.md`; actualizar `flit-design-guardian` para apuntar aquí; avisar al orquestador | ui-agent | Aprobación vigente |

**Reglas de transición:** hasta la fase 0 el PDF v4 manda; ninguna pantalla se mezcla; cada fase entra en su propio PR (convenciones FLIT); cada pantalla migrada pasa auditoría modo C con veredicto antes del code-review.

**Checklist de aceptación propuesto (para el modo C tras aprobar):** tokens sin hex · 4 estados presentes · foco visible · contraste verificado en claro/oscuro/marca · objetivos ≥ 44 px · estado no solo por color · componentes reutilizados del paquete · sin placeholders con datos personales.

---

*Documento en estado **Propuesto**. No sustituye al prototipo PDF v4 hasta su aprobación humana.*
