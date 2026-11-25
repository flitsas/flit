# Spec visual · Producto Impuestos (frontend-impuestos + piezas en frontend/)

> Generado: 2026-11-24 · `ui-agent` modos B (spec) y C (auditoría de M15) · rama `feature/AB-14694-producto-impuestos` (HEAD 7a8502863)
> Revisado: 2026-11-25 (HEAD 5860c5e40) · alineada con los contratos `impuestos.v1.yaml` y `platform.v1.yaml` 1.1.0 tras M16, M18 y M19 (§0 «Precedencia del contrato») y cerrado el dashboard M17 (D-UI-GRAFICAS).
> Revisado: 2026-11-25 (HEAD 3396655bc) · alineada con `impuestos.v1.yaml` 1.1.0 (deltas 4 a 7) y con el front ya integrado: chip y enlace de la fila `INDIVIDUAL`, motivo del cierre, Super Admin sin compañía en Liquidaciones, filtros y fila protegida de Usuarios, columna «Usuarios» y compañía identificada en el backoffice. Lo que espera al contrato 1.2.0 va marcado «en 1.2.0».
> Revisado: 2026-11-25 (HEAD cfffcab89) · alineada con `impuestos.v1.yaml` 1.2.1 y `platform.v1.yaml` 1.1.1: lo marcado «en 1.2.0» quedó **entregado** (Reintentar en `LISTO_SIN_WEBHOOK`, motivos nuevos, `CLAVE_ROTADA` en el historial, línea de `modoRespaldoFirma`, enlace «Ver la original»); H-14 y H-17 cerrados; literal nuevo de `pantalla.avisoRotacionCredenciales`.
> Insumo del `frontend-agent` para M16–M19 y del `ui-agent` para auditarlos. HU FE #14695.
> Plan de referencia: `docs/impuestos/plan-implementacion.md` §3 (estados), §5.1 (contrato), §6 (rutas), §9 M15–M19. Decisiones: `.claude/state/producto-impuestos/decisiones-usuario.md` (D-DASH, D-REPORTES, D-CONFIG, D-GESTOR, D-ROL-ADMIN, DA-6b, DA-7, DA-9).

---

## 0. Fuente de diseño y reglas

- **Fuente que rige:** los tokens y componentes de `packages/ui` (`src/styles/tokens.css` y exports de `@flit/ui`), que ya implementan el Manual UI Kit 2026, y la forma en que `frontend-diagnostico` los compone (molde de este producto). Las specs previas de Diagnóstico (`.claude/state/diagnostico-ola-ux-dashboard/spec-ui.md`) siguen la misma fuente. **`docs/suite/design-system.md` no existe en esta rama** (se sacó en el commit 16766b438); el PDF v4 heredado de `flit-design-guardian` no se mezcla con esta fuente en ninguna pantalla de Impuestos.
- **Copiar la forma, no importar el código.** `frontend-impuestos` no importa nada de `frontend-diagnostico` (lo vigila `__tests__/independencia.test.ts`). Cuando el molde usa recetas locales de Diagnóstico (`components/configuracion/estilos.ts`, `components/tablero/vistasDetalle/comun.tsx`), Impuestos las reproduce en `frontend-impuestos/components/comun/estilos.ts` (§0.2) y la duplicación queda reportada como hueco (§5).
- **Tokens o nada.** Cero `#hex` o `rgb()` en clases y en `style`. Solo se permiten `var(--di-*)`, `var(--kit-*)`, `var(--badge-*)`, `var(--chart-1..5)`, `var(--table-*)`, `var(--overlay)`, `var(--estado-*-fg)` y las utilidades `bg-card`, `text-flit-primary`, `border-border`, `text-muted-foreground` y `ring`. Todas tienen valor `.dark`. **Prohibido `--dx-*`**, que es exclusivo de Diagnóstico. Impuestos **no crea tokens propios**: no hace falta ninguno (§1). El único hex que se tolera es `themeColor` en `app/layout.tsx`, porque es metadato del navegador, igual que en Diagnóstico.
- **Precedencia del contrato (decisión del orquestador, 2026-11-25).** Ante un choque de literales mandan `contracts/openapi/impuestos.v1.yaml` (`x-flit-literales.errores`, `x-flit-literales.pantalla` y `x-flit-etiquetas` de cada enum) y `contracts/openapi/platform.v1.yaml` 1.1.0 (`x-flit-etiquetas` y `message` del aprovisionamiento). La UI pinta esos textos tal cual y **no redacta otros** para esos valores. Esta spec decide solo lo que el contrato no fija: tono del chip, componente, jerarquía, ayudas, títulos de sección y textos de los cuatro estados. Si una tabla de esta spec y el contrato discrepan, la auditoría toma el contrato y no lo marca como desviación. Un error de API se muestra con el campo `error` de la respuesta; si falta, con el literal del contrato para su `code`; y solo si el `code` es desconocido, con el texto genérico de cada pantalla.
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

Literales = `EstadoLiquidacion.x-flit-etiquetas` (coinciden con la tabla).

El tipo (`INDIVIDUAL` «Individual», `MASIVA` «Masiva», según `TipoLiquidacion.x-flit-etiquetas`) es **texto plano**, no chip, como la modalidad Express/Masivo de `ListadoDiagnosticos.tsx:54`.

### 1.2 Ítem / placa (`liquidacion_items.estado`)

Literal = `EstadoItem.x-flit-etiquetas`; en `EXITOSO`, `ResultadoStatus.x-flit-etiquetas`. El tono es de esta spec.

| Código | Literal (contrato) | Tono | Nota |
|---|---|---|---|
| `PENDIENTE` | Pendiente | `info` | |
| `CONSULTANDO_RUNT` | Consultando RUNT | `info` | |
| `REINTENTO_PROGRAMADO` | Reintento programado | `purple` | |
| `ENVIANDO` | Enviando | `info` | |
| `ENVIADO` | Esperando resultado | `info` | |
| `INCIERTO` | Incierto | `purple` | Detalle: `pantalla.detalleIncierto` |
| `INCIERTO_REQUIERE_DECISION` | Incierto — requiere decisión | `warning` | Permite relanzar |
| `DATOS_INCOMPLETOS` | Datos incompletos | `warning` | Junto al estado, la etiqueta de `CampoFaltante` |
| `DEPARTAMENTO_NO_HABILITADO` | Departamento no habilitado | `neutral` | Junto al estado, el nombre del departamento |
| `EXITOSO` + `paz_y_salvo` | Paz y salvo | `success` | El chip muestra el **resultado** |
| `EXITOSO` + `con_deuda` | Con deuda | `warning` | El estado de la liquidación es exitoso; el chip muestra el **resultado** |
| `EXITOSO` sin resultado | Exitosa | `success` | |
| `ERROR` | Con error | `danger` | Clase de error (§1.3) junto al estado |
| `SIN_RESULTADO_LOTE_CERRADO` | Sin resultado (lote cerrado) | `neutral` | |

Marcas adicionales: chips pequeños junto al estado, siempre con texto (`x-flit-literales.pantalla`).

| Marca | Literal | Tono |
|---|---|---|
| `parcial` | Parcial (`marcaParcial`) | `purple` |
| `posibleDuplicado` | Posible duplicado (`marcaPosibleDuplicado`) | `warning` |
| `resultado_tardio` | Resultado tardío (`marcaResultadoTardio`) | `info` |
| período `no_liquidable` | El portal no permite liquidar esta vigencia (`Periodo.x-flit-etiquetas.noLiquidable`) | `neutral` |

### 1.3 Clase de error (`errorClase`)

| Código | Literal (`ClaseError.x-flit-etiquetas`) |
|---|---|
| `CORREGIR_DATOS` | Corregir datos |
| `DEPENDE` | Depende del mensaje |
| `REINTENTABLE` | Reintentable como nueva consulta |

En el detalle de la placa (§3.6), el aviso de `ERROR` lleva la clase como título y **`errorMensaje`** como cuerpo: el contrato lo define como «texto del proveedor para el usuario» (`result.error.message`). Se pinta como texto plano (nunca HTML) y, si llega vacío, con el respaldo «El proveedor no pudo liquidar esta placa.». El Super Admin ve además «Código: {errorCodigo}» en `TEXTO_SECUNDARIO_CLS`.

### 1.4 Recibo (`recibos.estado`)

| Código | Literal (`EstadoRecibo.x-flit-etiquetas`) | Tono | Acción |
|---|---|---|---|
| `GUARDADO` | Disponible | `success` | «Descargar recibo» (`pantalla.accionDescargarRecibo`) |
| `PENDIENTE` | Descargando | `info` | — |
| `SIN_ARCHIVO` | Sin recibo | `neutral` | — |
| `PENDIENTE_REINTENTO_MANUAL` | No disponible | `warning` | — (lo resuelve soporte) |

Bajo el chip va el motivo de `MotivoRecibo.x-flit-etiquetas`; con `SIN_FILE_ID`, «Sin recibo: {razón}». El nombre del recibo sale de `TipoRecibo.x-flit-etiquetas` («Recibo de vigencia {periodos}», «Recibo consolidado», «Paz y salvo»).

### 1.5 Aprovisionamiento (backoffice de Impuestos y `EstadoAprovisionamiento` en `frontend/`)

- **Chip:** literal de `ProvisioningStatus.x-flit-etiquetas` (`platform.v1.yaml`) = `EstadoAprovisionamiento.x-flit-etiquetas` (`impuestos.v1.yaml`). En `frontend/` (ProductosTab) es siempre `StatusBadge` con `icono` de `ICONO_TONO` por tono, como el resto del tab. En el backoffice de Impuestos, `PENDIENTE` va con `EtiquetaEnProceso` (azul, mismo tono `info` y mismo literal), igual que el lote `EN_PROCESO` del producto; el resto, `etiquetaKit` en tabla y `StatusBadge` en el detalle. Es la misma semántica con el componente de cada contexto, no un patrón distinto.
- **Motivo legible:** en `frontend/`, el campo `message` del aprovisionamiento tal cual (el servicio ya pone el literal de `MotivoAprovisionamiento`); en el backoffice de Impuestos, la etiqueta de `MotivoAprovisionamiento` para `motivo`. Sin motivo, no se pinta línea.
- **Acción:** literal de `ProvisioningAction.x-flit-etiquetas`; la decide `action` del contrato, no el front.

| Código | Chip | Tono | Acción (si `action` la trae) |
|---|---|---|---|
| `PENDIENTE` | Pendiente | `info` | — |
| `LISTO` | Listo | `success` (+ chip `purple` «Firma sin confirmar» si `signatureConfirmed=false`) | — |
| `LISTO_SIN_WEBHOOK` | Listo sin webhook | `success`, **sin** chip extra: el literal ya lo dice | Reintentar (**entregado en 1.2.0**: `action` = `REINTENTAR`; `CARGAR_SECRETO` retirada, y si un reporte viejo la trae la UI la trata como «Reintentar») |
| `ERROR` | Error | `danger` | Reintentar |
| `INCIERTO` | Incierto | `purple` | Reintentar |
| `REQUIERE_CREDENCIALES` | Requiere credenciales | `warning` | Cargar credenciales |
| `REQUIERE_REVISION_NIT` | Requiere revisión del NIT | `warning` | Corregir NIT |
| `SUSPENDIDO` | Suspendido | `neutral` | — |
| desconocido | Sin clasificar | `neutral` | — |

`ERROR` ya no se parte en «datos» y «ambiente» en el chip: lo distingue el motivo (`ALTA_DATOS_RECHAZADOS` frente a `AMBIENTE_NO_CONFIGURADO`). La marca `modoRespaldoFirma` del backoffice sigue como chip `purple` «Respaldo de firma» en el listado; en el detalle, la tarjeta «Estado» añade la línea con su etiqueta del contrato 1.2.0 («Respaldo de firma: los resultados llegan por consulta mientras se revisa el secreto.»). H-14 cerrado.

**Entregado en 1.2.0** (plan §5.8, contrato aditivo): `MotivoAprovisionamiento` suma `EMPRESA_INACTIVA_EN_PROVEEDOR` y `CLAVE_ROTADA`, y `EMPRESA_YA_REGISTRADA` y `ALTA_SIN_CONFIRMAR` quedan retirados (su literal se conserva para el historial). La UI no cambió de forma: pinta la etiqueta del contrato para los motivos nuevos y los retirados, y «Sin clasificar» solo para un código que no esté en `x-flit-etiquetas`.

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

**Gráficas (D-UI-GRAFICAS, decisión cerrada):** se **copian localmente** en `frontend-impuestos/components/estadisticas/` (`Figura.tsx`, `BarrasSerie.tsx`, `ListaBarras.tsx`, `TablaAlternativa.tsx`, `RegionDesplazable.tsx`), con la forma del molde y cabecera de comentario que cite D-UI-GRAFICAS. **No** se mueven a `packages/ui` en esta ola ni se importan de Diagnóstico (lo vigila `independencia.test.ts`). La copia conserva: `<figure aria-labelledby>` con `figcaption` visible, leyenda en texto, «Ver datos» con la tabla alternativa, colores solo `--chart-1..5` y `prefers-reduced-motion`. La deuda queda en H-1.

**Literales del contrato** (`DashboardImpuestos.x-flit-etiquetas`): etiquetas de KPI («Liquidaciones», «Lotes en proceso», «Exitosas», «Con error», «Placas con deuda», «Placas al día», «Inciertas»…), «Monto total liquidado», `pantalla.liquidacionesSinMonto` y `pantalla.marcaParcial`. Lo que el contrato no nombra («Pendientes de resultado», títulos de las gráficas) es de esta spec.

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
  - `liquidacionesSinMonto > 0`: pie con `pantalla.liquidacionesSinMonto` («{n} liquidaciones sin monto») y la marca «Parcial».
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
│ [Placa ____] [Liquidar placa]            ││ [Carga masiva] [Descargar plantilla]            │
└──────────────────────────────────────────┘└─────────────────────────────────────────────────┘
[ Todas ][ Masivas ][ Por placa ]   ← Pestanas (?tipo=)
[Estado ▾] [Desde ▢] [Hasta ▢] [Buscar placa ___]                           [Limpiar filtros]
┌ tabla por defecto ────────────────────────────────────────────────────────────────────────┐
│ ID · Tipo* · Creada · Placas · Avance · Estado · Creada por · (acción)                    │
└───────────────────────────────────────────────────────────────────────────────────────────┘
Pagination
* Tipo solo en «Todas»
```

- **Super Admin:** el grid y la creación son de la compañía de `?empresa=` (contrato 1.1.0, uso único de `?empresa`). Sin compañía elegida no se pide nada a la API: en el bloque de la tabla va `EligeCompania` (`EstadoVacio` con `icono` de compañías y título = literal `EMPRESA_REQUERIDA`, «Elige una compañía.»), y «Liquidar placa», «Carga masiva» y «Descargar plantilla» quedan deshabilitados con `aria-describedby` hacia ese mismo texto. No hay vista global de liquidaciones, así que el grid no lleva columna «Compañía».
- **Errores de compañía** (`ERRORES_EMPRESA`: 400 `EMPRESA_REQUERIDA`, 404 `EMPRESA_NO_ENCONTRADA`, 403 `EMPRESA_SOLO_SUPER_ADMIN` y su alias retirado `EMPRESA_NO_PERMITIDA`): rechazan la petición, no el acceso. Van en línea, en el `InlineAlert` del bloque, con su literal del contrato; **nunca** llevan a la 403 ni a la 404. Solo un 403 de acceso (otro `code`) lleva a `/403?code=`.

- Tarjetas de creación: solo con `liquidaciones.crear`. Con el banner de suspensión o de «no listo» activo (§4.3), los botones quedan deshabilitados con `aria-describedby` hacia el banner.
  - «Liquidar placa» (`pantalla.accionLiquidarPlaca`): `BOTON_PRIMARIO_CLS`. El título de la tarjeta sigue siendo «Liquidar por placa». Lleva a `/liquidaciones/nueva?placa={placa}` o, si se implementa inline, hace el POST directo con los errores de §3.3.
  - «Carga masiva» (`pantalla.accionCargaMasiva`): `BOTON_PRIMARIO_CLS` con `Icono "reporte-detallado"`.
  - «Descargar plantilla» (`pantalla.accionDescargarPlantilla`): `BOTON_SECUNDARIO_CLS` con `Icono "nube-bajar"` sobre `GET /liquidaciones/plantilla`.
- Pestañas: `Pestanas` de `@flit/ui/Pestanas` con `ariaLabel="Tipo de liquidación"`: «Todas» (por defecto, cumple F3-CF6 con la columna Tipo), «Masivas» (`tipo=MASIVA`) y «Por placa» (`tipo=INDIVIDUAL`). La activa va en la URL.
- Filtros: `select` nativo «Estado» con `CAMPO_CLS` y las opciones de §1.1, fechas como en §3.1 y búsqueda «Buscar placa» (`placa=`, en mayúscula). «Limpiar filtros» es `Button variant="ghost" size="sm"` y solo aparece con algún filtro activo. Cada cambio vuelve a la página 1.
- Celdas:
  - ID: `idCorto` + enlace (la fila entera también navega). Una `MASIVA` lleva a `/liquidaciones/{id}`; una `INDIVIDUAL`, directo a `/liquidaciones/{id}/items/{itemId}` con `ResumenLiquidacion.itemId` (1.1.0). Solo si una respuesta vieja no trae `itemId`, a la del lote, que redirige.
  - Placas = `totalItems`.
  - Avance: `BarraProgreso` de `h-2` + «{terminadas}/{total}».
  - Tipo: `TipoLiquidacion.x-flit-etiquetas` («Individual» / «Masiva»). Las pestañas siguen diciendo «Por placa» y «Masivas» (texto de navegación de esta spec, no etiqueta de enum).
  - Estado: chip de §1.1. En `INDIVIDUAL` se muestra el chip del ítem (§1.2) a partir de `ResumenLiquidacion.estadoItem` (1.1.0, delta 4); en `EXITOSO`, el resultado («Con deuda» / «Paz y salvo») sale de `contadores`, porque el resumen no trae `resultadoStatus`. Los `contadores` solos son el respaldo para una respuesta sin `estadoItem` (`chipFila`; H-15 cerrado).
  - Creada por: `creadaPor.nombre`.
- Paginación: `Pagination` de `@flit/ui/Pagination` (`noun="liquidaciones"`, tamaños de `usePaginacion`).

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando liquidaciones…"` en el bloque de la tabla. Las tarjetas siguen usables. |
| Vacío (sin datos) | `EstadoVacio` «Aún no hay liquidaciones» · «Liquida una placa o sube un archivo para empezar.» · [«Liquidar por placa»]. |
| Vacío (por filtro) | `EstadoVacio` «Ninguna liquidación coincide con los filtros» · «Prueba con otro rango o quita algún filtro.» · [«Limpiar filtros»]. |
| Error | `InlineAlert tone="error" title="No se pudieron cargar las liquidaciones"` con el `error` del contrato (§0) + «Reintentar» (`BOTON_REINTENTAR_CLS`). |
| Lleno | La tabla paginada. |

### 3.3 Liquidación por placa — `/liquidaciones/nueva` (M16)

**Molde:** `frontend-diagnostico/components/nuevo/CreacionExpress.tsx` + `components/express/CapturaExpress.tsx`.

- `CabeceraModulo` «Liquidar por placa», con miga «Liquidaciones › Liquidar por placa».
- Una `TARJETA_CLS` con un `<form noValidate>`:
  - `label` «Placa» + `input` con `CAMPO_CLS uppercase tracking-[0.08em]`, `autoComplete="off"`, `maxLength={10}` (regla de `PLACA_INVALIDA`: de 4 a 10 letras o números, ya normalizada) y `inputMode="text"`. Se precarga desde `?placa=`.
  - Ayuda (`AYUDA_CLS`): «Solo la placa: los datos del vehículo y del propietario se consultan en el RUNT.»
  - CTA `Button` primario «Liquidar placa» (`cargando` mientras envía) y `Button variant="secondary"` «Cancelar», que vuelve a `/liquidaciones`.
- Si llega 201, navega a `/liquidaciones/{id}/items/{items[0].id}`.
- Super Admin sin `?empresa=`: CTA deshabilitado con el literal `EMPRESA_REQUERIDA` («Elige una compañía.») en `aria-describedby`, sin llamar a la API. Los errores de compañía (§3.2) van en el mismo aviso con su literal.

| Estado | Contenido |
|---|---|
| Vacío | Formulario con el campo enfocado al cargar. |
| Cargando | CTA con `aria-busy` y «Creando la liquidación…». Campo `disabled`. |
| Error | Textos = `x-flit-literales.errores` (§0). 422 `PLACA_INVALIDA` junto al campo con `aria-invalid` y `aria-describedby` · 409 `APROVISIONAMIENTO_NO_LISTO` y `PRODUCTO_SUSPENDIDO` en `InlineAlert warning` · Otro, `InlineAlert error` «No se pudo crear la liquidación» + «Reintentar». No se pierde lo digitado. |
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
  - Tabla de inválidas: Fila · Valor · Motivo (`MotivoFilaInvalida.x-flit-etiquetas`).
  - Con `superaTope`: `InlineAlert tone="warning"` «El archivo tiene {validas} placas válidas y el máximo por lote es {tope}. Divide el archivo para continuar.». CTA deshabilitado con ese texto en `aria-describedby`.
  - «Subir otro archivo» (secundario).
- **Paso 3:** CTA primario «Iniciar liquidación de {validas} placas». Con 201 navega a `/liquidaciones/{id}`.

| Estado | Contenido |
|---|---|
| Vacío | Paso 1 sin archivo. |
| Cargando | «Analizando el archivo…» (`VehicleLoader` en la tarjeta) / «Creando el lote…» en el CTA con `aria-busy`. |
| Error | Textos = `x-flit-literales.errores` (§0): `ARCHIVO_DEMASIADO_GRANDE`, `FORMATO_NO_SOPORTADO`, `COLUMNA_PLACA_AUSENTE`, `ARCHIVO_SIN_PLACAS`, `LIMITE_SUPERADO` (con `{tope}`) · 409, como en §3.3 · Otro: «No se pudo analizar el archivo» + «Reintentar» sobre el mismo archivo. |
| Lleno | Resumen del paso 2 y su tabla. |

### 3.5 Detalle del lote — `/liquidaciones/[id]` (M16): avance, cierre manual y relanzar

**Molde:** `frontend-diagnostico/components/tablero/TableroLote.tsx`, `CabeceraLote.tsx`, `EstadoGlobal.tsx` y `MetricasLote.tsx`.

```
┌ CabeceraModulo: miga «Liquidaciones › Lote {idCorto}» · «Liquidación masiva» ─── [chip estado] [Cerrar lote]* ┐
│  «ID {idCorto} · Creada el dd/mm/aaaa hh:mm · Por {nombre} · {Compañía}**»                                     │
└─────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌ Avance del lote (TARJETA) ───────────────────────────────────────────────────────────────────────────────────────┐
│ BarraProgreso «{t} de {n} placas terminadas» · «Se actualiza solo mientras el lote está en proceso.»            │
│ Embudo ChipFiltro: Todas · Exitosas · Con error · Datos incompletos · Departamento no habilitado · Sin resultado │
│ Solo cifra (dl): Con deuda · Paz y salvo · Inciertas · Pendientes                                              │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌ Placas del lote (TARJETA) [Buscar placa ___] ────────────────────────────────────────────────────────────────────┐
│ Placa · Propietario · Documento · Departamento · Estado · Total a pagar · Acciones                              │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
* Admin y SA, con estado CREADO o EN_PROCESO      ** solo SA
```

- Embudo: `ChipFiltro` de `@flit/ui/ChipsFiltro` dentro de `EMBUDO_CLS` (`role="group" aria-label="Filtros del lote"`), exclusivos y con `aria-pressed`. Cifra 0 en neutral. Filtra `?estado=`.
  - **Solo son chips los contadores que corresponden a un único `EstadoItem`:** «Todas» (`totalItems`), `exitosas`→`EXITOSO`, `error`→`ERROR`, `datosIncompletos`→`DATOS_INCOMPLETOS`, `departamentoNoHabilitado`→`DEPARTAMENTO_NO_HABILITADO` y `sinResultado`→`SIN_RESULTADO_LOTE_CERRADO`. Un chip que no filtra exactamente lo que cuenta rompería la promesa del embudo.
  - `conDeuda` y `pazYSalvo` (resultado, no estado), `incierto` (dos estados) y `pendientes` (seis estados) van como cifras de solo lectura en una `dl` bajo el embudo.
  - Etiquetas = `Contadores.x-flit-etiquetas`.
- Sondeo mientras está `EN_PROCESO`: cada 5 s, se duplica si nada cambia hasta 30 s y se detiene al pasar a terminal. Si falla, `InlineAlert tone="warning"` «Datos desactualizados desde hh:mm» + «Reintentar», sin borrar lo leído.
- `CERRADO_MANUALMENTE`: `InlineAlert tone="info" vivo={false}` «Cerrado manualmente el {fecha} por {nombre}.». Sin `cerradaPor`, o con `cerradaPor.nombre` vacío (un actor sin nombre cuenta como sin actor), «Cerrado manualmente el {fecha}.» (nunca «por .»). Si `Liquidacion.motivoCierre` (1.1.0) trae texto, va una segunda línea «Motivo: {texto}» en texto plano (nunca HTML), recortado; vacío o `null`, sin línea (H-16 cerrado).
- Acciones por fila: `ActionsMenu` de `@flit/ui` (`ariaLabel="Acciones de la placa"`, descrito por la celda Placa) con «Ver detalle» y «Relanzar» (`pantalla.accionRelanzar`; solo en `INCIERTO_REQUIERE_DECISION` o `ERROR` `REINTENTABLE`, con `liquidaciones.crear`). La placa también es enlace al detalle.
- **`DialogoCerrarLote`** (`Modal` de `@flit/ui/Modal`, `size="md"`, `icono="alerta"`, `closeOnBackdrop={false}`, `busy` mientras envía):
  - Título «Cerrar lote».
  - Cuerpo: `pantalla.confirmarCerrarLote`.
  - Campo `textarea` «Motivo (opcional)», con `maxLength` **300** (el del contrato en `POST /liquidaciones/{id}/cerrar`) y ayuda «Hasta 300 caracteres.» asociada por `aria-describedby`.
  - Pie: `Button variant="secondary"` «Cancelar» (foco inicial) + `Button variant="alerta"` «Cerrar lote».
  - Errores dentro del diálogo, que no se cierra: 409 `YA_TERMINADA` con su literal del contrato; otro, «No se pudo cerrar el lote. Intenta de nuevo.».
- **`DialogoRelanzar`** (`Modal size="md"`, `icono="reintento"`, `closeOnBackdrop={false}`):
  - Título «Relanzar la placa {PLACA}».
  - Cuerpo: `pantalla.confirmarRelanzar`.
  - Casilla obligatoria «Entiendo que puede quedar duplicada» (`MARCA_CLS`).
  - Pie: «Cancelar» + `Button` primario «Relanzar», deshabilitado hasta marcar la casilla y con el motivo visible.
  - Con 201 navega al ítem nuevo. 409 `NO_RELANZABLE` y 422 `CONFIRMACION_REQUERIDA`: literal del contrato, dentro del diálogo.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando el lote…"`. |
| Vacío | Lote sin ítems todavía (`CREADO`): `EstadoVacio` «Este lote todavía no tiene placas en proceso» · «Se actualiza solo en unos segundos.». Por filtro: «Ninguna placa coincide con el filtro» + «Quitar filtro». |
| Error | 404 de recurso → `notFound()`. Un error de compañía (§3.2), aunque sea 404 `EMPRESA_NO_ENCONTRADA`, va en línea con su literal. Otro: `InlineAlert error` «No se pudo cargar el lote» + «Reintentar». Super Admin sin `?empresa=`: `EligeCompania` (§3.2) en lugar de la pantalla. |
| Lleno | Lo descrito arriba. |

### 3.6 Detalle de la placa (ítem) — `/liquidaciones/[id]/items/[itemId]` (M16): montos y recibos

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
  - `ERROR`: `error`, con título = etiqueta de la clase (§1.3; sin clase, «El proveedor no pudo liquidar esta placa») y cuerpo = `errorMensaje` en texto plano, con el respaldo de §1.3 si llega vacío.
  - `DATOS_INCOMPLETOS`: `warning`, «Faltan datos del RUNT para liquidar: {campo}.», con `{campo}` = `CampoFaltante.x-flit-etiquetas`.
  - `DEPARTAMENTO_NO_HABILITADO`: `info`, «El departamento {nombre} no está habilitado en el contrato de tu compañía.»
  - `INCIERTO`: `pending`, `pantalla.detalleIncierto`.
  - `INCIERTO_REQUIERE_DECISION`: `warning`, «No se pudo confirmar si el proveedor recibió la solicitud. Puedes relanzarla; si sí la recibió, puede quedar duplicada.»
  - `SIN_RESULTADO_LOTE_CERRADO`: `info`, «El lote se cerró antes de recibir el resultado de esta placa.»
  - `resultado_tardio`: `info`, «Este resultado llegó después de cerrar el lote.»
  - `posibleDuplicado`: `warning`, «Esta liquidación es un relanzamiento: puede estar duplicada en el proveedor.». **Entregado en 1.2.0:** enlace «Ver la original» a `/liquidaciones/{relanzadoDesdeLiquidacionId}/items/{relanzadoDesdeItemId}` (con `?empresa` si el Super Admin la trae); si falta alguno de los dos ids, no hay enlace. H-17 cerrado.
- Vehículo y propietario (`ListaDefinicion`, `columnas={2}`, `vacio="Sin dato"`): Placa (`Placa`), Departamento, Secretaría, Propietario y Documento. Secretaría = `Item.secretaria` (1.1.0, delta 5) como «{nombre}, {ciudad}»; con uno solo de los dos, ese; sin ninguno, «Sin dato» (`formatoSecretaria`). Nada de dirección, correo ni teléfono.
- Totales: `ListaDefinicion` con «Total a pagar» (monto §0.1, `text-base font-bold`), «Vigencias pendientes» (n) y «Vencimiento más próximo». **`null` muestra «Sin dato»; `0` muestra `$ 0`.**
- Vigencias (`TablaVigencias.tsx`): tabla por defecto (§0.2) dentro de un contenedor con scroll horizontal (`RegionDesplazable`, con la forma de Diagnóstico: `tabIndex=0`, `role="region"`, `aria-label="Vigencias"`). Montos alineados a la derecha. Cada celda `null` dice «Sin dato». Una fila `no_liquidable` lleva el chip de §1.2. Cabeceras = `Periodo.x-flit-etiquetas`. `otros_cargos` va como sub-fila desplegable «Otros cargos ({n})». Por debajo de 1024 px se apila con `TABLA_MOVIL_*`.
- Recibos (`ListaRecibos.tsx`), una fila por recibo:
  - Nombre: `TipoRecibo.x-flit-etiquetas` («Recibo de vigencia {periodos}», «Recibo consolidado» o «Paz y salvo»).
  - Chip de §1.4 y, debajo, el motivo de `MotivoRecibo` si lo hay.
  - Con `GUARDADO` y `recibos.descargar`: enlace con forma de botón `BOTON_SECUNDARIO_CLS` + `Icono "nube-bajar"` «Descargar recibo» (`pantalla.accionDescargarRecibo`), con nombre accesible «Descargar recibo {nombre}». Estado propio por botón: «Descargando…» con `aria-busy`. Error bajo el botón con `role="alert"` y `aria-describedby`: 409 `RECIBO_NO_DISPONIBLE` y 404 `RECIBO_NO_ENCONTRADO` con su literal del contrato; otro, «No se descargó el recibo. Intenta de nuevo.».
  - Sin permiso, solo el estado.
  - Sin recibos: «Esta liquidación no tiene recibos.»
- Observaciones del proveedor: `TarjetaPlegable` de `@flit/ui/TarjetaPlegable` (`nivelTitulo={2}`, cerrada por defecto), con tres listas tituladas «Advertencias», «Validaciones» y «Archivos con error». De `validations` solo se muestran el campo y el mensaje; **nunca `sent`**, que puede traer PII. Sin ninguna, la tarjeta no se pinta.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando la placa…"`. |
| Vacío | No aplica como pantalla. Por bloque: «Sin vigencias pendientes», «Esta liquidación no tiene recibos.». |
| Error | 404 de recurso → `notFound()`; errores de compañía en línea y Super Admin sin `?empresa=` con `EligeCompania`, como en §3.5. Otro: `InlineAlert error` «No se pudo cargar la placa» + «Reintentar». |
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

- Admin Impuestos: `CabeceraModulo` «Usuarios» (`icono="colaboradores"`, `descripcion` = compañía, `acciones` = `Button` primario «Invitar usuario» con `Icono "mas"`). Debajo, filtros (1.1.0, delta 6): «Buscar nombre o correo», «Rol» (`select` nativo con «Todos» + `RolImpuestos.x-flit-etiquetas`) y «Estado» («Todos» + Activo, Invitado, Suspendido); cada cambio vuelve a la página 1. Tabla Nombre · Correo · Rol · Estado · Acciones (`ActionsMenu` «Acciones del usuario»: «Cambiar rol», solo si hay otro rol que dar, «Suspender» con `tone="danger"`, «Reactivar»).
- SA sin `?empresa`: tabla de compañías con Impuestos (Compañía · NIT · Usuarios · «Ver usuarios»). «Usuarios» = `EmpresaBackoffice.usuarios` (1.1.0, etiqueta del contrato); si no llega, celda vacía. NIT `null`: «Sin dato». Elegir una fija `?empresa=`, que también actualiza el selector de la barra.
- SA con `?empresa`: la cabecera sale de `PaginaUsuarios.empresa` (`EmpresaIdentificada`, 1.1.0, delta 7): miga «Usuarios › {nombre}» y `descripcion` «{nombre} · NIT {nit}». Si el nombre llega vacío (compañía suspendida), se usa el id; con `nit` `null` se omite la parte del NIT. Mientras se relee la lista con otros filtros se conserva la última compañía identificada.
- Filas sin menú, en `TEXTO_SECUNDARIO_CLS`: la propia, «Eres tú»; la protegida (`UsuarioImpuestos.protegido`, 1.1.0: el AdminCompany, siempre Administrador Impuestos por D-ROL-ADMIN), el literal del contrato `OBJETIVO_PROTEGIDO`, «Este usuario se gestiona desde el administrador de la plataforma.»; la invitación pendiente, el literal `INVITACION_PENDIENTE`.
- «Invitar usuario» (`Modal`, `closeOnBackdrop={false}`):
  - Descripción: «Le llegará un correo para entrar a Impuestos con el rol que elijas.»
  - Campos «Nombre completo», «Correo electrónico» y «Rol» (radios con descripción). Administrador Impuestos: «Todo en su compañía: liquidar, cerrar lotes, usuarios y configuración.» Gestor Impuestos: «Ve toda la compañía y liquida por placa o masivo. No gestiona usuarios ni configuración.»
  - Errores por campo con los textos del molde y un resumen «Revisa los datos de la invitación».
  - CTA «Enviar invitación».
- Cambiar rol: título «Cambiar rol de {Nombre}», CTA «Guardar rol» (deshabilitado si no cambia). Nombres de rol = `RolImpuestos.x-flit-etiquetas`.
- **Aviso de resultado** (invitar, cambiar rol, suspender, reactivar): con la forma del molde de Diagnóstico, una región estable `role="status" aria-live="polite" aria-atomic="true"` bajo la cabecera que contiene un `InlineAlert tone="success" vivo={false}`. No se auto-oculta por tiempo (WCAG 2.2.1) y se reemplaza con la siguiente acción. Este es el «toast» del producto; no se agrega una librería de toasts.
- Suspender / Reactivar:
  - Título «Suspender usuario» o «Reactivar usuario».
  - Cuerpo al suspender: «Pierde el acceso a Impuestos. Su cuenta y los demás productos no cambian.»
  - Foco inicial en «Cancelar». La acción usa `variant="alerta"` al suspender.

| Estado | Contenido |
|---|---|
| Cargando | Esqueleto de tabla «Cargando usuarios…». |
| Vacío | «Ningún usuario coincide» · «Prueba con otro nombre o correo, o quita algún filtro.» Sin usuarios: «Aún no hay usuarios en Impuestos» + «Invitar usuario». |
| Error | `InlineAlert error` «No pudimos cargar los usuarios» + «Reintentar». Errores de diálogo dentro del diálogo, con el literal del contrato por `code` (`ROL_NO_PERMITIDO`, `ROL_NO_ASIGNABLE`, `OBJETIVO_PROTEGIDO`, `ACCION_SOBRE_SI_MISMO`, `ULTIMO_ADMINISTRADOR`, `ROL_YA_VIGENTE`, `NOMBRE_REQUERIDO`, `CORREO_INVALIDO`, `PLATAFORMA_NO_DISPONIBLE`…). |
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
  - Éxito: «Configuración guardada.» en la región viva del aviso de resultado (§3.8). 422 `TOPE_INVALIDO` y `TOPE_SUPERA_CONTRACTUAL` (con `{tope}`) junto al campo; `CORREO_INVALIDO` junto a la lista, con el literal del contrato. La guía por línea «La línea {n} no es un correo válido.» es validación local previa al envío.
- Contractual:
  - Solo lectura: `ListaDefinicion` con «Máximo de placas por lote contratado» y «Departamentos habilitados» (chips `neutral`, o «Todos los departamentos» si es `null`).
  - SA editor: el selector de la barra elige la compañía; sin compañía elegida, `EstadoVacio` «Elige una compañía en la barra para editar su contrato».
  - Campos: el número y un `fieldset` «Departamentos habilitados» con el interruptor «Todos los departamentos» (`Interruptor` de `@flit/ui/Interruptor`) y las casillas de `departamentosDisponibles`.
  - CTA «Guardar contrato». 422 `DEPARTAMENTO_NO_DISPONIBLE`: literal del contrato. Éxito en la región viva (§3.8).
- Sin `configuracion.editar`: `InlineAlert tone="info" title="Solo lectura"` «Puedes ver la configuración, pero no cambiarla.» y los campos `disabled`.

| Estado | Contenido |
|---|---|
| Cargando | `VehicleLoader label="Cargando la configuración…"`. |
| Vacío | Sin correos: ayuda «Nadie recibe el aviso por correo; el aviso en pantalla sigue llegando.». |
| Error | Lectura: `InlineAlert error` «No se pudo cargar la configuración» + «Reintentar». Guardado: «No se guardó la configuración» + el `error` del contrato (§0), conservando lo digitado. |
| Lleno | El formulario. |

### 3.10 Backoffice SA — `/backoffice/empresas` y `/backoffice/empresas/[tenantId]` (M18)

**Molde:** `frontend-diagnostico/components/backoffice/ListaLotesBackoffice.tsx` (banda de contexto + tabla + filtros en la URL) y, para el formulario protegido, `frontend/components/admin/companies/tabs/CredencialImpuestoDiagnostico.tsx`.

**Lista:**
- `CabeceraModulo` «Compañías con Impuestos» (`icono="empresas"`, `descripcion` «Estado de la conexión de cada compañía con el proveedor»).
- Banda de contexto (`InlineAlert tone="info" vivo={false}`): «Estás en el backoffice de FLIT: ves todas las compañías, incluidas las suspendidas.»
- Búsqueda «Buscar por nombre o NIT» (`q`): vive **solo en el estado de la pantalla**. Ni en la URL (un NIT puede ser una cédula) ni en `sessionStorage`/`localStorage` (AC-FE-43, vigilado por `independencia.test.ts`). Al recargar se pierde: es aceptado.
- Tabla: Compañía · NIT · Usuarios · Estado · Motivo (`MotivoAprovisionamiento.x-flit-etiquetas`) · Acción («Ver detalle»).
  - NIT `null` y Motivo ausente: «Sin dato».
  - Usuarios = `EmpresaBackoffice.usuarios` (1.1.0, etiqueta del contrato); si no llega, celda vacía.
  - Estado = chip §1.5 más los chips que apliquen, siempre con texto: «Firma sin confirmar» `purple` (`LISTO` sin firma), «Respaldo de firma» `purple` y, con `suspendida=true`, «Suspendido» (etiqueta `SUSPENDIDO` del contrato, tono `neutral`) cuando el estado no lo dice ya.

**Detalle:**
- `CabeceraModulo` con titulo = compañía y miga «Backoffice › {Compañía}». Nombre y NIT salen de `AprovisionamientoDetalle.empresa` (`EmpresaIdentificada`, 1.1.0, delta 7): si el nombre llega vacío (compañía suspendida) o falta `empresa`, el título es el id; `descripcion` «NIT {nit}», omitida si `nit` es `null`.
- Tarjeta «Estado»: chip, motivo legible (§1.5) y acción.
  - «Reintentar aprovisionamiento» (`Button variant="secondary"`) en `ERROR`, `INCIERTO`, `REQUIERE_REVISION_NIT` y, **entregado en 1.2.0**, `LISTO_SIN_WEBHOOK` (el alta idempotente trae el secreto). Con 202: «Reintento en cola. El estado se actualiza en unos segundos.» en la región viva (§3.8). 409 `ESTADO_NO_REINTENTABLE`: literal del contrato.
  - En `REQUIERE_CREDENCIALES`, además, `pantalla.avisoRotacionCredenciales` (`InlineAlert warning vivo={false}`): «El proveedor rechazó la API key guardada: reintenta el aprovisionamiento o carga credenciales nuevas.» (1.2.1; en la 1.2.0 ese estado nace de `CREDENCIAL_RECHAZADA`, no de una empresa ya registrada). Solo en ese estado: en `LISTO_SIN_WEBHOOK` y en `ERROR` no sale.
  - Con `modoRespaldoFirma=true`, **entregado en 1.2.0**: línea en texto plano con la etiqueta del contrato (§1.5).
- Tarjeta «Credenciales» (`FormularioCredenciales.tsx`):
  - Estado: «API key: {pantalla.credencialConfigurada} el {fecha} · empieza por {apiKeyPrefijo}» o «{pantalla.credencialNoConfigurada}»; lo mismo para «Secreto del webhook»; «Origen: {OrigenCredencial.x-flit-etiquetas}» («Alta automática», «Copiada de Diagnóstico», «Cargada por el Super Admin»).
  - «Cargar credenciales» / «Reemplazar» abre el formulario:
    - «API key» y «Secreto del webhook (opcional)», `type="password"` con `autoComplete="off" spellCheck={false} autoCapitalize="none"` y botón `Button variant="ghost" size="sm"` «Mostrar API key» / «Ocultar API key» con `aria-pressed`.
    - Ayuda: «Rota las credenciales en la consola del proveedor antes de cargarlas. Al guardar se validan con el proveedor y no se vuelven a mostrar.»
    - CTA «Validar y guardar» + «Cancelar» (`variant="navy"`, como el molde).
  - Al guardar (204): se limpian los campos, se cierra el formulario y sale «Credenciales guardadas y validadas.» en la región viva (§3.8).
  - 422 `CREDENCIAL_INVALIDA` y `CREDENCIAL_REQUERIDA`, 409 `ESTADO_NO_ADMITE_CARGA` y 503 `PROVEEDOR_NO_DISPONIBLE`: literal del contrato, dentro de la tarjeta.
- Tarjeta «Contrato»: enlace «Editar contrato» a `/configuracion?pestana=contractual&empresa={id}`.
- Historial: tabla Fecha · De · A · Motivo · Por. El motivo va rotulado y «Por» es la etiqueta de `actorTipo` («Sistema», «Super Admin», «Plataforma»); sin actor, «Sistema». **Entregado en 1.2.0:** el historial pinta la fila de `CLAVE_ROTADA` con su etiqueta («El proveedor reemplazó la API key de la compañía; la nueva quedó guardada.»), y los motivos retirados conservan su literal (§1.5).

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
  - `SIN_ADMIN`: `InlineAlert warning` con la etiqueta del contrato (`ProvisioningPreview.blocking.x-flit-etiquetas.SIN_ADMIN`: «La compañía no tiene un Administrador de Compañía activo»). **El CTA queda deshabilitado** con ese motivo (`aria-describedby`). El 409 `ProvisioningPrerequisiteMissing` da el mismo texto.
  - NIT `AMBIGUO`, `AUSENTE` o `IGUAL_A_CABEZA`: `InlineAlert warning` «El NIT de la compañía requiere revisión: {`nitStatus.x-flit-etiquetas`}. Impuestos quedará en «Requiere revisión del NIT» hasta que lo corrijas en «Editar compañía».». El CTA sigue habilitado.
  - Razón social de más de 200 caracteres (validación local): `InlineAlert warning` «La razón social pasa de 200 caracteres: el proveedor la rechazará. Acórtala en «Editar compañía».».
  - «Editar compañía» es el nombre de la acción real del admin de compañías; no se habla de «la pestaña Datos».
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
- Contenido: «Conexión con el proveedor:» + chip (§1.5, `StatusBadge` con `icono` de `ICONO_TONO` por tono; `PENDIENTE` en tono `info`; `LISTO_SIN_WEBHOOK` sin chip extra) + motivo legible = `provisioning.message` tal cual + acción según `action`, con el literal de `ProvisioningAction.x-flit-etiquetas`:
  - `REINTENTAR` → «Reintentar» (`Button variant="secondary" size="sm"`).
  - `CARGAR_CREDENCIALES` → «Cargar credenciales» (**entregado en 1.2.0**: `LISTO_SIN_WEBHOOK` trae `REINTENTAR` y sale «Reintentar»; `CARGAR_SECRETO` está retirada y, si llega de un reporte anterior, se trata como `REINTENTAR`), enlace externo a `{url}/backoffice/empresas/{id}`, donde `{url}` es la `url` del producto `impuestos` en `GET /platform/me/apps` (no una variable de entorno), con `target="_blank" rel="noopener"` y « (se abre en otra pestaña)» en `sr-only`.
  - Sin `url` (el usuario no tiene Impuestos en sus apps): texto sin enlace **«{acción}: hazlo desde Impuestos › Backoffice»**, que conserva el nombre de la acción.
  - `CORREGIR_NIT` → «Corregir NIT»: abre «Editar compañía». Si el tab no recibe esa acción, texto «Corrige el NIT con «Editar compañía».».
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
| `SUSPENDIDO` | `SUSPENDIDO` | `warning` | Producto suspendido: solo lectura (`pantalla.bannerProductoSuspendido`) | Puedes consultar las liquidaciones, los recibos y los reportes, pero no crear liquidaciones nuevas. Los lotes que estaban en curso terminan solos. |
| `NO_LISTO` (nuevo) | `PENDIENTE`, `ERROR`, `INCIERTO`, `REQUIERE_CREDENCIALES`, `REQUIERE_REVISION_NIT` | `pending` | Impuestos todavía no está listo para liquidar | La conexión de tu compañía con el proveedor se está configurando. Puedes consultar lo que ya existe; para liquidar, espera a que termine o pídele ayuda a tu administrador. |
| `SIN_WEBHOOK` | `LISTO_SIN_WEBHOOK` o `modoSinWebhook=true` | `info` | `pantalla.bannerModoSinWebhook` | Ya puedes liquidar. Los resultados se consultan con el proveedor cada pocos minutos, en lugar de llegar al instante. |
| `FIRMA_SIN_CONFIRMAR` | `LISTO` con `firmaConfirmada=false` | `info` | `pantalla.bannerFirmaSinConfirmar` | Ya puedes liquidar. Se confirma sola con el primer resultado que llegue. |

- **Revisión 2026-11-25 (precedencia del contrato):** el título de `SUSPENDIDO`, `SIN_WEBHOOK` y `FIRMA_SIN_CONFIRMAR` es el literal de `x-flit-literales.pantalla`; el texto de la columna «Texto» sigue como cuerpo, porque el contrato no lo fija. `NO_LISTO` no tiene literal de banner en el contrato y conserva el de esta spec. **Pendiente en código (M15):** `BannerEstadoProducto.tsx:18-30` todavía pinta los títulos anteriores. Recomendación de UX para el contrato (no bloquea): los literales de banner hablan de «webhook» y «firma», que no le dicen nada a un Gestor; si el PO lo prefiere, el `architecture-agent` puede cambiarlos en el contrato y la UI los toma solos.
- Cambio de tono: `FIRMA_SIN_CONFIRMAR` pasa de `pending` (naranja = alerta) a `info` (azul = en proceso), porque no requiere ninguna acción (regla 5 de estados semánticos).
- Para el SA, los títulos `NO_LISTO` y `SUSPENDIDO` llevan además, después del texto, un enlace «Ver en el backoffice» a `/backoffice/empresas/{id}`.

### 4.4 Campana — literales

| `tipo` | Título | Mensaje | Enlace |
|---|---|---|---|
| `LOTE_TERMINADO` | Lote terminado | Ya está el resultado de cada placa del lote. | Ver lote (con `sr-only` «: Lote terminado ({fecha})», como hoy) |
| desconocido | Sin clasificar | Abre el lote para ver el detalle. | Ver lote |

Estados del panel (sin cambio): «Cargando avisos…», «No tienes avisos», «No se pudieron cargar los avisos» + «El resto de Impuestos sigue funcionando.» + «Reintentar».

### 4.5 403 — literales definitivos (`app/403/page.tsx`)

Los códigos que el contrato define (`PRODUCTO_NO_HABILITADO` y `SIN_EMPRESA`) usan su literal de `x-flit-literales.errores` (§0 «Precedencia del contrato»), que ya dice «compañía»; `PRODUCT_NOT_ENABLED` es su alias y lleva el mismo texto. Los códigos que solo llegan desde el hub o `@flit/auth` conservan «empresa» por coherencia con la 403 de Diagnóstico (regla 6); unificarlos es el hueco de suite H-10.

| `code` | Mensaje |
|---|---|
| `PRODUCTO_NO_HABILITADO`, `PRODUCT_NOT_ENABLED` | Impuestos no está activo para tu compañía. *(literal del contrato)* |
| `PRODUCTO_SUSPENDIDO` | Impuestos está suspendido para tu empresa. Pídele ayuda a tu administrador. *(sin cambio)* |
| `SIN_PERMISOS_IMPUESTOS`, `PRODUCT_ROLE_REQUIRED` | No tienes un rol en Impuestos. Pídele acceso a tu administrador. *(sin cambio)* |
| `SIN_EMPRESA` | Tu usuario no tiene una compañía asociada. *(literal del contrato)* |
| `SESSION_INVALID` | Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión. *(sin cambio)* |
| (otro) | No tienes permiso para abrir esta página. *(sin cambio)* |

Título «No tienes acceso» y acción «Ir a mis productos» → `{hubUrl}/?inicio=1`: sin cambio. **Pendiente en código:** `app/403/page.tsx:8-13` todavía pinta los textos anteriores de `PRODUCTO_NO_HABILITADO`, `PRODUCT_NOT_ENABLED` y `SIN_EMPRESA`; la auditoría toma esta tabla.

Mensajes del shell (sin cambio): «Abriendo Impuestos…» y «No se pudo abrir Impuestos» + «Reintentar».

---

## 5. Huecos del design system y componentes faltantes

Ninguno se crea en esta ola sin encargo (modo D). Para cada hueco se indica dónde debería vivir.

| # | Falta | Dónde | Recomendación para esta ola |
|---|---|---|---|
| H-1 | **Gráficas** (`Figura`, `BarrasSerie`, `ListaBarras`, `TablaAlternativa`, `RegionDesplazable`): locales de Diagnóstico y copiadas en Impuestos | `packages/ui` (`@flit/ui/graficas/*`) | **Decidido (D-UI-GRAFICAS):** copia local en `frontend-impuestos/components/estadisticas/` en esta ola, sin mover a `packages/ui` ni importar Diagnóstico. Deuda registrada: con dos consumidores ya justifica un modo D posterior que las extraiga y migre a ambos productos a la vez. |
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
| H-14 | **Cerrado en 1.2.0:** `AprovisionamientoDetalle.modoRespaldoFirma` tiene etiqueta en `x-flit-etiquetas` | `contracts/openapi/impuestos.v1.yaml` | El detalle pinta la etiqueta del contrato; el chip corto «Respaldo de firma» se mantiene en el listado (§1.5, §3.10). |
| H-15 | **Cerrado en 1.1.0 (delta 4):** `ResumenLiquidacion` trae `estadoItem` e `itemId` en una `INDIVIDUAL` | `contracts/openapi/impuestos.v1.yaml` | El chip sale de `estadoItem` y el enlace va directo al ítem (§3.2); `contadores` queda como respaldo para respuestas viejas y para el resultado de `EXITOSO`. |
| H-16 | **Cerrado en 1.1.0:** `Liquidacion.motivoCierre` (`maxLength` 300) | `contracts/openapi/impuestos.v1.yaml` | Segunda línea «Motivo: {texto}» en texto plano (§3.5). |
| H-17 | **Cerrado en 1.2.0:** `Item.relanzadoDesdeLiquidacionId` | `contracts/openapi/impuestos.v1.yaml` | Enlace «Ver la original» al ítem original (§3.6). |

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
4. **[B] Estados semánticos y literales:**
   - Los chips salen del mapa de §1 (`lib/liquidaciones/estados.ts`), con texto y punto o ícono.
   - Literales de estados, acciones, clases, motivos y errores = contrato (§0 «Precedencia del contrato»); coincidir con el contrato **no** es desviación aunque difiera de una versión previa de esta spec.
   - Un código desconocido dice «Sin clasificar».
   - En tablas, `etiquetaKit` sin `role="status"`.
5. **[B] Permisos en la UI:**
   - Gestor sin Usuarios ni Configuración y sin «Cerrar lote».
   - Selector y columna «Compañía» solo para el SA.
   - Contractual solo editable por el SA.
   - Exportar solo con `reportes.exportar` y «Descargar recibo» solo con `recibos.descargar`.
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
