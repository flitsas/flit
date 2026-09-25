# Plan técnico — Ajustes UI · observaciones de diseño del 7 de septiembre

- **Origen:** `observaciones-ui-7-septiembre.pdf` (raíz del repo, 9 páginas con capturas de DEV/QA y anotaciones del diseñador)
- **Fecha:** 2026-09-21
- **Estado:** **Creado en ADO** (2026-09-21) — Feature **#12722** + HUs **#12723–#12732** (`New`, DoR, sin activar) bajo la épica #12535; D2/D3/D5 cerradas por el PO, D4 aceptada como propuesta, D6 cerrada por la épica
- **ADO:** Épica **#12535** `[DISEÑO] - Ajustes de UX/Diseño, Layout y Notificaciones FLIT 2.0` (Active, Sprint 7, 15 CF; mismo PDF adjunto). Feature e HUs en **backlog raíz** hasta que exista Sprint 8 (el PAT no tiene permiso *Create child nodes* — crearlo desde la UI y moverlos)
- **Alcance:** solo `frontend/` (Next.js 16 · React 19 · Tailwind 4). Ningún endpoint ni migración, salvo la verificación de C.4 (refresco del detalle)
- **Fuera de alcance explícito (PDF pág. 1):** concesión y marca blanca — «revisar más adelante». Tampoco el numeral 3 del PDF, que no existe (salta de 2 a 4)
- **Agente ejecutor:** `frontend-agent` con `flit-design-guardian` obligatorio en cada HU; `dev-tester` al cierre de cada una
- **Brief de hechos:** `explore-agent` 2026-09-21 (16 puntos, paths verificados). Las anclas `archivo:línea` de `TramiteWizard.tsx` (5.172 líneas) y `TramitesTable.tsx` (~2.650) derivan con cada merge — **reverificar antes de citarlas en una HU**

El PDF recorre la plataforma en el orden en que la usa una persona: **dock → dashboard → lista y detalle de trámite → wizard (paso 3, validación, resumen, expediente) → administración del OT**. El plan respeta ese orden y lo convierte en cinco bloques y diez HUs. Cada bloque abre con la observación literal, sigue con lo que el código hace hoy (no siempre coincide con lo que el diseñador ve en la captura) y cierra con el diseño, los archivos y las trampas.

---

## D0 — Antes de todo: el azul corporativo no pasa AA en texto pequeño

Tres observaciones piden pintar **texto** en `#557eff` (labels del dock activo, la palabra «Opcional», botones secundarios). `#557eff` sobre blanco da un contraste de **3,6:1**: pasa el mínimo de 3:1 para iconos, bordes y texto grande (≥ 18,66 px en negrita), pero **no** el 4,5:1 que WCAG 2.1 AA exige a texto normal — y `flit-design-guardian` lo marcaría como bloqueante.

**Diseño:** introducir un token `--flit-brand-ink` (un azul de la misma familia con contraste ≥ 4,5:1 sobre `#fff` y sobre el fondo dark) y usarlo para **texto pequeño**; `#557eff` sigue en iconos, bordes, rellenos y texto grande. Se define una sola vez en `frontend/app/globals.css` junto a `--color-flit-brand` (`:15`) y sus variantes dark (`:212-238`).

**Decisión D0 (diseño):** valor hex exacto de `--flit-brand-ink`. Propuesta: el que resulte de oscurecer `#557eff` hasta 4,6:1 manteniendo el matiz; el `frontend-agent` lo calcula y lo deja documentado en el propio token con el ratio medido.

---

## Bloque A — Menú (dock flotante)

### A.1 Ítem seleccionado: sin fondo, icono y texto en azul corporativo

> **PDF p.1 #1:** «corregir colores del menú seleccionado, no parecen al degradado del icono del centro… o al seleccionar un menú y submenú se coloque el icono y texto del color azul corporativo flit 2 #557eff (sin fondo)».

**Hoy:** la píldora activa (`aria-current=page`) pinta `background: var(--nav-activo)` con texto blanco, y `--nav-activo` es `linear-gradient(90deg, #4fd4cc 0%, #4f74c9 100%)` (`frontend/app/globals.css:159`, reglas `.dock-pill` en `:396-419`). El botón central **no es un degradado CSS**: es `<img src="/assets/favicon.svg">` con `boxShadow: var(--nav-sombra-activo)` (`components/atom/dock/DockDesktop.tsx:9,63-74`). Es decir, hay **tres** degradados distintos conviviendo (el de la píldora, el del isotipo y el de marca `#00dbd5→#557eff` que usan el banner y los CTA del wizard). Por eso «no se parecen».

El submenú activo ya va sin degradado: fondo plano `--nav-app-bg` + `font-semibold` (`DockDesktop.tsx:241,262,291`).

**Diseño (opción B del PDF, la explícita):** el ítem activo pierde el fondo; icono en `#557eff` (`--color-flit-brand`) y label en `--flit-brand-ink` (D0). Mismo tratamiento para el submenú activo. Hover conserva `--nav-app-bg`. `--nav-activo` deja de usarse en `.dock-pill[aria-current=page]`; se conserva únicamente para `.dock-fab` mientras no se toque el isotipo (HU #12646 de Marca Blanca lo hará dinámico — no pisarla).

**Archivos:** `frontend/app/globals.css` (`.dock-pill[aria-current=page]`, tokens `--nav-*` light y dark `:150-164`, `:233-238`), `components/atom/dock/DockDesktop.tsx` (`:130`, `:150`, `:241`, `:262`, `:291`).
**Tests:** `components/atom/__tests__/Shell.test.tsx`, `useDockScrollCondense.test.tsx`; añadir aserción de que el ítem activo no tiene `background` y sí `aria-current`.
**Trampa:** el estado activo debe seguir siendo perceptible sin color (WCAG 1.4.1): mantener `font-semibold` y `aria-current` — no fiarlo solo al azul.

### A.2 Nivelar los lados, quitar «Ayuda», «Usuarios» a la derecha

> **PDF p.1 #2:** «nivelar cantidad de menú de lado y lado, el centro siempre debe ser el isotipo de FLIT. Eliminar icono ayuda y trasladar usuarios al lado derecho».

**Hoy:** el orden fijo es `DOCK_GROUP_ORDER = ['tramites','preasignacion','identidad','reportes','usuarios','administracion','administradores','integraciones','ayuda']` (`components/atom/dock/dockGroups.ts:26-36`). El reparto es mecánico: `half = Math.ceil(groups.length / 2)`, primera mitad a la izquierda, resto a la derecha, con relleno invisible para centrar el FAB (`DockDesktop.tsx:31-36`). «Ayuda» está forzado como siempre visible por el filtro RBAC (`components/atom/Shell.tsx:225-231`), y `Shell.test.tsx` lo fija («Ayuda siempre» y «mismo nº de elementos a cada lado»).

**Diseño:**
1. Retirar `ayuda` de `DOCK` (`Shell.tsx:95-105`) y de `DOCK_GROUP_ORDER`, y quitar la excepción `it.id === 'ayuda'` del filtro RBAC.
2. Nuevo orden: `['tramites','preasignacion','identidad','reportes', | 'usuarios','administracion','administradores','integraciones']`. Con eso «Usuarios» abre el lado derecho.
3. Sustituir el reparto por mitades por un **reparto por lado declarado** (`side: 'left' | 'right'` en `DOCK_ITEM_GROUP`): así el lado no depende de cuántos módulos vea el rol. Cuando un rol vea un número impar, el relleno invisible existente sigue centrando el FAB; el `frontend-agent` decide si nivela con un slot vacío o acepta la asimetría de uno (el PDF pide «nivelar», no «igualar a toda costa»).
4. Verificar contra `docs/arbol-menu-por-rol.md` las ramas adminOT / adminCompany (`Shell.tsx:432-513`, no barridas) para que ningún módulo quede sin lado y por tanto invisible (comentario del propio código).

**Decisión D2 (producto):** ¿«Ayuda» desaparece o se reubica? Propuesta: **reubicar** en el menú del usuario (⋮ arriba a la derecha) para no perder la entrada al manual (`components/manual/ManualShell.tsx`). Si el PO confirma «eliminar», se retira la ruta del dock y se deja accesible solo por URL.

**Archivos:** `components/atom/Shell.tsx`, `components/atom/dock/dockGroups.ts`, `components/atom/dock/DockDesktop.tsx`, `docs/arbol-menu-por-rol.md` (actualizar en el mismo PR).
**Tests:** `Shell.test.tsx` (reescribir las dos aserciones citadas), `dock/__tests__/dockGroups.test.ts`, las cuatro variantes `Shell.*.test.tsx`.

---

## Bloque B — Dashboard

### B.1 Banner con alto estándar

> **PDF p.1 #4 y p.2:** «este banner está demasiado grande, predefinir el banner en alto para todos los perfiles, este tamaño debe ser estándar… no debe variar». El ejemplo de prototipo/Lovable muestra el banner **a la altura exacta de las dos filas de tarjetas de la derecha**.

**Hoy:** el carrusel vive inline en `components/atom/modules/Dashboard.tsx` (`:500-505`): `md:col-span-2` de una grilla `md:grid-cols-3`, con `style={{ minHeight: '220px' }}`. Como es `min-height`, el alto real lo fija el slide más largo (el de bienvenida, cuyo texto depende de datos: validaciones por vencer, `:133-144`). No existe la variante naranja «3 SOAT próximos a vencer» del prototipo — es solo un ejemplo del diseñador. La columna derecha es `flex flex-col gap-3` con filtros + KPIs `flex-1`.

**Diseño:** la columna derecha pasa a definir el alto y el banner se estira a ella (`items-stretch` + `h-full` en el banner, sin `minHeight`). La columna derecha queda **fija**: una fila de filtros (B.2) + una grilla de KPIs de **dos filas**. El texto del slide se recorta con `line-clamp` para que nunca empuje el alto. Se documenta el alto resultante como token `--dashboard-hero-h` para que las tarjetas y el banner lo compartan y no se desincronicen en otro breakpoint.

**Decisión D3 (diseño):** hoy hay **5 KPIs en `grid-cols-3`** (`:647-653`: Total, Matrículas, Traspasos, Otros, Completados) — dos filas con la segunda incompleta. El prototipo muestra **4 en 2×2**. Opciones: (a) 2×2 quitando «Otros trámites» (lo absorbe Total), (b) 2×3 conservando los 5 con una celda de relleno, (c) 3+2 como hoy. Propuesta: **(a)** si el PO acepta perder «Otros»; si no, (c) con la fila incompleta alineada a la izquierda.

**Archivos:** `Dashboard.tsx` (`:500-530` banner, `:615-653` columna derecha, `:667-705` tarjeta KPI), `globals.css` (token).
**Tests:** `components/atom/modules/__tests__/Dashboard.test.tsx`.
**Trampa:** `bannerAmbientGradient(bannerColor)` (`:513`) y `useDominantColor` siguen intactos: cambia el alto, no el color de los banners de HU #12242.

### B.2 Un solo campo de rango de fechas + buscador en su sitio

> **PDF p.1 #4:** «unificar el contenedor de la fecha en un solo campo, es decir que el calendario me deje elegir las fechas inicio o final; donde está el campo de "hasta" ubicar el buscador de lupa que está debajo».

**Hoy:** `DateRangeFilter` son dos `<input type="date">` nativos apilados con label uppercase (`components/atom/modules/_reportes/DateRangeFilter.tsx:18-41`); `DateRange` en `range.ts` es solo un tipo `{from,to}`. El selector de compañía es `CompanySelector → SearchableSelect` con icono `Search` de lucide (`CompanySelector.tsx:44`, `SearchableSelect.tsx:191`). **No hay** ningún date-range picker ni `react-day-picker`/`date-fns`/`dayjs` en `package.json`. `DateRangeFilter` lo comparten también los reportes del OT (`components/admin/transit-offices/_reportes/*`) e `IctReports`.

**Diseño:** nuevo `components/atom/DateRangePicker.tsx`: un campo (`Desde – Hasta`) que abre un popover con calendario de rango; emite el mismo `DateRange` de `range.ts`, con lo que `Dashboard`, reportes OT e ICT migran sin tocar su lógica. En el dashboard la fila de filtros queda **`[Rango de fechas] [🔍 Todas las compañías]`** en una sola línea (`grid-cols-2`), y la grilla de KPIs debajo (B.1).

**Decisión D4 (técnica):** implementación del calendario. (a) **`react-day-picker`** (maduro, accesible, sin dependencias transitivas pesadas) — exige auditoría previa (regla 18) y pasa por `dependency-audit` del CI; (b) calendario propio (más control, más superficie de bugs y de a11y). Propuesta: **(a)**. Si se rechaza, (b) reusando el patrón `useDisclosureNav` del dock para el popover.

**Archivos:** nuevo `components/atom/DateRangePicker.tsx`; `_reportes/DateRangeFilter.tsx` (pasa a envolver el picker o se elimina); `Dashboard.tsx:615-630`; consumidores en `components/admin/transit-offices/_reportes/` e ICT; `package.json` si D4=(a).
**Tests:** nuevo `DateRangePicker.test.tsx` (teclado, rango inválido, `sinRango`), `Dashboard.test.tsx`, `SearchableSelect.test.tsx`, specs de reportes OT que rendericen el filtro.
**Trampa:** `isValidOptionalRange` (`range.ts`) es la única validación de rango; el picker debe seguir permitiendo rango vacío (dashboard sin filtro).

---

## Bloque C — Lista y detalle del trámite (MI y traspaso)

Los dos detalles (matrícula inicial y traspaso) son **el mismo componente**: `components/operacion/TramiteDetalleModal.tsx` (919 líneas), con título y pasos por `item.modalidad` (`:75-76`, `:86`, `:338`). Todo lo de este bloque aplica a MI, traspaso y OTROS a la vez — es una ventaja y un riesgo.

### C.1 El badge del detalle no coincide con el color del filtro

> **PDF p.2:** «la etiqueta morada no coincide con el color del estado del trámite, ver colores de los filtros de los estados».

**Hoy:** hay **dos fuentes de color de estado**. La fila y el funnel de filtros usan `ESTADO_CHIP_STYLES` de `lib/tramites/estados.ts:117-176` (entregado `#00A99D` teal, aprobado `#8CC63F`, preparado `#557EFF`, subsanación `#FF4E00`…). El **header del detalle** usa `detalleEstadoHeader()` en `components/operacion/detalle/detalle-estado-header.ts:100-131`, cuyo `default:` (que incluye `entregado`, `asignado`, `preasignacion`) pinta **dorado `#F9AC00`** (`detalle-visual.ts:9`). El «morado» de la captura corresponde a un build anterior o a `asignado` (`#6366F1`); el desajuste real es dorado vs teal.

**Diseño:** `detalleEstadoHeader()` deja de tener paleta propia y toma el `accent` de `estadoChipStyle(estado)` — una sola fuente de verdad (`estados.ts`). El header conserva su icono y su flag `pendiente`, pero el color viene del catálogo. Sin cambiar ningún hex del catálogo.

**Decisión D5 (producto):** el PDF cita «verde #8cc63f» al lado de la observación. En el funnel, **Entregado es teal** y **Aprobado es verde**; el propio filtro que el PDF pone de referencia ya lo muestra así. Propuesta: **no recolorear Entregado**; el objetivo es que badge = filtro, y eso se cumple unificando la fuente. Si el PO quiere Entregado en verde, es un cambio de catálogo (afecta fila, funnel, detalle y `estados-catalogo.test.ts`) y se decide aparte — respeta ADR-0022 (no renombrar estados).

**Archivos:** `detalle-estado-header.ts`, `detalle-visual.ts` (DETALLE_GOLD queda solo para C.3 si aplica), `TramiteDetalleModal.tsx:430-436`.
**Tests:** `components/operacion/detalle/__tests__/estados-ruta-placa.test.ts`, `lib/tramites/__tests__/estados-catalogo.test.ts`, `components/atom/__tests__/StatusBadge.test.tsx`.

### C.2 Clic en el estado abre la línea de tiempo

> **PDF p.3:** «al dar clic en el estado del trámite, me abra la línea de tiempo del detalle del trámite, no el tracking actual».

**Hoy:** el chip de la fila **no es clicable** (`StatusBadge` sin `onClick`, `TramitesTable.tsx:2253`); la fila abre el detalle con `onOpenDetalle(item)` (`:2385`). Dentro del modal, «Línea de tiempo del trámite» y «Trazabilidad de identidad» son botones toggle sobre `panelTracking` (`TramiteDetalleModal.tsx:204,400,473-495`) que pintan un `TimelineTrackPanel` a ancho completo (`:691-760`). El «tracking actual» que menciona el PDF es `TramiteTrackingModal` (`:1480-1503`), que se abre desde la columna Firmas (acción por parte, `:2147`).

**Diseño:** el chip de estado de la fila pasa a ser un `<button>` (label accesible «Ver línea de tiempo de {ref}») que llama `onOpenDetalle(item, { panel: 'timeline' })`; el modal acepta ese `initialPanel` y arranca con `panelTracking='timeline'`. El badge del header del modal también alterna la línea de tiempo al clic. `TramiteTrackingModal` no se toca: sigue siendo la acción de identidad por parte.

**Archivos:** `TramitesTable.tsx` (`:2253`, `:2385`, firma de `onOpenDetalle`), `TramiteDetalleModal.tsx` (prop `initialPanel`, header `:430-436`), `components/atom/StatusBadge.tsx` (variante `asButton` o wrapper).
**Tests:** `components/operacion/__tests__/detalle-consulta-secciones.test.tsx`, `TramiteTrackingModal.test.tsx` (debe seguir igual), `lib/tramites/__tests__/tramites-table-columns.test.ts`.

### C.3 Alerta de «pendiente por aprobación» en naranja corporativo

> **PDF p.3:** «mejorar esta alerta cuando hay algo pendiente en el trámite o cambiar el color a naranja corporativo #ff4e00 (el mismo de los botones de cancelar)».

**Hoy:** la alerta se pinta inline en el modal con `background: ${estadoHdr.color}1F` (`TramiteDetalleModal.tsx:650-672`), es decir, dorado al 12 % — **no** usa el `InlineAlert` compartido (`components/atom/InlineAlert.tsx`, tonos `error|warning|info|success`), que el mismo modal sí usa para rechazado/subsanación/revocatoria (`:535-648`). `#ff4e00` ya es token: `--color-flit-alert` (`globals.css:17`) y `--badge-danger-*` (`:119-121`).

**Diseño:** migrar la alerta a `InlineAlert` y añadirle un tono `pending` basado en `--color-flit-alert` (fondo al 12 %, borde y texto con contraste medido, icono `AlertTriangle`). Con eso desaparece el inline y la alerta de «pendiente» se ve igual en MI, traspaso y OTROS. Al desacoplar el color de la alerta del color del badge (C.1), el badge conserva el color del estado y la alerta el naranja.

**Decisión D6 (diseño):** ¿tono `pending` naranja o reutilizar `warning` dorado mejorando solo la jerarquía (icono + borde)? El PDF da las dos salidas; propuesta: **`pending` naranja**, porque el dorado ya significa «Traspasos» en los KPIs y «warning» en admin, y el naranja es el color que la plataforma usa para «requiere acción».

**Archivos:** `InlineAlert.tsx` (`:13`, `:31-35`, `:69`), `TramiteDetalleModal.tsx:650-672`, `globals.css` (variante dark del tono).
**Tests:** `components/atom/__tests__/InlineAlert.test.tsx` (nuevo tono), `detalle-consulta-secciones.test.tsx`.

### C.4 «No se ha actualizado el detalle de trámite»

> **PDF p.2:** «ajustes para ambas rutas MI / traspasos: no se ha actualizado el detalle de trámite».

Como el componente es único, no es un problema de UI de traspaso sino de **datos**: `statusHistory` / `detail` que el modal recibe al abrirse. Antes de tocar nada, la HU incluye una verificación acotada (`explore-agent`, 3 claims): ¿el detalle se vuelve a pedir al abrir el modal o reutiliza la fila cacheada? ¿se invalida la consulta tras un cambio de estado (radicar, aprobar, corregir placa)? ¿el historial que pinta `TimelineTrackPanel` viene de `mapStatusHistoryToTimelineNodes` con el mismo origen que la fila? Si es caché de React Query/SWR sin invalidación, el fix es un `refetch` al abrir + invalidación en las mutaciones; si el backend devuelve historial incompleto, se reclasifica a Bug (Caso B) con su `/fix-bug` y sale de este plan.

---

## Bloque D — Wizard de trámite (MI y traspaso)

### D.1 Paso 3: Prenda/Limitación y Observaciones en la misma fila

> **PDF p.4:** «verificar la viabilidad de unificar los contenedores de Asignación de Prenda / Limitación a la Propiedad y Observaciones, que queden los dos contenedores en la misma fila… no afectar la diagramación del contenedor trámite simultáneo; el de prenda diagramar a 3 columnas los campos de input y carga doc (a decisión del desarrollador)».

**Hoy:** ambos son `WizardAccordion` (h3) apilados en `grid grid-cols-1 gap-3` (`components/operacion/TramiteWizard.tsx:4744-4760`, segunda instancia `:4822-4826`); «Observaciones del trámite» está en **otro** acordeón al final del paso (`:4955-4957`), con la previsualización «Así quedarán en el FUR» (`:2817-2823`, `:2859`). Dentro de `PrendaForm.tsx:888-892` el bloque Acreedor / NIT / Documento **ya es `md:grid-cols-3`** cuando `muestraAcreedor`. «Trámites simultáneos» vive aparte en `DeclaracionesTramite.tsx:410` con su gate (`TramiteWizard.tsx:726-729`).

**Diseño:** mover el acordeón de Observaciones a la misma `WizardAccordionRow` que Prenda y cambiar la grilla a `grid-cols-1 lg:grid-cols-2 items-stretch`. La carga de documento de prenda («Certificado / registro de prenda») entra como tercera celda de la fila de 3 en `PrendaForm` para que en media pantalla siga cabiendo. `DeclaracionesTramite` no se toca. **Viabilidad:** sí — el único acoplamiento es que Observaciones lee las transformaciones para la previsualización del FUR, y eso no depende de su posición en el DOM.

**Archivos:** `TramiteWizard.tsx` (`:4744-4760`, `:4822-4826`, `:4955-4957`), `PrendaForm.tsx:888-892`, `WizardAccordion.tsx` (si hace falta `className` en la fila).
**Tests:** `PrendaForm.test.tsx`, `PrendaDocumentUpload.test.tsx`, `PrendaModificar.test.tsx`, `VehicleTransformationsCard.test.tsx`; snapshot del paso 3 si existe.
**Trampa:** en `< lg` vuelve a una columna; la previsualización «Así quedarán en el FUR» necesita ancho y no debe truncar valores largos (color / carrocería).

### D.2 Tarjeta de documento: «Opcional» en azul y el ⓘ de OCR arriba

> **PDF p.4:** «implementar la palabra opcional de color azul corporativo. El icono de i cuando hace lectura de OCR, ya que aparecía en la parte de abajo — ver icono y texto donde se debe ubicar (ojo: la nota informativa con icono i debe mantenerse igual)».

**Hoy:** `DocumentSlot` en `components/operacion/DocumentChecklist.tsx:689`. «Opcional» es `<span className="text-xs font-medium opacity-60">` (`:809`); la nota informativa lleva `<Info h-3 color #557EFF>` (`:826-834`) — esa se queda; el indicador OCR es `OcrStatusPanel` (`:498-623`, icono `Info h-4` con color por estado + modal de detalle) montado **debajo** de la tarjeta (`:867`).

**Diseño:** en la cabecera de la tarjeta, a la derecha: `Opcional` en `--flit-brand-ink` (D0) sin opacidad, seguido del icono ⓘ del OCR (el mismo `OcrStatusPanel`, reducido a su icono + tooltip, abriendo el mismo modal). La nota informativa de `:826` no cambia. El chip «Obligatorio» (`bg-red-50 text-red-600`, Tailwind de stock, drift ya documentado en `context/07-ux-ui.md`) se migra al token `--badge-danger-*` en el mismo paso porque comparte fila — cambio de dos clases.

**Archivos:** `DocumentChecklist.tsx` (`:797-809`, `:498-623`, `:867`).
**Tests:** no existe `DocumentChecklist.test.tsx` — **crearlo** (obligatorio/opcional/OCR en cabecera, nota intacta); `documentos-modo-consulta.test.tsx`.
**Trampa:** `DocumentSlot` también lo usa la escritura del RL (`:777`); la cabecera nueva debe verse igual allí.

### D.3 Validación de identidad: dos columnas, trazabilidad a una, botones azules

> **PDF p.5:** «diagramar a dos columnas si se hala la firma o cuando sale el QR Kyverum en pantalla también; trazabilidad, por la cantidad de info, debe ir diagramada a una columna; botones de acción secundaria son azul corporativo #557eff».

**Hoy:** `BiometricStep.tsx:381,560` ya usa `lg:grid-cols-2` **solo si hay más de una parte**; por debajo de `lg` (1024 px) apila — la captura del PDF muestra el apilado. El QR (`KyverumPendingView`, `QRCodeSVG size={120}`, `:1000-1060`) cabe en media columna. «Ver trazabilidad de validación» es un disclosure dentro de cada tarjeta (`:906-927`). Botones: el primario usa `WIZARD_CTA_GRADIENT` (`135deg #557EFF→#00DBD5`, `wizard-field-styles.ts:86`) y el secundario ya es borde+texto `#557EFF` (`:1435-1439`). Lo que el diseñador ve como «teal» son los CTA en degradado a tamaño pequeño («Validar identidad», «Reiniciar validación», «Confirmar y enviar», `:1203-1204`, `:1310-1359`, `:1459-1469`).

**Diseño:** bajar el breakpoint de dos columnas a `md` y aplicarlo también con una sola parte cuando aparece firma o QR (grilla `md:grid-cols-2` con la tarjeta de la parte y el bloque de captura como celdas hermanas). La trazabilidad sale del interior de la tarjeta a una **fila propia a ancho completo** (`col-span-full`) debajo de las partes, plegada por defecto. Los botones de acción **dentro** de las tarjetas pasan a sólido `#557eff` con texto blanco (≥ 3:1 como componente UI; el label en `font-semibold` 14 px) — el degradado queda reservado al CTA principal del wizard (Siguiente / Finalizar).

**Decisión D7 (diseño):** «secundario» = ¿sólido `#557eff` o outline `#557eff`? Propuesta: **sólido** para la acción principal de cada tarjeta (Validar / Reiniciar / Confirmar) y **outline** para Cancelar/Reintentar, coherente con `variant='secondary'` que ya existe.

**Archivos:** `BiometricStep.tsx` (`:381`, `:560`, `:906-927`, `:1203`, `:1310-1359`, `:1435-1469`), `wizard-field-styles.ts` (nuevo `WIZARD_BTN_BRAND`), `components/atom/IdentityValidationTrackingPanel.tsx` (si se extrae a fila).
**Tests:** `BiometricStep.multiple-propietario.test.tsx`, `BiometricStep.signature-card.test.tsx`, `IdentityValidationTrackingPanel.test.tsx`.

### D.4 Resumen: tarjetas a dos columnas alineadas con las de arriba

> **PDF p.6:** «esto se ve muy raro: solo estas dos columnas de prenda y OT ahí sin alineación con la de arriba; ajustar contenedores que queden dos columnas organizados, expandir los contenedores como los dos de arriba. MI también».

**Hoy:** `MatriculaResumen.tsx` (compartido por MI y traspaso vía `modalidad`, `:77`) mezcla tres grillas: Vehículo + partes en `lg:grid-cols-2` (`:1008-1009`); Transformaciones / Prenda / Organismo / Placa en **`lg:grid-cols-3`** (`:1136-1180` + `organismoRow` inyectado desde `FirmaFurStep.tsx:674-690`); Documentos cargados en `md:2 xl:4 2xl:6` (`ExpedienteVisor.tsx:216-231`). En traspaso solo hay Prenda + OT → dos celdas en una grilla de tres, huérfanas a la izquierda: exactamente la captura.

**Diseño:** la segunda grilla pasa a `lg:grid-cols-2`, alineada con la de Vehículo/partes. MI: Transformaciones · Prenda / Organismo · Placa (2×2). Traspaso: Prenda · Organismo (1×2). Si queda un número impar (p. ej. MI sin transformaciones), la última tarjeta hace `lg:col-span-2` para no dejar hueco. Documentos cargados conserva su grilla densa (son miniaturas, no tarjetas).

**Archivos:** `MatriculaResumen.tsx:1136-1180`, `FirmaFurStep.tsx:674-690` (`organismoRow`).
**Tests:** `MatriculaResumen.multiple-propietario.test.tsx`, `FirmaFurStep.copropietarios.test.tsx`.

### D.5 Expediente como acordeón

> **PDF p.7:** «contenedor expediente: implementar acordeón».

**Hoy:** `ExpedienteTimeline.tsx:45-48` es una `<section>` fija («Expediente — Trazabilidad cronológica del trámite»), visible solo en borrador desde `FirmaFurStep.tsx:817-821`. El único acordeón del wizard es `WizardAccordion` (`components/operacion/WizardAccordion.tsx:32-84`); no hay `Accordion/Collapsible` genérico en `components/ui` ni `components/atom`.

**Diseño:** la épica #12535 nombra el **Expediente consolidado** (`ExpedienteVisor.tsx:386`, la tarjeta con «Re-generar» y «Ver expediente consolidado (PDF)») y el PDF señala el contenedor «Expediente» cronológico; los dos van en `WizardAccordion` (h3): el consolidado `defaultOpen` (es la acción del paso) y el cronológico plegado con resumen en la cabecera («N eventos · último: {estado} {fecha}»). No se crea un primitivo nuevo: el wizard ya tiene el suyo y D.1 lo reutiliza.

**Decisión D8 (técnica, menor):** ¿aprovechar para extraer `WizardAccordion` a `components/atom/Accordion.tsx` y que `RepresentativeCompaniesAccordion` (admin) lo consuma? Propuesta: **no en esta ola** — es refactor sin cambio visible y engordaría el PR; queda anotado como deuda en `context/07-ux-ui.md`.

**Archivos:** `FirmaFurStep.tsx:804-821`, `ExpedienteVisor.tsx:386`, `ExpedienteTimeline.tsx`.
**Tests:** `FirmaFurStep.copropietarios.test.tsx`; añadir caso «acordeón plegado por defecto, se expande y lista eventos».

---

## Bloque E — Administración del OT (y auditoría transversal de admin)

### E.1 Quitar el «contenedor detrás» y unificar tablas

> **PDF p.7:** «en módulos de Reportes, Usuarios y Administración presenta el mismo problema de contenedores detrás y problema de diseño de tablas: ajustar mismas tablas y quitar contenedores».

**Hoy:** `OtHubLayout.tsx:37-160` pone `ModuleTitle` arriba y envuelve el contenido en `rounded-2xl border bg-card p-4 md:p-6` cuando `surface='panel'` (**el default**). Mandatos, client-procedures, imprint-validation, reportes y revocation-requests ya piden `surface='plano'`; **Usuarios, Motor de reglas y Documentos no**, y por eso muestran la card detrás. Tres patrones de tabla coexisten: `OtUsersSection` reutiliza `UsersTable` compartida con `RowActions` (bien) pero su tabla de eliminados es propia (`:381-391`); `RulesSection.tsx:90-105` es un `<table>` crudo con `border-spacing-y-2`, sin `Pagination`; `OtMandatosSection` usa `DataTable` + `RowActions`. Las piezas estándar existen desde las HUs #10493–#10495: `components/atom/DataTable.tsx`, `RowActions.tsx`, `Pagination.tsx`, `StatusBadge.tsx`, `OtTablePagination.tsx`.

**Diseño:**
1. `OtHubLayout`: el default de `surface` pasa a `'plano'`; se elimina la prop en las páginas que ya la pasaban y se revisa que ninguna dependa visualmente del panel (la card la ponen las secciones, no el layout).
2. `RulesSection` → `DataTable` + `StatusBadge` (ya lo usa) + `RowActions` (editar / toggle) + `OtTablePagination`.
3. Tabla de usuarios eliminados de `OtUsersSection:381-391` → `DataTable`.
4. Cabeceras de tabla: un solo estilo (`th` con token, no `color: #557EFF` inline como en `:391`).

**Archivos:** `components/admin/transit-offices/OtHubLayout.tsx`, `RulesSection.tsx`, `OtUsersSection.tsx`, `app/admin/transit-offices/[id]/{usuarios,rules,documents,mandatos,…}/page.tsx`.
**Tests:** `OtHubLayout.test.tsx`, `RulesSection.test.tsx`, `OtUsersSection.*.test.tsx` (8 specs), `DataTable.test.tsx`.

### E.2 Mandatos y Motor de reglas: las columnas de detalle se van al panel

> **PDF p.8:** «este tipo de columnas no dejarlas en el sistema, aplicar modal en su reemplazo».

**Hoy:** `OtMandatosSection.tsx:141-182` pinta `DataTable` con NIT / Empresa / **Mandatario / Tipo doc. / N.º documento / Hash** / Acción, y una segunda tabla «general» (`:212-237`) con NIT / Organismo / Mandatario. Ya existe un panel lateral «Configurar mandatario» (captura p.8, QA) y `MandatarioFirmaPreviewDialog.tsx` es modal.

**Diseño:** ambas tablas se reducen a **NIT · Empresa/Organismo · Mandatario (nombre o «Sin definir») · Acción**; tipo y número de documento y hash se muestran **dentro** del panel «Configurar mandatario» (bloque de solo lectura «Mandatario actual» con hash truncado + copiar). El botón de acción abre ese panel; no se crea un modal nuevo.

**Motor de reglas (CF de la épica #12535, no estaba en el PDF como texto):** `RulesSection.tsx:90-105` pinta Nombre · **Lógica · Acción** · Estado · Toggle. Las columnas Lógica y Acción (que crecen con cada condición) salen de la tabla; la fila queda **Nombre · Estado · Toggle · Acción (editar)** y la edición abre el panel «Nueva regla / Editar regla» que ya existe (captura p.9), precargado. Se hace junto con E.1 porque la tabla se migra a `DataTable` en el mismo cambio.

**Archivos:** `OtMandatosSection.tsx:141-182,212-237`, panel de configuración de mandatario (`components/admin/transit-offices/…Mandatario…`), `MandatarioFirmaPreviewDialog.tsx` si se reutiliza el preview de hash; `RulesSection.tsx` + panel de regla.
**Tests:** `OtMandatosSection.test.tsx` (columnas nuevas; hash ya no en tabla; hash sí en panel), `RulesSection.test.tsx` (sin columnas Lógica/Acción; editar abre panel precargado).
**Trampa:** el hash es dato de auditoría (baúl de firmas, HU #11170): no eliminar su visualización, solo moverla.

### E.3 Prelación: switches a tres columnas

> **PDF p.9:** «en Administración OT — Documentos y prelación, colocar todos los suiches a 3 columnas».

**Hoy:** `PledgeDocumentOverrideToggle.tsx:100-105`: `<ul className="space-y-2">` con un `<li rounded-xl border px-3 py-2>` + `ToggleSwitch` (`label="{tenant} — Prenda opcional"`) por compañía, una por fila a ancho completo. Segundo consumidor: `components/admin/documents/tabs/OtOverridesTab.tsx`.

**Diseño:** `ul` → `grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-2`; cada `li` conserva borde y switch. El label pasa a dos líneas (nombre arriba, «Prenda opcional» debajo en `text-xs`) para que quepa en un tercio. `OtOverridesTab` hereda el cambio (misma pantalla desde el módulo Documentos).

**Archivos:** `components/admin/documents/panels/PledgeDocumentOverrideToggle.tsx:100-105`.
**Tests:** `panels/__tests__/PledgeDocumentOverrideToggle.test.tsx`, `DocumentsSection.test.tsx`.

### E.4 Auditoría transversal del módulo de administración

> **PDF p.7 (rojo):** «considerar la aplicación de mejoras en el módulo de administración: aplicar las mismas indicaciones recibidas durante todo el desarrollo en este y otros documentos compartidos».

Última HU del plan, con checklist cerrado (no «mejorar todo»): para cada página bajo `app/admin/**` y los módulos globales Reportes y Usuarios verificar (1) sin card detrás del contenido, (2) `ModuleTitle` con `action` como en #10493, (3) tablas con `DataTable`/`RowActions`/`Pagination`/`StatusBadge` (#10494–#10495), (4) colores por token (sin `#557EFF`/`#FF4E00` inline nuevos; los ~40 archivos con `ring-[#557EFF]` se dejan — son focus rings), (5) botones secundarios en azul (D7), (6) dark mode íntegro. Salida: tabla `página · hallazgo · corregido/diferido` en el propio commit; lo diferido se anota en `context/07-ux-ui.md`.

---

## Decisiones abiertas (resumen)

| # | Decisión | Propuesta | Quién |
|---|---|---|---|
| D0 | Hex de `--flit-brand-ink` (texto pequeño ≥ 4,5:1) | Oscurecer `#557eff` manteniendo matiz; documentar ratio en el token | Diseño + frontend-agent |
| D1 | Ítem activo del dock: ¿igualar al degradado del isotipo o icono+texto azul sin fondo? | **Azul sin fondo** (opción explícita del PDF) | Diseño |
| D2 | «Ayuda»: ¿eliminar o reubicar en el menú de usuario? | **Cerrada (PO 2026-09-21): al menú de usuario** | PO |
| D3 | KPIs del dashboard: 2×2 (quitar «Otros») · 2×3 · 3+2 | **Cerrada (PO 2026-09-21): 2×2** — «Otros trámites» se absorbe en Total | PO |
| D4 | Calendario de rango: `react-day-picker` (auditar) o propio | **`react-day-picker`** — el PO delega en la propuesta técnica; auditoría (regla 18) dentro de la HU | Líder Técnico |
| D5 | ¿Recolorear «Entregado» a verde `#8cc63f`? | **Cerrada (PO 2026-09-21): no** — el detalle usa los mismos colores de badge que los DataTable (`estados.ts`) | PO |
| D6 | Alerta «pendiente»: tono `pending` naranja o `warning` dorado mejorado | **Cerrada por la épica #12535: naranja `#ff4e00`** (`--color-flit-alert`) | Diseño |
| D7 | Botones de acción en tarjetas de validación: sólido o outline `#557eff` | **Sólido** para la acción principal de la tarjeta, outline para cancelar | Diseño |
| D8 | Extraer `WizardAccordion` a primitivo compartido | **No en esta ola** (deuda anotada) | Líder Técnico |

---

## Descomposición propuesta

Feature **#12722** `[FRONTEND] - Ajustes UI · observaciones de diseño 2026-09-07 (épica #12535)` **hijo de la épica #12535**, con 10 HUs `[FRONTEND]` en **Sprint 8** (activo = Sprint 7, 15–22 sep), tags `adopcion-ia; DOR`, asignadas a humano, sin activar. Orden = orden del PDF; ninguna HU depende de otra salvo donde se indica.

| # | HU | Bloque | SP | Depende de |
|---|---|---|---|---|
| 1 · **#12723** | Token `--flit-brand-ink` + dock: ítem activo sin fondo en azul, «Ayuda» fuera, «Usuarios» a la derecha, lados declarados | D0, A.1, A.2 | 3 | D0, D1, D2 |
| 2 · **#12724** | `DateRangePicker` de un solo campo y migración de `DateRangeFilter` (dashboard, reportes OT, ICT) | B.2 | 5 | D4 |
| 3 · **#12725** | Dashboard: banner con alto estándar, fila de filtros `[rango][🔍 compañía]`, KPIs en dos filas | B.1 | 2 | HU 2, D3 |
| 4 · **#12726** | Detalle de trámite: badge con la paleta de `estados.ts`, clic en estado abre línea de tiempo, alerta «pendiente» en `InlineAlert` naranja, verificación del refresco del detalle | C.1–C.4 | 5 | D5, D6 |
| 5 · **#12727** | Wizard paso 3: Prenda/Limitación + Observaciones en dos columnas; prenda a 3 columnas con carga de documento | D.1 | 3 | — |
| 6 · **#12728** | Tarjeta de documento: «Opcional» azul + ⓘ OCR en cabecera; chip Obligatorio por token; `DocumentChecklist.test.tsx` | D.2 | 2 | HU 1 (token) |
| 7 · **#12729** | Validación de identidad: dos columnas desde `md` (también con QR/firma), trazabilidad a fila completa, botones azules | D.3 | 3 | D7 |
| 8 · **#12730** | Resumen a dos columnas alineadas (MI y traspaso) + Expediente consolidado y cronológico como acordeón | D.4, D.5 | 3 | — |
| 9 · **#12731** | Admin OT: `surface` plano por defecto, `RulesSection` y usuarios eliminados a `DataTable`; Mandatos y Motor de reglas sin columnas de detalle (al panel); Prelación a 3 columnas | E.1–E.3 | 5 | — |
| 10 · **#12732** | Auditoría transversal de admin/Reportes/Usuarios con checklist cerrado | E.4 | 3 | HU 1–9 |

**Total: 34 SP.** Si se prefiere ≤ 30 por sprint, la HU 10 pasa al siguiente sin romper nada.

---

## Entrega

- **Rama única** `feature/AB-12722-ajustes-ui-observaciones` desde `develop`, **un commit por HU** (`HU####: …`), como en las olas anteriores (#10491, #11254).
- **PR a `develop`.** Estimación del diff: ~35 archivos de producto + tests; probablemente **> 800 líneas** por la migración del filtro de fechas y de `RulesSection`. Si se supera, declarar el exceso en el cuerpo del PR con el desglose (producto / tests / snapshots) y pedir la excepción del Líder Técnico, o abrir dos PRs: (HU 1–4) y (HU 5–10).
- Gates: activación de cada HU con «sí» explícito; merge con reviewer humano real; `Resolved` solo con PR `MERGED`.
- `docs/arbol-menu-por-rol.md` y `context/07-ux-ui.md` se actualizan **en el mismo PR** (dock nuevo, tono `pending`, token `--flit-brand-ink`, deuda D8).

## Verificación

- Por HU: `pnpm --filter frontend typecheck`, ESLint **scoped al diff**, y tests **filtrados por símbolo tocado** (cada clase/constante/ruta del diff → `grep -rl` en `__tests__` → todo al `--filter`); los specs de `Shell.*`, `Dashboard`, `estados-catalogo`, `OtHubLayout` van siempre que se toque su dominio.
- Evidencias `dev-tester` en `Custom.Evidences`: capturas **light y dark** de cada superficie (dock activo, dashboard, detalle con timeline, paso 3, validación con QR, resumen MI y traspaso, Admin OT Usuarios/Reglas/Mandatos/Prelación) a 1280 y 1024 px.
- Contraste medido (`--flit-brand-ink`, tono `pending`, botones sólidos) anotado en la HU 1 y la HU 4.
- `pnpm build` verde antes de abrir el PR; CI `Lint, type-check, build` + `dependency-audit` (por `react-day-picker`, si D4=a).
- Validación manual del diseñador sobre DEV contra el PDF, página por página, antes de `Resolved`.
