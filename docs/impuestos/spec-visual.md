# Spec visual · Producto Impuestos (frontend-impuestos + piezas en frontend/)

> Generado: 2026-11-24 · `ui-agent` modos B (spec) y C (auditoría de M15) · rama `feature/AB-14694-producto-impuestos` (HEAD 7a8502863)
> Insumo del `frontend-agent` para M16–M19 y del `ui-agent` para auditarlos. HU FE #14695.
> Plan de referencia: `docs/impuestos/plan-implementacion.md` §3 (estados), §5.1 (contrato), §6 (rutas), §9 M15–M19. Decisiones: `.claude/state/producto-impuestos/decisiones-usuario.md` (D-DASH, D-REPORTES, D-CONFIG, D-GESTOR, D-ROL-ADMIN, DA-6b, DA-7, DA-9).

---

## 0. Fuente de diseño y reglas

- **Fuente que rige:** los tokens y componentes de `packages/ui` (`src/styles/tokens.css` y exports de `@flit/ui`), que ya implementan el Manual UI Kit 2026, y la forma en que `frontend-diagnostico` los compone (molde de este producto). Las specs previas de Diagnóstico (`.claude/state/diagnostico-ola-ux-dashboard/spec-ui.md`) siguen la misma fuente. **`docs/suite/design-system.md` no existe en esta rama** (se sacó en el commit 16766b438); el PDF v4 heredado de `flit-design-guardian` no se mezcla con esta fuente en ninguna pantalla de Impuestos.
- **Copiar la forma, no importar el código.** `frontend-impuestos` no importa nada de `frontend-diagnostico` (lo vigila `__tests__/independencia.test.ts`). Cuando el molde usa recetas locales de Diagnóstico (`components/configuracion/estilos.ts`, `components/tablero/vistasDetalle/comun.tsx`), Impuestos las reproduce en `frontend-impuestos/components/comun/estilos.ts` (§0.2) y la duplicación queda reportada como hueco (§5).
- **Tokens o nada.** Cero `#hex` o `rgb()` en clases y en `style`. Solo se permiten `var(--di-*)`, `var(--kit-*)`, `var(--badge-*)`, `var(--chart-1..5)`, `var(--table-*)`, `var(--overlay)`, `var(--estado-*-fg)` y las utilidades `bg-card`, `text-flit-primary`, `border-border`, `text-muted-foreground` y `ring`. Todas tienen valor `.dark`. **Prohibido `--dx-*`**, que es exclusivo de Diagnóstico. Impuestos **no crea tokens propios**: no hace falta ninguno (§1). El único hex que se tolera es `themeColor` en `app/layout.tsx`, porque es metadato del navegador, igual que en Diagnóstico.
- **4 estados obligatorios** (vacío, cargando, error y lleno) en cada pantalla y en cada bloque aislado.
- **WCAG 2.1 AA:** contraste ≥ 4,5:1 (ya lo garantizan los tokens), foco visible (`focus-visible:outline-2 outline-offset-2 outline-ring` o `--di-action`), teclado completo, nombres accesibles y estado nunca solo por color (texto siempre y, en chips, punto o ícono).
- **PII (Ley 1581, DA-7=b):** en pantalla, documento y nombre del propietario van completos donde el contrato los trae. En tests, E2E, capturas y en este documento solo se usan placeholders: `ABC123`, `CC 0000000`, «Propietario Demo», «Compañía Demo», `nombre@empresa.com`. Nunca se pinta dirección, correo ni teléfono del propietario.

### 0.1 Formato y vocabulario (lo usa `lib/liquidaciones/formato.ts`)

| Dato | Regla | Ejemplo |
|---|---|---|
| Monto | `Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", minimumFractionDigits: 0, maximumFractionDigits: 2 })`, alineado a la derecha y con `tabular-nums` | `$ 1.234.567` |
| Monto `null` | Texto **«Sin dato»** en `text-[color:var(--di-text-tertiary)]`. Nunca `0`, `$ 0` ni `—` | Sin dato |
| Monto `0` | Se muestra como dato real | `$ 0` |
| Fecha | `dd/mm/aaaa hh:mm`, zona `America/Bogota`, `<time dateTime>` | 24/11/2026 14:05 |
| Placa | Componente `Placa` de `@flit/ui/Placa` en detalle y cabecera. En celdas, texto en mayúscula con `tracking-[0.08em] font-semibold` | ABC123 |
| Documento | `{tipo} {número}` sin máscara | CC 0000000 |
| Cifras | `Intl.NumberFormat("es-CO")` | 1.234 |
| Entidad cliente | Siempre **«compañía»** en la UI; nunca «empresa» ni «tenant». Única excepción: los textos de la 403, que repiten el de Diagnóstico (§4.5) | |
| Liquidación | «Liquidación por placa» (`INDIVIDUAL`) y «Liquidación masiva» o «lote» (`MASIVA`) | |
| Roles | «Administrador Impuestos» (`admin_impuestos`), «Gestor Impuestos» (`gestor_impuestos`) y «Super Admin» | |

### 0.2 Recetas locales (copia de forma de Diagnóstico) — `frontend-impuestos/components/comun/estilos.ts`

| Constante | Valor (idéntico al molde) | Molde |
|---|---|---|
| `TARJETA_CLS` | `rounded-[9px] border border-border bg-card px-6 pb-6 pt-4 shadow-none` | `frontend-diagnostico/components/configuracion/estilos.ts:5` |
| `FOCO_CLS` | `focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring` | ídem `:8` |
| `BOTON_PRIMARIO_CLS` / `BOTON_SECUNDARIO_CLS` | `buttonClass({ size: "sm", className: "gap-1.5 text-sm" })` / con `variant: "secondary"` | ídem `:11-13` |
| `BOTON_REINTENTAR_CLS` | `rounded-full border border-current px-3 py-1 text-xs font-semibold ${FOCO_CLS}` | ídem `:15` |
| `CAMPO_CLS`, `CAMPO_FECHA_CLS`, `ETIQUETA_CLS`, `AYUDA_CLS`, `OPCION_CLS`, `ENLACE_ACCION_CLS` | iguales | ídem `:17-46` |
| `CONTENEDOR_TABLA_CLS`, `CABECERA_TABLA_CLS`, `CABECERA_TABLA_ESTILO`, `CELDA_TABLA_CLS`, `FILA_TABLA_CLS`, `TEXTO_SECUNDARIO_CLS` | iguales | `frontend-diagnostico/components/tablero/vistasDetalle/comun.tsx:12-24` |
| `FILA_TABLA_HOVER_CLS` | igual, pero con `duration-[var(--di-motion-fast)]` en lugar de `--dx-motion-rapido` | ídem `:21` |

Las tablas propias se apilan como tarjetas por debajo de 1024 px con `TABLA_MOVIL_*` de `@flit/ui/table-styles` y `data-label` en cada celda, como `ListadoDiagnosticos.tsx`.

---

## 1. Estados → chip (tono de token y literal)

**Componente:**
- Estado único y vivo de una pantalla (cabecera del lote o del ítem): `StatusBadge` de `@flit/ui/StatusBadge`, con `tone` y `label`. Lleva `role="status"`.
- En tablas y listas: `etiquetaKit(tono)` + `<span className={ETIQUETA_PUNTO_CLS} aria-hidden />` + texto, **sin** `role="status"`, para no crear decenas de regiones vivas. El molde es `Etiqueta` de `comun.tsx:45`.
- Lote `EN_PROCESO`: `EtiquetaEnProceso` de `@flit/ui/EtiquetaEnProceso`, como `EstadoGlobal.tsx:5`.
- Todo vive en una sola tabla, `lib/liquidaciones/estados.ts`, que consume `components/liquidaciones/ChipEstado.tsx`. Un código desconocido se pinta en `neutral` como «Sin clasificar», nunca con el código crudo.

Mapa de tonos (`StatusBadgeTone` → token):
- `success` → `--badge-success-*` (verde)
- `info` → `--badge-info-*` (azul)
- `warning` → `--badge-warning-*` (naranja)
- `danger` → `--badge-danger-*` (rojo)
- `neutral` → `--badge-neutral-*` (gris)
- `purple` → `--badge-purple-*`, que **es ámbar** (#f9ac00); se usa para «en espera, sin culpa del usuario»
- `tinta` → `--badge-tinta-*`

### 1.1 Lote (`liquidaciones.estado`)

| Código | Literal | Tono |
|---|---|---|
| `CREADO` | Creado | `neutral` |
| `EN_PROCESO` | En proceso | `EtiquetaEnProceso` (azul, punto pulsante; quieto con `prefers-reduced-motion`) |
| `COMPLETADO` | Completado | `success` |
| `COMPLETADO_CON_NOVEDADES` | Completado con novedades | `warning` |
| `CERRADO_MANUALMENTE` | Cerrado manualmente | `tinta` |

El tipo (`INDIVIDUAL` «Por placa», `MASIVA` «Masiva») es **texto plano**, no chip, como la modalidad Express/Masivo de `ListadoDiagnosticos.tsx:54`.

### 1.2 Ítem / placa (`liquidacion_items.estado`)

| Código | Literal | Tono | Nota |
|---|---|---|---|
| `PENDIENTE` | En cola | `info` | |
| `CONSULTANDO_RUNT` | Consultando RUNT | `info` | |
| `REINTENTO_PROGRAMADO` | Reintento programado | `purple` | Detalle: «Próximo intento a las hh:mm» |
| `ENVIANDO` | Enviando al proveedor | `info` | |
| `ENVIADO` | Esperando resultado | `info` | |
| `INCIERTO` | Incierto | `purple` | |
| `INCIERTO_REQUIERE_DECISION` | Incierto: requiere decisión | `warning` | Permite relanzar |
| `DATOS_INCOMPLETOS` | Datos incompletos | `warning` | Sub-texto: «Falta: {campo}» |
| `DEPARTAMENTO_NO_HABILITADO` | Departamento no habilitado | `neutral` | |
| `EXITOSO` + `paz_y_salvo` | Al día | `success` | |
| `EXITOSO` + `con_deuda` | Con deuda | `warning` | El estado de la liquidación es exitoso; el chip muestra el **resultado** |
| `ERROR` | Error | `danger` | Sub-texto por clase (§1.3) |
| `SIN_RESULTADO_LOTE_CERRADO` | Sin resultado (lote cerrado) | `neutral` | |

Marcas adicionales: chips pequeños junto al estado, siempre con texto.

| Marca | Literal | Tono |
|---|---|---|
| `parcial` | Parcial | `purple` |
| `posibleDuplicado` | Posible duplicado | `warning` |
| `resultado_tardio` | Resultado tardío | `info` |
| período `no_liquidable` | No liquidable | `neutral` |

### 1.3 Clase de error (`errorClase`)

| Código | Sub-texto bajo «Error» |
|---|---|
| `CORREGIR_DATOS` | Hay que corregir datos |
| `DEPENDE` | Depende del proveedor o de la secretaría |
| `REINTENTABLE` | Se puede relanzar |

El mensaje detallado sale de `rotular()` sobre `errorCodigo` (lista cerrada en `lib/rotulos.ts`). Nunca se muestra `error_mensaje` del servidor. El Super Admin ve además «Código: {errorCodigo}» en `TEXTO_SECUNDARIO_CLS`.

### 1.4 Recibo (`recibos.estado`)

| Código | Literal | Tono | Acción |
|---|---|---|---|
| `GUARDADO` | Disponible | `success` | «Descargar PDF» |
| `PENDIENTE` | Preparando | `info` | — |
| `SIN_ARCHIVO` | Sin archivo | `neutral` | — |
| `PENDIENTE_REINTENTO_MANUAL` | No se pudo guardar | `warning` | — (lo resuelve soporte) |

### 1.5 Aprovisionamiento (backoffice de Impuestos y `EstadoAprovisionamiento` en `frontend/`)

| Código | Literal del chip | Tono | Motivo legible | Acción |
|---|---|---|---|---|
| `PENDIENTE` | Configurando | `EtiquetaEnProceso` | Se está conectando la compañía con el proveedor. Se actualiza en unos segundos. | — |
| `LISTO` | Listo | `success` | (si `firmaConfirmada=false`) Firma del webhook sin confirmar: llega con el primer resultado. | — |
| `LISTO_SIN_WEBHOOK` | Listo sin webhook | `success` + chip `purple` «Sin webhook» | Liquida, pero los resultados se consultan cada pocos minutos porque falta el secreto del webhook. | Cargar secreto del webhook |
| `ERROR` (`DATOS`) | Error en los datos | `danger` | El proveedor rechazó los datos de la compañía. Revisa la razón social y el NIT. | Reintentar |
| `ERROR` (`AMBIENTE`) | Error de configuración | `danger` | La integración con el proveedor no está habilitada en este ambiente. Avisa a soporte. | Reintentar |
| `INCIERTO` | Incierto | `purple` | No sabemos si el proveedor creó la compañía. Reintenta para confirmarlo. | Reintentar |
| `REQUIERE_CREDENCIALES` | Requiere credenciales | `warning` | La compañía ya existe en el proveedor. Rota las credenciales en su consola y cárgalas aquí. | Cargar credenciales |
| `REQUIERE_REVISION_NIT` | Requiere revisión del NIT | `warning` | El NIT no permite separar el dígito de verificación, falta o es igual al de la compañía cabeza. | Corregir NIT y luego Reintentar |
| `SUSPENDIDO` | Suspendido | `neutral` | Impuestos está apagado para esta compañía. Al encenderlo sigue con las mismas credenciales. | — |
| marca `modo_respaldo_firma` | Respaldo de firma | `purple` | Llegaron varias firmas inválidas: los resultados se consultan sin webhook hasta el próximo aviso válido. | Cargar secreto del webhook |

### 1.6 Usuario

| Estado | Literal | Tono |
|---|---|---|
| Activo | Activo | `success` |
| Invitado | Invitado | `info` |
| Suspendido | Suspendido | `warning` |

Se copia la forma de `frontend-diagnostico/components/usuarios/EtiquetaEstadoUsuario.tsx`.

---

## 2. Shell y navegación (M15, ya integrado; ajustes en §4)

- `SuiteShell` de `@flit/shell/SuiteShell` con `layout="app"` y **`dockDistribucion="equilibrada"`**, como `DiagnosticoShell.tsx:230`. El contenido va dentro de `flex min-h-full flex-col gap-4 px-4 pt-6 md:px-6`.
- Orden del dock: módulo de liquidaciones · Reportes · (Backoffice, solo SA) | Inicio (Dashboard) | (Usuarios) · (Configuración). Cada sección tiene una sola entrada y funciona como enlace directo. El filtro es por permiso y la API vuelve a exigirlo.
- Botón de inicio = Dashboard (`/`). No se repite como entrada del dock.
- `headerActions` (solo con sesión): `SelectorEmpresa` (solo SA) + `CampanaAvisos`.
- Ícono del producto: glifo **`pesos`** en todos los sitios: el dock, `packages/shell/src/apps.ts` (el `receipt` de la DDL se mapea a `iconoComo("pesos")`, porque el kit no trae glifo de recibo) y `CabeceraModulo icono` (§5, hueco H-3). La ilustración del dock es `rutaKit("modulo/pagos")`; no existe `modulo/impuestos.svg` (hueco H-4).

---

## 3. Pantallas

Cada pantalla abre con `CabeceraModulo` de `@flit/ui/CabeceraModulo` (tarjeta título: `titulo`, `descripcion`, `icono`, `miga`, `acciones`). En vista SA sin compañía, `descripcion` = «Todas las compañías»; con compañía, su nombre. Toda petición propaga `?empresa=` con `conEmpresa()` y todo enlace interno con `hrefConEmpresa()`.

### 3.1 Dashboard — `/` (M17)

**Molde:** `frontend-diagnostico/components/estadisticas/Dashboard.tsx`, `TarjetasResumen.tsx`, `FiltrosDashboard.tsx` y `graficas/*`; spec previa `diagnostico-ola-ux-dashboard/spec-ui.md` §4.

```
┌ CabeceraModulo: icono pagos · «Dashboard» · «Resumen de {Compañía | todas las compañías}» ── [Desde ▢][Hasta ▢] ┐
│   chip neutral «Vista global» (solo SA sin compañía)                                                           │
└────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
[KPI×6]  grid-cols-2 md:grid-cols-3 xl:grid-cols-6 (TarjetaMetrica)
[Liquidaciones por día (lg:col-span-2)]      [Monto total liquidado]        grid-cols-1 lg:grid-cols-3
[Por departamento (lg:col-span-2)]           [Últimos 5 lotes]              grid-cols-1 lg:grid-cols-3
[Compañías con más liquidaciones — solo SA en vista global, ancho completo]
```

- **Selector de compañía:** no se repite aquí; está en la barra. Esto es una diferencia deliberada con Diagnóstico, que lo pone en la cabecera: en Impuestos la compañía afecta a todas las pantallas. Elegir una compañía en el ranking llama a `elegir(id)` de `useEmpresaSeleccionada`.
- **Rango:** dos `input type="date"` con `<label>` visible «Desde» y «Hasta», `CAMPO_FECHA_CLS`, `min` y `max` cruzados y `max` = hoy. Por defecto, los últimos 30 días. Si el rango pasa de 366 días: error junto a «Hasta», «El rango no puede superar un año», con `aria-invalid` y sin petición. Un 400 se pinta con el mismo texto.
- **KPIs** (`TarjetaMetrica` de `@flit/ui/TarjetaMetrica` dentro de `METRICAS_GRID_CLS`, sin `colorCifra`):

| # | `etiqueta` | `cifra` | `pie` | `icono` (MAPA_ICONO) | `href` |
|---|---|---|---|---|---|
| 1 | Liquidaciones | total | «{n} por placa · {m} masivas» | `reportes-detallados` | `/liquidaciones?desde&hasta` |
| 2 | Lotes en proceso | n | — | `en-curso` | `/liquidaciones?tipo=MASIVA&estado=EN_PROCESO` |
| 3 | Exitosas | n | — | `finalizados` | — |
| 4 | Con error | n | — | `errores` | `/reportes?reporte=errores&desde&hasta` |
| 5 | Pendientes de resultado | n | «{i} inciertas» si i > 0 | `pendientes` | — |
| 6 | Placas con deuda | n | «{a} al día» | `pagos` | `/reportes?reporte=deuda&desde&hasta` |

- **Monto total liquidado** (`components/estadisticas/MontoTotal.tsx`): tarjeta `TARJETA_CLS` con `h2` «Monto total liquidado» y la cifra en `text-[length:var(--di-text-cifra)] font-bold tabular-nums`.
  - `montoTotal === null`: «Sin dato».
  - `liquidacionesSinMonto > 0`: pie «No incluye {n} liquidaciones sin monto».
  - `parcial === true`: `StatusBadge tone="purple" label="Parcial"` + «Incluye resultados parciales del proveedor».
- **Liquidaciones por día:** barras por cubeta, con la forma de `BarrasSerie`. Dos series: por placa en `--chart-1` y masivas en `--chart-2`. Leyenda en texto y «Ver datos» con la tabla alternativa.
- **Por departamento:** lista de barras con la forma de `ListaBarras`: nombre del departamento, cantidad y monto (si hay). Barra en `--chart-1`.
- **Últimos 5 lotes:** lista. Cada fila: «Masiva · {fecha}», chip de estado (§1.1), `BarraProgreso` («{t} de {n} placas») y enlace `ENLACE_ACCION_CLS` «Ver lote».
- **Ranking (solo SA global):** `ListaBarras` con `onElegir`, cada fila es un botón con nombre accesible «Ver {Compañía}».

| Estado | Contenido |
|---|---|
| Cargando | Skeleton por bloque: `animate-pulse rounded-[9px] bg-[var(--di-surface-sunken)] motion-reduce:animate-none`, `role="status" aria-busy` + `sr-only` «Cargando el resumen…». KPIs con cifra «—». |
| Vacío | `EstadoVacio`: título «Aún no hay liquidaciones en este periodo»; mensaje «Cambia el rango de fechas o liquida una placa.»; acciones [«Liquidar por placa»] (solo con `liquidaciones.crear` y producto no suspendido) y [«Ver los últimos 90 días»]. |
| Error | Por bloque: `InlineAlert tone="error"` «No se pudo cargar {bloque}» + «Reintentar». Error total: «No se pudo cargar el resumen» + «Reintentar». Un bloque `null` muestra «Sin datos para este bloque» (`BloqueSinDatos` de `Dashboard.tsx:87`), nunca ceros. |
| Lleno | Lo descrito arriba. |

### 3.2 Liquidaciones (grid) — `/liquidaciones` (M16)

**Molde:** `frontend-diagnostico/app/(app)/diagnosticos/page.tsx` (tarjetas Express y Masivo sobre el listado) + `components/listado/ListadoDiagnosticos.tsx` y `FiltrosListado.tsx`.

```
┌ CabeceraModulo: «Liquidaciones» · {Compañía} ──────────────────────────────────────────────┐
┌ Liquidar por placa (TARJETA) ───────────┐┌ Liquidación masiva (TARJETA) ──────────────────┐   lg:grid-cols-2
│ «Una placa, sin crear un lote.»          ││ «Muchas placas desde un archivo.»               │
│ [Placa ____] [Liquidar]                  ││ [Carga masiva] [Descargar plantilla]            │
└──────────────────────────────────────────┘└─────────────────────────────────────────────────┘
[ Todas ][ Masivas ][ Por placa ]   ← Pestanas (?tipo=)
[Estado ▾] [Desde ▢] [Hasta ▢] [Buscar placa ___]                           [Limpiar filtros]
┌ tabla por defecto ────────────────────────────────────────────────────────────────────────┐
│ ID · Tipo* · Creada · Placas · Avance · Estado · Creada por · Compañía** · (acción)       │
└───────────────────────────────────────────────────────────────────────────────────────────┘
Pagination
* Tipo solo en «Todas»   ** solo SA en vista global
```

- Tarjetas de creación: solo con `liquidaciones.crear`. Con el banner de suspensión o de «no listo» activo (§4.3), los botones quedan deshabilitados con `aria-describedby` hacia el banner.
  - «Liquidar»: `BOTON_PRIMARIO_CLS`. Lleva a `/liquidaciones/nueva?placa={placa}` o, si se implementa inline, hace el POST directo con los errores de §3.3.
  - «Carga masiva»: `BOTON_PRIMARIO_CLS` con `Icono "reporte-detallado"`.
  - «Descargar plantilla»: `BOTON_SECUNDARIO_CLS` con `Icono "nube-bajar"` sobre `GET /liquidaciones/plantilla`.
- Pestañas: `Pestanas` de `@flit/ui/Pestanas` con `ariaLabel="Tipo de liquidación"`: «Todas» (por defecto, cumple F3-CF6 con la columna Tipo), «Masivas» (`tipo=MASIVA`) y «Por placa» (`tipo=INDIVIDUAL`). La activa va en la URL.
- Filtros: `select` nativo «Estado» con `CAMPO_CLS` y las opciones de §1.1, fechas como en §3.1 y búsqueda «Buscar placa» (`placa=`, en mayúscula). «Limpiar filtros» es `Button variant="ghost" size="sm"` y solo aparece con algún filtro activo. Cada cambio vuelve a la página 1.
- Celdas:
  - ID: `idCorto` + enlace. Una `MASIVA` lleva a `/liquidaciones/{id}`; una `INDIVIDUAL`, directo a `/liquidaciones/{id}/items/{items[0].id}` (o a la del lote, que redirige).
  - Placas = `totalItems`.
  - Avance: `BarraProgreso` de `h-2` + «{terminadas}/{total}».
  - Estado: chip de §1.1. En `INDIVIDUAL` se muestra el chip del ítem (§1.2).
  - Creada por: `creadaPor.nombre`.
- Paginación: `Pagination` de `@flit/ui/Pagination` (`noun="liquidaciones"`, tamaños de `usePaginacion`).

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando liquidaciones…"` en el bloque de la tabla. Las tarjetas siguen usables. |
| Vacío (sin datos) | `EstadoVacio` «Aún no hay liquidaciones» · «Liquida una placa o sube un archivo para empezar.» · [«Liquidar por placa»]. |
| Vacío (por filtro) | `EstadoVacio` «Ninguna liquidación coincide con los filtros» · «Prueba con otro rango o quita algún filtro.» · [«Limpiar filtros»]. |
| Error | `InlineAlert tone="error" title="No se pudieron cargar las liquidaciones"` con texto fijo por `code` + «Reintentar» (`BOTON_REINTENTAR_CLS`). |
| Lleno | La tabla paginada. |

### 3.3 Liquidación por placa — `/liquidaciones/nueva` (M16)

**Molde:** `frontend-diagnostico/components/nuevo/CreacionExpress.tsx` + `components/express/CapturaExpress.tsx`.

- `CabeceraModulo` «Liquidar por placa», con miga «Liquidaciones › Liquidar por placa».
- Una `TARJETA_CLS` con un `<form noValidate>`:
  - `label` «Placa» + `input` con `CAMPO_CLS uppercase tracking-[0.08em]`, `autoComplete="off"`, `maxLength` 6–7 y `inputMode="text"`. Se precarga desde `?placa=`.
  - Ayuda (`AYUDA_CLS`): «Solo la placa: los datos del vehículo y del propietario se consultan en el RUNT.»
  - CTA `Button` primario «Liquidar» (`cargando` mientras envía) y `Button variant="secondary"` «Cancelar», que vuelve a `/liquidaciones`.
- Si llega 201, navega a `/liquidaciones/{id}/items/{items[0].id}`.

| Estado | Contenido |
|---|---|
| Vacío | Formulario con el campo enfocado al cargar. |
| Cargando | CTA con `aria-busy` y «Creando la liquidación…». Campo `disabled`. |
| Error | 422 `PLACA_INVALIDA`, junto al campo con `aria-invalid` y `aria-describedby`: «Escribe una placa válida, como ABC123.» · 409 `APROVISIONAMIENTO_NO_LISTO`, `InlineAlert warning`: «Impuestos aún no está listo para liquidar en tu compañía.» · 409 `PRODUCTO_SUSPENDIDO`, `InlineAlert warning`: «Impuestos está suspendido: no se pueden crear liquidaciones.» · Otro, `InlineAlert error` «No se pudo crear la liquidación» + «Reintentar». No se pierde lo digitado. |
| Lleno | (Navega al detalle.) |

### 3.4 Carga masiva — `/liquidaciones/masiva` (M16)

**Molde:** `frontend-diagnostico/components/carga/CargaMasiva.tsx`, `SubidaArchivo.tsx` y `ResumenValidacion.tsx`.

- `CabeceraModulo` «Liquidación masiva», con miga «Liquidaciones › Liquidación masiva» y `encima` = `PasosHorizontal` de `@flit/ui/PasosHorizontal` (`ariaLabel="Pasos de la carga"`, pasos «Subir archivo · Revisar · Iniciar»).
- **Paso 1** (`TARJETA_CLS`):
  - `ZonaArrastre` de `@flit/ui/ZonaArrastre`: `etiqueta="Archivo de placas (.xlsx o .csv)"`, `aceptar={[".xlsx", ".csv"]}`, `maxBytes={5 * 1024 * 1024}`, `ayuda="Una columna Placa. Máximo 5 MB."`.
  - «Descargar plantilla» (secundario).
  - Aviso `AYUDA_CLS`: «El análisis no consulta el RUNT ni el proveedor: solo revisa el archivo.»
  - CTA «Analizar archivo».
- **Paso 2** (`ResumenAnalisis.tsx`):
  - Conteos con `etiquetaKit`: «Válidas» `success`, «Duplicadas (se liquidan una vez)» `neutral`, «Inválidas» `danger`.
  - Tope: `BarraProgreso etiqueta="Placas válidas frente al máximo del lote" textoValor="{validas} de {tope}"`.
  - Tabla de inválidas: Fila · Valor · Motivo (motivo rotulado).
  - Con `superaTope`: `InlineAlert tone="warning"` «El archivo tiene {validas} placas válidas y el máximo por lote es {tope}. Divide el archivo para continuar.». CTA deshabilitado con ese texto en `aria-describedby`.
  - «Subir otro archivo» (secundario).
- **Paso 3:** CTA primario «Iniciar liquidación de {validas} placas». Con 201 navega a `/liquidaciones/{id}`.

| Estado | Contenido |
|---|---|
| Vacío | Paso 1 sin archivo. |
| Cargando | «Analizando el archivo…» (`VehicleLoader` en la tarjeta) / «Creando el lote…» en el CTA con `aria-busy`. |
| Error | 413 «El archivo pesa más de 5 MB.» · 415 «Formato no admitido: sube el archivo en .xlsx o .csv (puedes partir de la plantilla).» · 422 `COLUMNA_PLACA_AUSENTE` «El archivo no tiene la columna Placa. Parte de la plantilla.» · 422 `LIMITE_SUPERADO` = texto de `superaTope` · 409, como en §3.3 · Otro: «No se pudo analizar el archivo» + «Reintentar» sobre el mismo archivo. |
| Lleno | Resumen del paso 2 y su tabla. |

### 3.5 Detalle del lote — `/liquidaciones/[id]` (M16): avance, cierre manual y relanzar

**Molde:** `frontend-diagnostico/components/tablero/TableroLote.tsx`, `CabeceraLote.tsx`, `EstadoGlobal.tsx` y `MetricasLote.tsx`.

```
┌ CabeceraModulo: miga «Liquidaciones › Lote {idCorto}» · «Liquidación masiva» ─── [chip estado] [Cerrar lote]* ┐
│  «ID {idCorto} · Creada el dd/mm/aaaa hh:mm · Por {nombre} · {Compañía}**»                                     │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌ Avance del lote (TARJETA) ───────────────────────────────────────────────────────────────────────────────────────┐
│ BarraProgreso «{t} de {n} placas terminadas» · «Se actualiza solo mientras el lote está en proceso.»            │
│ Embudo ChipFiltro: Todas · Al día · Con deuda · En proceso · Inciertas · Datos incompletos · Con error · Sin res.│
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌ Placas del lote (TARJETA) [Buscar placa ___] ────────────────────────────────────────────────────────────────────┐
│ Placa · Propietario · Documento · Departamento · Estado · Total a pagar · Acciones                              │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
* Admin y SA, con estado CREADO o EN_PROCESO      ** solo SA
```

- Embudo: `ChipFiltro` de `@flit/ui/ChipsFiltro` dentro de `EMBUDO_CLS` (`role="group" aria-label="Filtros del lote"`), exclusivos y con `aria-pressed`. Cifra 0 en neutral. Filtra `?estado=`.
- Sondeo mientras está `EN_PROCESO`: cada 5 s, se duplica si nada cambia hasta 30 s y se detiene al pasar a terminal. Si falla, `InlineAlert tone="warning"` «Datos desactualizados desde hh:mm» + «Reintentar», sin borrar lo leído.
- `CERRADO_MANUALMENTE`: `InlineAlert tone="info" vivo={false}` «Cerrado manualmente el {fecha} por {nombre}.» + «Motivo: {motivo}» si existe.
- Acciones por fila: `ActionsMenu` de `@flit/ui` (`ariaLabel="Acciones de la placa"`, descrito por la celda Placa) con «Ver detalle» y «Relanzar» (solo en `INCIERTO_REQUIERE_DECISION` o `ERROR` `REINTENTABLE`, con `liquidaciones.crear`). La placa también es enlace al detalle.
- **`DialogoCerrarLote`** (`Modal` de `@flit/ui/Modal`, `size="md"`, `icono="alerta"`, `closeOnBackdrop={false}`, `busy` mientras envía):
  - Título «Cerrar lote».
  - Cuerpo: «Las {p} placas que siguen pendientes quedarán como «Sin resultado (lote cerrado)». Si después llega un resultado, se aplica a la placa sin reabrir el lote.»
  - Campo `textarea` «Motivo (opcional)», con un máximo de 500.
  - Pie: `Button variant="secondary"` «Cancelar» (foco inicial) + `Button variant="alerta"` «Cerrar lote».
  - Errores dentro del diálogo, que no se cierra: 409 `YA_TERMINADA` «El lote ya terminó; no hace falta cerrarlo.»; otro, «No se pudo cerrar el lote. Intenta de nuevo.».
- **`DialogoRelanzar`** (`Modal size="md"`, `icono="reintento"`, `closeOnBackdrop={false}`):
  - Título «Relanzar la placa {PLACA}».
  - Cuerpo: «Se creará una liquidación por placa nueva. Si el proveedor sí recibió la anterior, la placa puede quedar duplicada.»
  - Casilla obligatoria «Entiendo que puede quedar duplicada» (`MARCA_CLS`).
  - Pie: «Cancelar» + `Button` primario «Relanzar», deshabilitado hasta marcar la casilla y con el motivo visible.
  - Con 201 navega al ítem nuevo. 409 `NO_RELANZABLE`: «Esta placa ya no se puede relanzar. Recarga para ver su estado.»

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando el lote…"`. |
| Vacío | Lote sin ítems todavía (`CREADO`): `EstadoVacio` «Este lote todavía no tiene placas en proceso» · «Se actualiza solo en unos segundos.». Por filtro: «Ninguna placa coincide con el filtro» + «Quitar filtro». |
| Error | 404 → `notFound()`. Otro: `InlineAlert error` «No se pudo cargar el lote» + «Reintentar». |
| Lleno | Lo descrito arriba. |

### 3.6 Detalle de la placa (ítem) — `/liquidaciones/[id]/items/[itemId]` (M16): montos y PDF

**Molde:** `frontend-diagnostico/components/backoffice/gestionSaneamiento/CabeceraGestion.tsx` (cabecera con placa), `components/tablero/vistasDetalle/LiquidacionesImpuesto.tsx` (vigencias + botón de descarga con estado propio) y `ListaDefinicion` de `@flit/ui/ListaDefinicion`.

```
┌ CabeceraModulo: miga «Liquidaciones › Lote {idCorto} › ABC123» (individual: «Liquidaciones › ABC123») ┐
│ titulo «ABC123» (tituloClassName "uppercase tracking-[0.12em]") · descripción «Liquidación por placa|masiva · dd/mm/aaaa» │
│ acciones: [StatusBadge estado] [chips de marca] [Relanzar]*                                           │
└────────────────────────────────────────────────────────────────────────────────────────────────────────┘
[Aviso de resultado — InlineAlert según estado]
┌ Vehículo y propietario ──────────┐┌ Totales ───────────────────────────────┐     lg:grid-cols-2
│ ListaDefinicion columnas=2        ││ Total a pagar · Vigencias · Vencimiento │
└───────────────────────────────────┘└─────────────────────────────────────────┘
┌ Vigencias (tabla) ─────────────────────────────────────────────────────────────────────────────┐
│ Vigencia · Avalúo · Impuesto · Sanción · Intereses · Descuentos · Pagos previos · Total a pagar · Vencimiento · Referencia de pago │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
┌ Recibos (lista) ────────────────────────────────────────────┐
┌ TarjetaPlegable «Observaciones del proveedor» (warnings · validations · file_errors) ┐
```

- Aviso de resultado (`InlineAlert vivo={false}`), según el estado:
  - En proceso (`PENDIENTE`…`ENVIADO`): `EtiquetaEnProceso` + «Esperando el resultado del proveedor. Esta página se actualiza sola.» Sondeo como en §3.5.
  - `parcial`: `pending`, «Resultado parcial: el proveedor no pudo liquidar todas las vigencias.»
  - `ERROR`: `error`, con título = sub-texto de la clase (§1.3) y cuerpo = mensaje rotulado.
  - `DATOS_INCOMPLETOS`: `warning`, «Faltan datos del RUNT para liquidar: {campo}.»
  - `DEPARTAMENTO_NO_HABILITADO`: `info`, «El departamento {nombre} no está habilitado en el contrato de tu compañía.»
  - `INCIERTO`: `pending`, «No sabemos todavía si el proveedor recibió la solicitud. Se confirma sola en unos minutos.»
  - `INCIERTO_REQUIERE_DECISION`: `warning`, «No se pudo confirmar si el proveedor recibió la solicitud. Puedes relanzarla; si sí la recibió, puede quedar duplicada.»
  - `SIN_RESULTADO_LOTE_CERRADO`: `info`, «El lote se cerró antes de recibir el resultado de esta placa.»
  - `resultado_tardio`: `info`, «Este resultado llegó después de cerrar el lote.»
  - `posibleDuplicado`: `warning`, «Esta liquidación es un relanzamiento: puede estar duplicada en el proveedor.» + enlace «Ver la original».
- Vehículo y propietario (`ListaDefinicion`, `columnas={2}`, `vacio="Sin dato"`): Placa (`Placa`), Departamento, Secretaría, Propietario y Documento. Nada de dirección, correo ni teléfono.
- Totales: `ListaDefinicion` con «Total a pagar» (monto §0.1, `text-base font-bold`), «Vigencias pendientes» (n) y «Vencimiento más próximo». **`null` muestra «Sin dato»; `0` muestra `$ 0`.**
- Vigencias (`TablaVigencias.tsx`): tabla por defecto (§0.2) dentro de un contenedor con scroll horizontal (`RegionDesplazable`, con la forma de Diagnóstico: `tabIndex=0`, `role="region"`, `aria-label="Vigencias"`). Montos alineados a la derecha. Cada celda `null` dice «Sin dato». Una fila `no_liquidable` lleva el chip «No liquidable». `otros_cargos` va como sub-fila desplegable «Otros cargos ({n})». Por debajo de 1024 px se apila con `TABLA_MOVIL_*`.
- Recibos (`ListaRecibos.tsx`), una fila por recibo:
  - Nombre: «Recibo vigencia {periodos}», «Recibo consolidado» o «Paz y salvo».
  - Chip de §1.4.
  - Con `GUARDADO` y `recibos.descargar`: enlace con forma de botón `BOTON_SECUNDARIO_CLS` + `Icono "nube-bajar"` «Descargar PDF», con nombre accesible «Descargar PDF del recibo {nombre}». Estado propio por botón: «Descargando…» con `aria-busy`. Error bajo el botón con `role="alert"` y `aria-describedby`: 409 `RECIBO_NO_DISPONIBLE` «El recibo todavía no está disponible.»; otro, «No se descargó el recibo. Intenta de nuevo.».
  - Sin permiso, solo el estado.
  - Sin recibos: «Esta liquidación no tiene recibos.»
- Observaciones del proveedor: `TarjetaPlegable` de `@flit/ui/TarjetaPlegable` (`nivelTitulo={2}`, cerrada por defecto), con tres listas tituladas «Advertencias», «Validaciones» y «Archivos con error». De `validations` solo se muestran el campo y el mensaje; **nunca `sent`**, que puede traer PII. Sin ninguna, la tarjeta no se pinta.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando la placa…"`. |
| Vacío | No aplica como pantalla. Por bloque: «Sin vigencias pendientes», «Esta liquidación no tiene recibos.». |
| Error | 404 → `notFound()`. Otro: `InlineAlert error` «No se pudo cargar la placa» + «Reintentar». |
| Lleno | Lo descrito arriba. |

### 3.7 Reportes — `/reportes` (M17)

**Molde:** `frontend-diagnostico/components/reportes/ModuloReportes.tsx`, `FiltrosReporte.tsx`, `TablaReporte.tsx`, `BotonesExportar.tsx` y `lib/reportes/useDescarga.ts`; spec previa §5.

```
CabeceraModulo: icono reportes · «Reportes» · «Descarga la información de tus liquidaciones»
InlineAlert aviso PII (persistente, sin cerrar; id para aria-describedby de Exportar)
Pestanas: Detalle de liquidaciones · Resumen por lote · Placas con deuda · Errores de liquidación   (?reporte=)
Filtros: Desde · Hasta · Tipo · Estado · Departamento · Usuario · Placa              [Limpiar filtros]
Resultados «{N} registros» ─────────────────────────────── [Exportar Excel] [Exportar PDF]
DataTable + Pagination
```

- Aviso PII (`InlineAlert tone="warning" vivo={false}`):
  - En `detalle`, `deuda` y `errores`: «Este reporte contiene datos personales (documentos y nombres) protegidos por la Ley 1581 de 2012. Úsalos solo para la finalidad autorizada. Cada descarga queda registrada con tu usuario.»
  - En `lotes`: `tone="info"` «Cada descarga queda registrada con tu usuario.»
- Filtros:
  - Fechas como en §3.1.
  - «Tipo» con `SelectorSegmentado` de `@flit/ui/SelectorSegmentado`: Todas, Por placa y Masivas.
  - «Estado» y «Departamento»: `select` nativo con `CAMPO_CLS`.
  - «Usuario»: `SearchableSelect` con los usuarios del producto.
  - «Placa»: texto en mayúscula.
  - Solo se pintan los filtros que aplican al reporte activo.
- Tabla: `DataTable` de `@flit/ui/DataTable` (`ariaLabel` = nombre del reporte, `status` según la carga) + `Pagination`. En SA global, la primera columna es «Compañía». Los montos siguen §0.1, también «Sin dato».
- Exportar: dos `Button variant="secondary" size="sm"` con `iconoComo("nube-bajar")`, «Exportar Excel» y «Exportar PDF».
  - Ayuda: «Se exportan los {N} registros del filtro».
  - Tope: Excel 5.000 y PDF 1.000. Si `total` lo supera, el botón de ese formato queda deshabilitado con el motivo visible «Acota el filtro: máximo {L} registros en {Excel|PDF}». Un 422 `TOPE_EXPORTACION` da el mismo texto bajo los botones. Nunca se recorta en silencio.
  - En vuelo, «Generando…» con `aria-busy`.
  - Los botones llevan `aria-describedby` hacia el aviso PII.
  - Sin `reportes.exportar`, los botones no se pintan.

| Estado | Contenido |
|---|---|
| Cargando | `UiStateBoundary status="loading" skeletonRows={8}`; contador «— registros»; Exportar deshabilitado con «Espera a que cargue el reporte». |
| Vacío | Sin datos: `EstadoVacio` «No hay registros en este periodo» + «Cambiar rango». Por filtro: «Ningún registro coincide con los filtros» + «Limpiar filtros». Exportar deshabilitado con «No hay registros para exportar». |
| Error | `InlineAlert error` «No se pudo cargar el reporte» + «Reintentar». Si el error es solo de la exportación, la tabla sigue y el error va bajo los botones: «No se pudo generar el archivo. Intenta de nuevo.». |
| Lleno | Tabla paginada; celdas largas con `truncate max-w-[16rem]` + `title`. |

### 3.8 Usuarios — `/usuarios` (M18)

**Molde:** `frontend-diagnostico/components/usuarios/PaginaUsuarios.tsx`, `TablaUsuarios.tsx`, `TablaCompanias.tsx`, `ModalInvitarUsuario.tsx`, `ModalCambiarRol.tsx` y `DialogoConfirmarEstado.tsx`.

- Admin Impuestos: `CabeceraModulo` «Usuarios» (`icono="colaboradores"`, `descripcion` = compañía, `acciones` = `Button` primario «Invitar usuario» con `Icono "mas"`). Debajo, filtros (Buscar nombre o correo; Rol; Estado) y la tabla Nombre · Correo · Rol · Estado · Acciones (`ActionsMenu` «Acciones del usuario»: «Cambiar rol», «Suspender» con `tone="danger"`, «Reactivar»).
- SA sin `?empresa`: tabla de compañías con Impuestos (Compañía · NIT · Usuarios · «Ver usuarios»). Elegir una fija `?empresa=`, que también actualiza el selector de la barra, con miga «Usuarios › {Compañía}».
- Fila protegida: el AdminCompany es siempre Administrador Impuestos (D-ROL-ADMIN). En lugar del menú, «Administrador de la compañía: su rol se cambia en el admin de compañías». En la fila propia: «Eres tú».
- «Invitar usuario» (`Modal`, `closeOnBackdrop={false}`):
  - Descripción: «Le llegará un correo para entrar a Impuestos con el rol que elijas.»
  - Campos «Nombre completo», «Correo electrónico» y «Rol» (radios con descripción). Administrador Impuestos: «Todo en su compañía: liquidar, cerrar lotes, usuarios y configuración.» Gestor Impuestos: «Ve toda la compañía y liquida por placa o masivo. No gestiona usuarios ni configuración.»
  - Errores por campo con los textos del molde y un resumen «Revisa los datos de la invitación».
  - CTA «Enviar invitación».
- Cambiar rol: título «Cambiar rol de {Nombre}», CTA «Guardar rol» (deshabilitado si no cambia).
- Suspender / Reactivar:
  - Título «Suspender usuario» o «Reactivar usuario».
  - Cuerpo al suspender: «Pierde el acceso a Impuestos. Su cuenta y los demás productos no cambian.»
  - Foco inicial en «Cancelar». La acción usa `variant="alerta"` al suspender.

| Estado | Contenido |
|---|---|
| Cargando | Esqueleto de tabla «Cargando usuarios…». |
| Vacío | «Ningún usuario coincide» · «Prueba con otro nombre o correo, o quita algún filtro.» Sin usuarios: «Aún no hay usuarios en Impuestos» + «Invitar usuario». |
| Error | `InlineAlert error` «No pudimos cargar los usuarios» + «Reintentar». Errores de diálogo dentro del diálogo. |
| Lleno | La tabla. |

### 3.9 Configuración — `/configuracion` (M18)

**Molde:** `frontend-diagnostico/app/(app)/configuracion/page.tsx`, `components/configuracion/PestanaOperacion.tsx` y `PestanaContrato.tsx`. Las pestañas usan `Pestanas` de `@flit/ui`, no la copia local de `ConfiguracionTabs.tsx`.

- `CabeceraModulo` «Configuración» (`icono="configuracion"`, `descripcion` = compañía).
- `Pestanas` (`ariaLabel="Configuración de Impuestos"`):
  - **«Operativa»**: Admin y SA, que la editan.
  - **«Contractual»**: visible para todos con `configuracion.ver`, editable solo con `configuracion.contrato.editar` (SA). Para el Admin es de solo lectura.
- Operativa (`<form>`):
  - «Máximo de placas por lote»: `input type="number"`, `min=1`, `max` = contractual. Ayuda «Hasta {N}, lo contratado.»
  - «Correos para el aviso de lote terminado»: `textarea`, un correo por línea. Ayuda «Reciben un correo cuando un lote termina o se cierra.» El error va por línea: «La línea {n} no es un correo válido.».
  - Pie: `AYUDA_CLS` «Los cambios aplican a los lotes nuevos.» + «Descartar» (secundario) + «Guardar configuración» (primario).
  - Éxito: `InlineAlert tone="success"` «Configuración guardada.». 422 `TOPE_SUPERA_CONTRACTUAL`: junto al campo, «El máximo no puede superar lo contratado: {tope} placas.».
- Contractual:
  - Solo lectura: `ListaDefinicion` con «Máximo de placas por lote contratado» y «Departamentos habilitados» (chips `neutral`, o «Todos los departamentos» si es `null`).
  - SA editor: el selector de la barra elige la compañía; sin compañía elegida, `EstadoVacio` «Elige una compañía en la barra para editar su contrato».
  - Campos: el número y un `fieldset` «Departamentos habilitados» con el interruptor «Todos los departamentos» (`Interruptor` de `@flit/ui/Interruptor`) y las casillas de `departamentosDisponibles`.
  - CTA «Guardar contrato». 422 (departamento no disponible): «{Departamento} no está disponible en el proveedor.».
- Sin `configuracion.editar`: `InlineAlert tone="info" title="Solo lectura"` «Puedes ver la configuración, pero no cambiarla.» y los campos `disabled`.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando la configuración…"`. |
| Vacío | Sin correos: ayuda «Nadie recibe el aviso por correo; el aviso en pantalla sigue llegando.». |
| Error | Lectura: `InlineAlert error` «No se pudo cargar la configuración» + «Reintentar». Guardado: «No se guardó la configuración» + texto fijo, conservando lo digitado. |
| Lleno | El formulario. |

### 3.10 Backoffice SA — `/backoffice/empresas` y `/backoffice/empresas/[tenantId]` (M18)

**Molde:** `frontend-diagnostico/components/backoffice/ListaLotesBackoffice.tsx` (banda de contexto + tabla + filtros en la URL) y, para el formulario protegido, `frontend/components/admin/companies/tabs/CredencialImpuestoDiagnostico.tsx`.

**Lista:**
- `CabeceraModulo` «Compañías con Impuestos» (`icono="empresas"`, `descripcion` «Estado de la conexión de cada compañía con el proveedor»).
- Banda de contexto (`InlineAlert tone="info" vivo={false}`): «Estás en el backoffice de FLIT: ves todas las compañías, incluidas las suspendidas.»
- Búsqueda «Buscar por nombre o NIT» (`q`, en `sessionStorage`, nunca en la URL porque un NIT puede ser una cédula, como hace Diagnóstico).
- Tabla: Compañía · NIT · Estado (chip §1.5 + chip «Firma sin confirmar» `purple` si aplica) · Motivo · Acción («Ver detalle»).

**Detalle:**
- `CabeceraModulo` con titulo = compañía y miga «Backoffice › {Compañía}».
- Tarjeta «Estado»: chip, motivo legible (§1.5) y acción.
  - «Reintentar aprovisionamiento» (`Button variant="secondary"`) en `ERROR`, `INCIERTO` y `REQUIERE_REVISION_NIT`. Con 202: `InlineAlert success` «Reintento en cola. El estado se actualiza en unos segundos.». 409: «Este estado ya no admite reintento. Recarga para ver el actual.».
- Tarjeta «Credenciales» (`FormularioCredenciales.tsx`):
  - Estado: «API key: Configurada el {fecha} · empieza por {apiKeyPrefijo}» o «No configurada»; «Secreto del webhook: Configurado» o «No configurado»; «Origen: Propia | Copiada de Diagnóstico | Cargada a mano».
  - «Cargar credenciales» / «Reemplazar» abre el formulario:
    - «API key» y «Secreto del webhook (opcional)», `type="password"` con `autoComplete="off" spellCheck={false} autoCapitalize="none"` y botón `Button variant="ghost" size="sm"` «Mostrar API key» / «Ocultar API key» con `aria-pressed`.
    - Ayuda: «Rota las credenciales en la consola del proveedor antes de cargarlas. Al guardar se validan con el proveedor y no se vuelven a mostrar.»
    - CTA «Validar y guardar» + «Cancelar» (`variant="navy"`, como el molde).
  - Al guardar (204): se limpian los campos, se cierra el formulario y sale `InlineAlert success` «Credenciales guardadas y validadas.».
  - 422 `CREDENCIAL_INVALIDA`: «El proveedor no aceptó la API key. Revisa que sea la vigente.». 409: «En este estado no se pueden cargar credenciales.».
- Tarjeta «Contrato»: enlace «Editar contrato» a `/configuracion?pestana=contractual&empresa={id}`.
- Historial: tabla Fecha · De · A · Motivo · Por. El motivo va rotulado y «Por» dice «Sistema» cuando no hay actor.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader` «Cargando compañías…» / «Cargando la compañía…». |
| Vacío | «Ninguna compañía tiene Impuestos activo todavía» / por búsqueda: «Ninguna compañía coincide». Historial vacío: «Sin cambios registrados.». |
| Error | `InlineAlert error` «No se pudieron cargar las compañías» + «Reintentar»; 404 en el detalle → `notFound()`. |
| Lleno | Lo descrito arriba. |

### 3.11 `frontend/`: activación y estado en ProductosTab (M19)

**Molde:** el propio `ProductosTab.tsx` (`IconoModulo` + `ToggleSwitch` + `StatusBadge` con `icono={rutaKit(ICONO_TONO.*)}`) y `CredencialImpuestoDiagnostico.tsx` (subtarjeta `ml-12 rounded-xl border px-4 py-3 text-xs`). `frontend/` usa sus reexports (`@/components/atom/Modal`, `@/components/atom/StatusBadge`) y `@flit/ui/Button`.

**`DialogoActivacionImpuestos.tsx`:** se abre al encender `impuestos`. Es un `Modal` con `size="md"`, `icono="pesos"` y `closeOnBackdrop={false}`.
- Título: «Activar Impuestos».
- Descripción: «Antes de encenderlo, revisa los datos con los que se creará la compañía en el proveedor.»
- Cuerpo: `ListaDefinicion` con «NIT que se enviará» (base sin dígito de verificación, en `tabular-nums`), «Razón social» y «Correo de contacto» («Administrador de Compañía más antiguo»).
- Avisos según `estadoNit` y el alta previa:
  - `SIN_ADMIN`: `InlineAlert warning` «La compañía no tiene un Administrador de Compañía activo. Asigna uno antes de activar Impuestos.». **El CTA queda deshabilitado** con ese motivo (`aria-describedby`). El 409 `ProvisioningPrerequisiteMissing` da el mismo texto.
  - NIT `AMBIGUO`, `AUSENTE` o `IGUAL_A_CABEZA`: `InlineAlert warning` «El NIT de la compañía requiere revisión: {motivo legible}. Impuestos quedará en «Requiere revisión del NIT» hasta que lo corrijas en la pestaña Datos.». El CTA sigue habilitado.
  - `RAZON_SOCIAL_LARGA`: `InlineAlert warning` «La razón social pasa de 200 caracteres: el proveedor la rechazará. Acórtala en la pestaña Datos.».
- Pie: «Cancelar» (secundario) y «Activar Impuestos» (primario, `cargando`).
- Cuatro estados:
  - Cargando: `Cargador` + «Consultando los datos del alta…», con el CTA deshabilitado.
  - Error: `InlineAlert error` «No se pudieron leer los datos del alta» + «Reintentar».
  - Lleno: lo anterior.
  - Vacío: no aplica; un campo sin valor dice «Sin dato».
- Apagar Impuestos: se reutiliza la confirmación del tab, con texto propio:
  - Título: «¿Suspender Impuestos para esta compañía?».
  - Cuerpo: «Nadie de la compañía podrá entrar ni crear liquidaciones. Los lotes en curso terminan y el historial se conserva. Al volver a encenderlo, sigue con las mismas credenciales.».
  - Botón: «Suspender Impuestos».

**`EstadoAprovisionamiento.tsx`:** subtarjeta bajo el `ToggleSwitch` de Impuestos, con el mismo contenedor que `CredencialImpuestoDiagnostico`.
- Contenido: «Conexión con el proveedor:» + chip (§1.5, `StatusBadge` con `icono` de `ICONO_TONO` por tono) + motivo legible + acción:
  - «Reintentar» (`Button variant="secondary" size="sm"`).
  - Para credenciales o secreto: «Cargar credenciales en Impuestos» o «Cargar secreto en Impuestos», enlace externo a `{IMPUESTOS_URL}/backoffice/empresas/{id}` con `target="_blank" rel="noopener"` y «(se abre en otra pestaña)» en `sr-only`.
  - «Corregir NIT»: lleva a la pestaña Datos de la compañía y pone el foco en el campo NIT.
- La actualización se anuncia en una región `role="status"` del tab.
- Cuatro estados:
  - Cargando: va con los productos.
  - Sin fila: chip `neutral` «Sin estado todavía» · «Aparece en unos segundos tras encender Impuestos.».
  - Error del reintento: `role="alert"` «No se pudo pedir el reintento. Intenta de nuevo.».
  - Lleno: chip, motivo y acción.

**Hub y shell:**
- `frontend-hub/lib/products.ts`: ficha `impuestos` con `status: "soon"` y tagline «Liquidación del impuesto vehicular de tus placas».
- `packages/shell/src/apps.ts`: la misma tagline en `TAGLINES.impuestos` y `ICONS.receipt = iconoComo("pesos")`.

---

## 4. Auditoría de M15 (modo C)

**Archivos revisados:**
- `frontend-impuestos/components/{ImpuestosShell,SelectorEmpresa,BannerEstadoProducto,CampanaAvisos}.tsx`
- `app/(app)/layout.tsx`, `app/403/page.tsx`, `app/globals.css`, `app/layout.tsx` y `app/not-found.tsx`
- `lib/sesion.ts` y `lib/empresaSeleccionada.ts`
- Comparados con `frontend-diagnostico` (`DiagnosticoShell.tsx`, `app/403/page.tsx`, `app/globals.css`, `components/dx/campana/CampanaDiagnostico.tsx`, `components/estadisticas/SelectorCompania.tsx`)
- Grep de `#hex`: cero en componentes y CSS. `public/` es idéntico al de Diagnóstico.

**Veredicto global: Aprobado con observaciones.** No hay patrón roto ni valores fuera de tokens. Los ajustes son de coherencia y de copy y conviene hacerlos antes de M16 y M17, que se montan encima.

| Archivo | Veredicto | Cambio concreto |
|---|---|---|
| `components/ImpuestosShell.tsx` | **AJUSTE** | (1) Agregar `dockDistribucion="equilibrada"` a `SuiteShell` (`:176-190`), como `DiagnosticoShell.tsx:230`. Con el reparto por defecto («lados» con relleno), el Gestor ve dos módulos a la izquierda y un hueco a la derecha. (2) Renombrar el módulo «Impuestos» → **«Liquidaciones»** (`:45` y `:54`, label de la sección y de la entrada; `icon` e `href` siguen iguales). Hoy la barra dice «Impuestos», el botón de inicio «Inicio Impuestos» y el módulo «Impuestos»: tres controles con el mismo nombre y destinos distintos. Diagnóstico separa producto («Diagnóstico») y módulo («Diagnósticos»). Si un AC fija el literal «Impuestos», decide el PO. (3) Observación para M18, no bloquea M15: cuando el SA elige `?empresa=`, los banners deben salir del aprovisionamiento de **esa** compañía (`GET /backoffice/empresas/{id}/aprovisionamiento`) y no de la sesión propia. |
| `components/SelectorEmpresa.tsx` | **AJUSTE** | Vive en la barra: no debe crecer en alto. (1) Quitar el `<p role="status">` «Cargando compañías…» (`:90-94`) y en su lugar pasar `placeholder="Cargando compañías…"` con el combobox `disabled` y `aria-busy`. (2) Error (`:95-111`): fila única `flex items-center gap-1` con el combobox deshabilitado (`placeholder="No se cargaron las compañías"`) y un botón de 32 px «Reintentar» (`BOTON_REINTENTAR_CLS`, nombre accesible «Reintentar cargar compañías»). Tono `warning`, como `SelectorCompania.tsx:86`, no `error`: la vista global sigue funcionando. (3) Vacío (`:112`): ningún texto bajo el control; `noResultsLabel="No hay compañías con Impuestos"`. (4) Ancho `w-56` fijo (`:77`) → `w-40 sm:w-56`, para que quepa en 360 px junto a la campana y la cuenta. (5) Aplicar al `SearchableSelect` la clase de 24 × 24 px del botón «Quitar selección» (`QUITAR_24PX_CLS` de `SelectorCompania.tsx:53`, WCAG 2.5.8). El hueco real está en `packages/ui` (H-6). Hay que actualizar `SelectorEmpresa.test.tsx:46` (hoy espera `role="status"`). |
| `components/BannerEstadoProducto.tsx` + `lib/sesion.ts` | **AJUSTE** | Tonos y literales en §4.3. Hay que agregar el banner `NO_LISTO` en `bannersDeSesion` (`lib/sesion.ts:47-54`) para `PENDIENTE`, `ERROR`, `INCIERTO`, `REQUIERE_CREDENCIALES` y `REQUIERE_REVISION_NIT`. Hoy, en esos estados, el usuario no ve nada y su primera liquidación choca con un 409 `APROVISIONAMIENTO_NO_LISTO`. Hay que actualizar `ImpuestosShell.test.tsx:206` y `:215`. |
| `components/CampanaAvisos.tsx` | **AJUSTE menor** | (1) Agregar la línea de mensaje bajo el título, como el molde (`CampanaDiagnostico.tsx:110`): `<p className="text-sm text-[color:var(--di-text-secondary)]">` con el texto de §4.4. (2) Panel (`:182`): agregar `transition-opacity duration-[var(--di-motion-base)] motion-reduce:transition-none`. Usar `--di-*` y no `--dx-*` está bien. (3) El resto es conforme: botón de 40 px, insignia `--kit-rojo` con blanco (6,6:1), `aria-expanded`, Escape devuelve el foco, cuatro estados y nombre accesible «Avisos, N avisos sin leer». «Marcar todas como leídas» no existe porque falta el endpoint (hueco de contrato H-9). |
| `app/(app)/layout.tsx` | **CONFORME** | Igual al molde. El `Suspense` con `VehicleLoader` es correcto: lo exige el `useSearchParams` del selector. |
| `app/403/page.tsx` | **CONFORME** | Igual al molde. Además mejora la guarda de `hasOwnProperty`. Literales en §4.5: solo se ajusta uno. |
| `app/globals.css` | **CONFORME** | Mismo orden de `@import` que Diagnóstico, sin `tokens-dx.css` (correcto: Impuestos no crea tokens) y con los tres `@source`. |
| `app/layout.tsx` / `app/not-found.tsx` (revisión extra) | **CONFORME** | `themeColor` hex = metadato, igual que el molde. La 404 usa `PaginaExcepcion` con `enlace={Link}`. |

### 4.3 Banners — literales definitivos (`BannerEstadoProducto.tsx`)

Las claves internas siguen siendo las del plan §6.1. El usuario ve títulos en lenguaje de negocio: «webhook» y «firma» no le dicen nada a un Gestor. Todos van con `vivo={false}`, se muestran sobre el contenido de cada pantalla y no se pueden cerrar. Si hay varios, se muestra solo el de mayor prioridad: `SUSPENDIDO` > `NO_LISTO` > `SIN_WEBHOOK` > `FIRMA_SIN_CONFIRMAR`.

| Clave | Estado de origen | `tone` | Título | Texto |
|---|---|---|---|---|
| `SUSPENDIDO` | `SUSPENDIDO` | `warning` | Impuestos está suspendido: solo lectura | Puedes consultar las liquidaciones, los recibos y los reportes, pero no crear liquidaciones nuevas. Los lotes que estaban en curso terminan solos. |
| `NO_LISTO` (nuevo) | `PENDIENTE`, `ERROR`, `INCIERTO`, `REQUIERE_CREDENCIALES`, `REQUIERE_REVISION_NIT` | `pending` | Impuestos todavía no está listo para liquidar | La conexión de tu compañía con el proveedor se está configurando. Puedes consultar lo que ya existe; para liquidar, espera a que termine o pídele ayuda a tu administrador. |
| `SIN_WEBHOOK` | `LISTO_SIN_WEBHOOK` | `info` | Los resultados pueden tardar un poco más | Ya puedes liquidar. Los resultados se consultan con el proveedor cada pocos minutos, en lugar de llegar al instante. |
| `FIRMA_SIN_CONFIRMAR` | `LISTO` con `firmaConfirmada=false` | `info` | Conexión con el proveedor en verificación | Ya puedes liquidar. Se confirma sola con el primer resultado que llegue. |

- Cambio de tono: `FIRMA_SIN_CONFIRMAR` pasa de `pending` (naranja = alerta) a `info` (azul = en proceso), porque no requiere ninguna acción (regla 5 de estados semánticos).
- Para el SA, los títulos `NO_LISTO` y `SUSPENDIDO` llevan además, después del texto, un enlace «Ver en el backoffice» a `/backoffice/empresas/{id}`.

### 4.4 Campana — literales

| `tipo` | Título | Mensaje | Enlace |
|---|---|---|---|
| `LOTE_TERMINADO` | Lote terminado | Ya está el resultado de cada placa del lote. | Ver lote (con `sr-only` «: Lote terminado ({fecha})», como hoy) |
| desconocido | Sin clasificar | Abre el lote para ver el detalle. | Ver lote |

Estados del panel (sin cambio): «Cargando avisos…», «No tienes avisos», «No se pudieron cargar los avisos» + «El resto de Impuestos sigue funcionando.» + «Reintentar».

### 4.5 403 — literales definitivos (`app/403/page.tsx`)

Se mantiene la palabra «empresa» en esta página por coherencia con la 403 de Diagnóstico (regla 6). Unificar el término con «compañía» es un hueco de suite (H-10) y se haría en los dos productos a la vez.

| `code` | Mensaje |
|---|---|
| `PRODUCTO_NO_HABILITADO`, `PRODUCT_NOT_ENABLED` | Impuestos no está habilitado para tu empresa. |
| `PRODUCTO_SUSPENDIDO` | Impuestos está suspendido para tu empresa. Pídele ayuda a tu administrador. *(sin cambio)* |
| `SIN_PERMISOS_IMPUESTOS`, `PRODUCT_ROLE_REQUIRED` | No tienes un rol en Impuestos. Pídele acceso a tu administrador. *(sin cambio)* |
| `SIN_EMPRESA` | Tu usuario no tiene una empresa asociada. Pídele ayuda a tu administrador. *(sin cambio)* |
| `SESSION_INVALID` | Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión. *(sin cambio)* |
| (otro) | No tienes permiso para abrir esta página. *(sin cambio)* |

Título «No tienes acceso» y acción «Ir a mis productos» → `{hubUrl}/?inicio=1`: sin cambio. **Es CONFORME tal como está**; esta tabla solo confirma los textos.

Mensajes del shell (sin cambio): «Abriendo Impuestos…» y «No se pudo abrir Impuestos» + «Reintentar».

---

## 5. Huecos del design system y componentes faltantes

Ninguno se crea en esta ola sin encargo (modo D). Para cada hueco se indica dónde debería vivir.

| # | Falta | Dónde | Recomendación para esta ola |
|---|---|---|---|
| H-1 | **Gráficas** (`Figura`, `Leyenda`, `BarrasSerie`, `ListaBarras`, `TablaAlternativa`): hoy son locales de Diagnóstico y M17 las necesita | `packages/ui` (`@flit/ui/graficas/*`) | **(a, recomendada)** ui-agent en modo D las extrae a `packages/ui` como export nuevo, sin tocar Diagnóstico (que migra después), antes de M17. **(b)** Si el orquestador no quiere tocar `packages/ui` en este PR, copia local en `frontend-impuestos/components/estadisticas/graficas/` con la deuda registrada. Con un segundo consumidor ya justifica ser compartido (regla 6). |
| H-2 | Recetas de **tarjeta de sección** y **tabla por defecto** (`TARJETA_CLS`, `CONTENEDOR_TABLA_CLS`…) | `packages/ui` (`table-styles.ts` + un `TARJETA_SECCION_CLS`) | Copia local (§0.2) en esta ola. Se extrae junto con H-1 si se elige la opción (a). |
| H-3 | Clave **`impuestos`** en `MAPA_ICONO` (para `CabeceraModulo icono` / `IconoModulo clave="impuestos"` del ProductosTab) | `packages/ui/src/iconos-mapas.ts` (aditivo: `"impuestos": "pesos"`) | Cambio de una línea en M19. Sin él, `IconoModulo clave="impuestos"` cae a su respaldo y la cabecera usa `icono="pagos"`. |
| H-4 | Ilustración `modulo/impuestos.svg` del kit | Diseño (activo gráfico) en `public/assets/kit/modulo/` de las 4 apps | Usar `modulo/pagos` mientras llega. |
| H-5 | Glifo **recibo** (`receipt` de la DDL) | `packages/ui/src/iconos-kit.ts` (Diseño) | Mapear `receipt` → `pesos` en `apps.ts` (M19). |
| H-6 | `SearchableSelect`: el botón «Quitar selección» mide 22 px (WCAG 2.5.8) | `packages/ui/src/SearchableSelect.tsx:234` (`min-w-6 min-h-6`) | Parche local por `className` (§4); la corrección va a modo D. |
| H-7 | **Rango de fechas** del kit (un campo con calendario y «Confirmar») | `packages/ui` | Par de `input type="date"` como en Diagnóstico. |
| H-8 | `CampoClave` tiene fijo el nombre accesible «Mostrar contraseña» y estilos de ingreso | `packages/ui/src/Ingreso.tsx:202` (prop `etiquetaMostrar`) | No usarlo para la API key: patrón local de `CredencialImpuestoDiagnostico` (input `password` + `Button ghost` «Mostrar API key»). |
| H-9 | Contrato: no hay `POST /notificaciones/leidas` («Marcar todas como leídas» del molde) | `contracts/openapi/impuestos.v1.yaml` (backend o architecture) | Se omite el botón. |
| H-10 | Glosario de suite: «empresa» frente a «compañía» en las 403 | design system de suite | Mantener «empresa» solo en las 403 hasta unificar. |
| H-11 | `ChipFiltro` sin prop `tono` (Diagnóstico lo pinta por `ReactNode`) | `packages/ui/src/ChipsFiltro.tsx` | Mismo arreglo local que `MetricasLote.tsx:39`. |
| H-12 | Campo de **lista de correos** | `packages/ui` (si un tercer producto lo pide) | `textarea` local, un correo por línea (§3.9). |
| H-13 | `docs/suite/design-system.md` no existe en la rama | ui-agent modo A | Esta spec cita como fuente `packages/ui` + el molde de Diagnóstico. |

**Componentes nuevos locales justificados** (en `frontend-impuestos`, no compartidos):
- `ChipEstado`: mapa de §1 sobre `etiquetaKit` y `StatusBadge`.
- `MontoTotal`, `TablaVigencias` y `ListaRecibos`: son de dominio.
- `DialogoCerrarLote` y `DialogoRelanzar`: son `Modal` compuestos.
- `FormularioCredenciales`.

Ninguno reemplaza a un export existente.

---

## 6. Checklist de auditoría por módulo (para los encargos M16–M19)

Se marca cada punto con evidencia (`archivo:línea` o captura). Un punto en rojo de **[B]** bloquea. El resto son mejoras.

1. **[B] Fuente única:**
   - Solo exports de `@flit/ui`, `@flit/shell` y `@flit/brand`, más las recetas de §0.2.
   - Ningún import de `frontend-diagnostico`.
   - Ningún token `--dx-*`.
   - Cero `#hex` o `rgb()` en `components/**` y `app/**` (fuera de `themeColor`).
2. **[B] Reutilizar:** cada pieza usa el export que nombra la spec (nombre exacto). Un componente nuevo lleva su justificación en el encargo o en esta spec.
3. **[B] Cuatro estados** por pantalla y por bloque aislado, con test, y con los literales de esta spec. Un `null` de monto dice «Sin dato» y nunca `0`, con test en `formato.test.ts`.
4. **[B] Estados semánticos:**
   - Los chips salen del mapa de §1 (`lib/liquidaciones/estados.ts`), con texto y punto o ícono.
   - Un código desconocido dice «Sin clasificar».
   - En tablas, `etiquetaKit` sin `role="status"`.
5. **[B] Permisos en la UI:**
   - Gestor sin Usuarios ni Configuración y sin «Cerrar lote».
   - Selector y columna «Compañía» solo para el SA.
   - Contractual solo editable por el SA.
   - Exportar solo con `reportes.exportar` y Descargar PDF solo con `recibos.descargar`.
   - Se verifica en el DOM (no está), no solo deshabilitado.
6. **[B] PII:**
   - Sin dirección, correo ni teléfono del propietario.
   - `validations[].sent` nunca se pinta.
   - Placeholders en tests y E2E (`ABC123`, `CC 0000000`, «Compañía Demo»).
   - El aviso de Ley 1581 está en Reportes.
   - Las credenciales no se vuelven a mostrar y se limpian al guardar.
7. **[B] Accesibilidad:**
   - Foco visible en todo control.
   - Teclado completo: menú de acciones, pestañas, embudo y diálogos con trampa de foco, Escape y foco de vuelta.
   - `label` visible en cada campo.
   - Errores con `aria-invalid` y `aria-describedby`.
   - Botón deshabilitado con el motivo visible y asociado.
   - Gráficas con `figcaption` y datos en texto.
   - Objetivos de ≥ 24 px.
8. **Diálogos:**
   - `Modal` de `@flit/ui` con `closeOnBackdrop={false}` si tiene campos.
   - Foco inicial en «Cancelar» en las acciones destructivas.
   - Acción destructiva con `variant="alerta"`.
   - Errores dentro del diálogo, que no se cierra.
   - Una sola acción primaria por vista.
9. **Responsive:**
   - 360, 768 y 1280 px sin desbordar la página.
   - Las tablas se apilan como tarjetas por debajo de 1024 px (`TABLA_MOVIL_*` con `data-label`).
   - La barra con el selector y la campana cabe en 360 px.
10. **Coherencia de suite:**
    - Mismo patrón que el molde citado en cada pantalla (§3).
    - Vocabulario de §0.1 («compañía», «Sin dato», roles con su nombre).
    - Formatos es-CO.
    - `?empresa=` propagado en peticiones y enlaces.
11. **Claro y oscuro:** se revisan los dos temas (todos los tokens tienen `.dark`). Ningún texto queda por debajo de 4,5:1.
12. **Evidencia:** lista de archivos revisados, veredicto (Aprobado / Aprobado con observaciones / No aprobado) y huecos nuevos agregados a §5.
