# Plan técnico — DR-FLIT v3: filtros unificados, ayuda por rol y manual v2

> Generado: 2026-09-21 · rama `feature/dr-flit-mejoras-v3` @ `7273bb43` (base `develop`).
> Diagnóstico por lectura estática del código + ejecución de la suite actual (37/37 verde).
> Las anclas `archivo:línea` derivan con cualquier commit: reverificar antes de citarlas.

> **Premisa vigente (leer antes de continuar este desarrollo):** la Resolución 20233040017145 de
> 2023 del Ministerio de Transporte es la **base del criterio** con el que DR-FLIT responde, no un
> apartado más del manual. El chip «Normativa» es solo la puerta de entrada explícita; la norma debe
> sostener cualquier respuesta —búsquedas, estados, requisitos, ayuda contextual, mensajes de
> error— aunque el usuario nunca abra esa sección ni pregunte por la ley. Detalle y reglas de
> aplicación en **§6**.

---

## 0. Resumen ejecutivo

DR-FLIT (`frontend/components/dr-flit/`, ~3.050 líneas) es un asistente conversacional **UI-only**
sobre APIs existentes: máquina de estados pura (`dr-flit-conversation.ts`), hook (`useDrFlitChat`),
capa de búsqueda (`dr-flit-search.ts`) y base documental estática (`frontend/lib/manual/`, 17
artículos, v1.1.0 Ago-2026). La arquitectura es sólida y testeable; **no requiere reescritura**.

El problema es de **desfase**: desde su última evolución funcional (`ed8e431d`) el repo acumuló
**652 commits** que DR-FLIT no absorbió — búsqueda libre transversal (`busqueda`, HU12187/12218),
historial por placa (HU12194), alcance de red (HU12363/12652), estados reales de la ruta de placa y
revocatorias (ADR-0059, F12565), catálogo canónico de copy (HU12693), formato estándar de fecha
(HU12664) y una docena de módulos nuevos sin documentar.

El plan tiene **4 fases / 8 HUs**. Fase 0 es prerequisito técnico; Fase 1 es el objetivo funcional
(placas, VIN, trámites, clientes); Fase 2 es la opción de ayuda; Fase 3 es la evolución documental y
corre en paralelo desde Fase 0.

---

## 1. Estado actual verificado

| Capacidad | Estado | Implementación hoy |
|---|---|---|
| Buscar por placa / VIN | Funcional | `listInstances({placa\|vin})` tenant; `fetchOtClientProcedures` si `ot_admin` |
| Buscar por trámite | **Degradado** | Exige GUID (`dr-flit-search.ts:36`); el usuario conoce el radicado |
| Buscar por cliente | Parcial | 2–4 llamadas substring por nombre comprador/vendedor; **no por documento** |
| Ayuda → Necesito ayuda | Desactualizado | Keywords sobre 17 artículos; sin filtro por rol |
| Ayuda → Soporte | Placeholder | `DR_FLIT_SUPPORT_PHONE = "300 000 0000"` |
| Persistencia / accesibilidad | OK | `sessionStorage`, foco gestionado, panel no modal |

### 1.1 Brechas de búsqueda

1. **Radicado vs GUID.** El backend ya expone `busqueda` (radicado exacto + placa/VIN/nombre/documento/OT
   por subcadena) en `GET/POST /api/v1/tramites/instances[/search]` y en la bandeja OT. DR-FLIT no lo usa.
2. **Cliente por documento**: `busqueda` lo cubre; hoy no se consulta en trámites.
3. **Estados nuevos** (`preasignacion`, `asignado`, `revocado`, sub-estados de revocatoria): el chip
   no rompe (fallback), pero la conversación no los explica ni sugiere acción.
4. **Historial por placa** (`?m=historial-placa&placa=X`, HU12196) no se ofrece desde el resultado.
5. **Alcance de red** (cabeza AdminCompany Concesión/Marca Blanca): DR-FLIT solo busca en la propia
   compañía; existe `searchNetworkInstances` (`POST /tramites/network/instances/search`).
6. **SuperAdmin**: sin tratamiento explícito de rol/tenant.
7. **Copy y fechas**: labels fuera de `lib/copy/copy-catalog.ts`; `dateOnly()` corta ISO en vez de
   usar `formatFechaHora` (DD/MM/YYYY HH:mm, hora Colombia).
8. El resultado **no muestra radicado** (`referenceNumber` sí viene en `InstanceSummary`).

### 1.2 Brechas documentales (manual vs menú real en `Shell.tsx` / `dockGroups.ts`)

| Perfil | Documentado | Sin documentar (existe en producto) |
|---|---|---|
| Gestor | Inicio, Crear trámite, Documentos, Prevalidación, Seguimiento, DR-FLIT | Identidad, Historial por placa, Revocatorias (solicitar), Reportes, Reportes detallados, Usuarios, búsqueda libre y gramática de Consultas (HU12106/12187), ruta de placa RUNT corta/larga (HU12648-50), «Enviar al OT», Términos y condiciones (Epic 12543), fecha/hora Colombia |
| OT | Bandeja, Preasignación, Reportes, Usuarios, Reglas, Documentos, Requisitos, DR-FLIT | Mandatos, Validar impronta, Configuración (ventana revocatoria), decidir revocatoria, bandeja por estado real + «Liberar placa» (HU12598-602) |
| AdminCompany | — | Administración / Mi empresa, Red de clientes, Generación documental, configurador de marca (F12366-12370) |
| SuperAdmin | — | Compañías, Tránsito, Documental, Improntas, Quipux, Procesos periódicos, RBAC, Auditoría, Plataforma (Tipos de trámite, Mandatos, FUR, Notificaciones, Confirmación RUNT, Banners), Log QX, ICT |

Además: el buscador del manual depende de `keywords` curadas por artículo y no filtra por audiencia
del usuario; `docs/arbol-menu-por-rol.md` (2026-08-04) está desactualizado.

---

## 2. Plan por fases

```
Fase 0 (HU-A) ──► Fase 1 (HU-B, HU-C, HU-D en paralelo) ──► HU-E
                  Fase 3 (HU-H contenido) ═══════════════► Fase 2 (HU-F, HU-G)
```

Estimación relativa: Fase 0+1 ≈ 40 %, Fase 2 ≈ 20 %, Fase 3 ≈ 40 %.

### Fase 0 — Cimientos

**HU-A · Contrato de búsqueda unificado y contexto de rol**
- `dr-flit-context.ts`: resuelve `{ role: superadmin|ot_admin|admin_company|gestor, tenantId,
  network: {active, childTenantId} }` desde JWT (`lib/auth/jwt.ts`) + `useNetworkScope`.
- `dr-flit-search.ts`: un solo `searchTramites(intent, valor, ctx)` que enruta por contexto
  (OT → bandeja; red activa → `searchNetworkInstances`; resto → `searchInstances` POST con `total`).
  Cliente → una llamada `busqueda` (nombre o documento) en lugar de 2–4.
- `DrFlitTramiteResult` gana `radicado` y `compania`; `fecha` usa `formatFechaHora`.
- El resultado devuelve `{ items, total }`; la conversación informa cuando hay más de los mostrados.
- Tests: `dr-flit-search.test.ts` por rol × intent; `dr-flit-context.test.ts`.

### Fase 1 — Filtros

**HU-B · Trámite por radicado** — acepta radicado (exacto vía `busqueda`) o GUID (`getInstance`).
Prompt «Indícame el número de radicado». Sin error de GUID.

**HU-C · Placa / VIN enriquecidos** — chip «Ver historial completo de la placa» →
`?m=historial-placa&placa=`; etiqueta de compañía hija en alcance de red; mensaje de total.

**HU-D · Cliente por documento o nombre** — rama Trámites usa `busqueda`; rama Identidad mantiene
`listTenantBiometricValidations`; detección automática documento/nombre (`looksLikeDocument`).

**HU-E · Copy canónico y estados** — labels desde `copy-catalog.ts`; texto por estado
(`preasignacion`, `asignado`, `revocado`, revocatoria activa) con acción sugerida.

### Fase 2 — Ayuda

**HU-F · Ayuda consciente del rol** — `searchManualArticles(query, {audience})`; `ManualAudience`
ampliado a `AdminCompany` | `SuperAdmin`; datos reales de soporte desde configuración; FAQ de
`Ayuda.tsx` actualizada.

**HU-G · Sugerencias contextuales** — primer chip de «Necesito ayuda» = artículo del módulo activo
(`routeScope` ya llega al hook); mapa `moduleId → slug` en `lib/manual/articles/meta.ts`.

### Fase 3 — Documentación

**HU-H · Manual v2.0** — secciones `admin-company` y `superadmin`; ~20 artículos nuevos (tabla §1.2)
+ actualización de los 17 existentes; `keywords` validadas por test («toda pregunta de referencia
devuelve el artículo esperado»); `MANUAL_VERSION = "2.0.0"`; regenerar `docs/arbol-menu-por-rol.md`.

---

## 3. Riesgos y decisiones abiertas

| # | Decisión | Recomendación |
|---|---|---|
| D1 | ¿Documentar SuperAdmin? | Sí, como sección interna filtrada por rol en DR-FLIT. AdminCompany es obligatorio (es cliente). |
| D2 | ¿Ayuda con LLM? | Mantener keyword-matching (viable con ~40 artículos); LLM como Fase 4 opcional. |
| D3 | ¿Cabeza de red busca trámites de hijas desde DR-FLIT? | Sí, siguiendo el alcance guardado del usuario (`tramites.scope`), igual que la tabla. Radicador de la cabeza: solo propia (HU12652). |
| D4 | Datos reales de soporte | Obtener de negocio antes de HU-F. |

Rendimiento: `busqueda` con `take: 20` sustituye hasta 4 llamadas por 1 — mejora neta.

---

## 4. Validación

- Cada HU cierra con `pnpm test` (suites `components/dr-flit`, `lib/manual`), `pnpm typecheck`,
  `pnpm lint` y verificación manual en la app por rol (Gestor, OT, AdminCompany cabeza, SuperAdmin).
- Prohibido romper el contrato del mock de `DrFlitAssistant.test.tsx` sin actualizar sus casos.

## 5. Bitácora de ejecución

| Fecha | HU | Estado | Notas |
|---|---|---|---|
| 2026-09-21 | HU-A | Implementada · validada por API | `dr-flit-context.ts` (rol efectivo + red), `dr-flit-search.ts` unificado por contexto, cliente → 1 llamada `busqueda`, `radicado` + `compania` + `formatFechaHora`, `{items,total}`. Validado contra backend local: SuperAdmin `POST /instances/search` devuelve `total` y `companiaNombre`; **hallazgo**: la bandeja OT por GET ignora `busqueda` → se usa `searchOtClientProcedures` (POST). Alcance de red sin dato en seed local (validar en DEV). |
| 2026-09-21 | HU-B | Implementada · validada por API | Radicado con prefijo (`FT1-0000003`, `ft1 0000003`) o consecutivo (`3`) → coincidencia exacta confirmada en backend; GUID sigue aceptado; `esMismoRadicado` filtra ruido de subcadena; prompt del intent ya no menciona GUID. |
| 2026-09-21 | HU-C | Implementada | Chip «Ver historial completo de la placa» → `?m=historial-placa&placa=X`, gateado por `visibleModuleCodes` (prop `historialPlacaEnabled` desde `Shell`) y excluido para `ot_admin`; compañía por fila y mensaje de total ya venían de HU-A. |
| 2026-09-21 | HU-D | Implementada | Documento con puntos/espacios se compacta antes de `busqueda` (`normalizeClienteQuery`); rama Identidad intacta. `busqueda` por nombre confirmada en backend («Renting» → 2). |
| 2026-09-21 | HU-E | Implementada | Etiquetas de menú y tarjeta desde `copy-catalog.ts` (Placa, VIN, Trámite, Radicado, Fecha radicación); `dr-flit-estados.ts` con pista de acción por estado (ruta de placa, subsanación, revocado). |
| 2026-09-21 | HU-F | Implementada | `ManualAudience` + `Admin de Compañía` / `Super Admin`; `ManualProfile` y `visibleAudiences(perfil)` en `lib/manual/audience.ts`; `searchManualArticles(query, limit, {audiences})`; DR-FLIT filtra por rol efectivo (el portal `/manual` sigue sin filtro). Soporte por env `NEXT_PUBLIC_DR_FLIT_SUPPORT_{EMAIL,PHONE,CASE_URL}`: el teléfono placeholder «300 000 0000» deja de mostrarse hasta que negocio lo defina (D4). FAQ de `Ayuda.tsx` actualizada. |
| 2026-09-21 | HU-G | Implementada | `MANUAL_MODULE_ARTICLES` / `MANUAL_PATH_ARTICLES` en `meta.ts` (test: todo slug existe); `resolveContextArticle(routeScope, audiences)` (hub OT por segmento de URL → ruta de página → módulo `?m=` solo en `/`); «Necesito ayuda» ofrece el artículo del módulo actual como primer chip sin cerrar la pregunta libre. |
| 2026-09-21 | HU-H · bloque Gestor | Implementado | 6 artículos nuevos (`7-ruta-placa`, `8-identidad`, `9-historial-placa`, `10-revocatorias`, `11-reportes`, `12-usuarios`) + `0-introduccion/4-fechas-y-horas` (Todos); actualizados `2-crear-tramite` (T&C, carga masiva, ruta de placa), `5-seguimiento` (búsqueda libre, Periodo, Consultas, 10 estados reales + distintivo, detalle), `6-ayuda-dr-flit` (radicado, historial, ayuda por rol, soporte) e intro «Perfiles» (ya no dice que admin queda fuera). Mapa HU-G partido en `MANUAL_MODULE_ARTICLES` (SPA) y `MANUAL_OT_TAB_ARTICLES` (hub) por colisión `usuarios`/`reportes`; módulos Identidad, Historial, Reportes, Usuarios y ruta `/tramites/revocatorias` enlazados. Nuevo `search-coverage.test.ts`: 31 preguntas de referencia → slug esperado (todas verdes). Portal `/manual` sirve los artículos nuevos (200). Pendiente del bloque: bumps de `MANUAL_VERSION` al cerrar Fase 3. |
| 2026-09-21 | HU-H · bloque OT | Implementado | 4 artículos nuevos (`9-revocatorias` decisión, `10-mandatos`, `11-validar-impronta`, `12-configuracion` modo FLIT/Quipux + ventana + flags); reescrito `1-tramites-bandeja` (7 contadores reales, filtros/búsqueda/exportar, cola de placa: asignar del rango o fuera, corregir 1 h, liberar; decisión: LT con OCR, mandatario, causales por familia, Quipux solo lectura); actualizados `2-preasignacion` (el gestor ya no elige placa) y `8-ayuda-dr-flit` (radicado, alcance bandeja, ayuda por rol). Pestañas `revocation-requests`, `mandatos`, `imprint-validation`, `configuracion` enlazadas en `MANUAL_OT_TAB_ARTICLES`. Cobertura: +13 preguntas OT (44 en total) y garantía «el OT no recibe artículos del Gestor». 135 tests verdes; `tsc`/`eslint` limpios; portal sirve los nuevos (200). |
| 2026-09-21 | HU-H · bloque AdminCompany | Implementado | Sección nueva `admin-company` («Administración de compañía», audiencia «Admin de Compañía») con 5 artículos: `1-consola` (pestañas reales, políticas por familia, proveedores, notificaciones, lista blanca, organismos, «Guardar todo» con confirmación), `2-representantes-mandatarios` (fichas, escrituras, baúl, reglas de mandatarios por organismo/empresa, convenio), `3-red-de-clientes` (Concesión vs Marca Blanca, panel de red, alcance Mi compañía/Toda la red/cliente, solo consulta, radicador sin alcance), `4-marca-y-dominio` (configurador, borrador/publicar, herencia, dominio + TXT + estados + certificados), `5-generacion-documental` (RUES, transferencia con causales A/B/C, carga masiva, historial). Mapa contextual por sufijo (`/admin/companies/{id}` → consola, `/children` → red) y `/admin/generacion-documental`. Cobertura +13 preguntas (57 en total) y garantía de audiencias. 150 tests verdes; `tsc`/`eslint` limpios; sidebar del portal muestra la sección. |
| 2026-09-21 | HU-H · bloque SuperAdmin | Implementado | Sección `superadmin` («Super Admin») con 6 artículos por tema: `1-companias-y-organismos`, `2-documental-e-improntas`, `3-plataforma` (tipos de trámite con capacidades/recorrido, mandatos, FUR, notificaciones, Confirmación RUNT, banners), `4-integraciones-y-procesos` (Quipux, Log QX, ICT, jobs ADR-0059, migración), `5-rbac-y-auditoria`, `6-dr-flit-global`. Rutas `/admin/*` y módulos `rbac`/`auditoria`/`log-qx`/`ict*` mapeados; resolución por sufijo antes que por prefijo (ficha de compañía gana al catálogo). Cobertura +16 preguntas (73 en total). |
| 2026-09-21 | HU-H · cierre | **Fase 3 cerrada en código** | `MANUAL_VERSION = 2.0.0` / «Septiembre 2026» (17 → 39 artículos, 5 secciones, 4 audiencias). `docs/arbol-menu-por-rol.md` regenerado (árboles de dock reales por rol, matriz con Historial/Revocatorias/Red/Generación documental/Procesos/Plataforma, y §11 mapa menú → artículo). Suites dr-flit + manual: 166/166. **Suite completa del frontend**: 33 archivos en rojo con mis cambios vs 32 en `develop` limpio (`git stash`); los 2 que difieren (`hu10874-subsanacion-wizard`, `TransferenciaEscenariosBYC`) pasan 20/20 aislados en dos corridas → flaky por concurrencia, no regresión. Los 95 fallos heredados no se tocan. `tsc` y `eslint` limpios en lo modificado. |
| 2026-09-21 | E2E en navegador | **35/35 PASS** | Playwright + Chromium local (la extensión de Chrome no alcanzó `localhost`). 4 roles + portal público, happy path y bordes. Evidencia: `docs/qa/dr-flit-v3-e2e-2026-09-21.md` + script `docs/qa/dr-flit-v3-e2e.playwright.js`. Tres hallazgos corregidos en la misma sesión: GUID fuera de alcance → mensaje accionable; `/tramites` sin barra → sugerencia contextual; bienvenida del manual desfasada → actualizada + test de guardia. Unitarias 168/168, `tsc`/`eslint` limpios. |
| 2026-09-21 | Normativa · fuente principal | Implementada · E2E 40/40 | PDF `Resolución 20233040017145 de 2023 (MinTransporte)` copiado a `frontend/public/legal/resolucion-20233040017145-2023-mintransporte.pdf` (servido en `/legal/…`, copia única del binario; `docs/legal/README.md` apunta a ella). `ManualArticle.sources` + `primarySource`; nueva sección **Normativa** (tras Introducción) con artículo `5-normativa/1-resolucion-20233040017145-2023` (audiencia Todos, resumen por temas con citas: 5.1.1, 5.1.2/5.1.3/5.1.16, 5.1.10–5.1.14, 5.3.1.1, 5.3.2.1/5.3.2.14, improntas/QR). Buscador: bonificación de fuente principal solo cuando casa (no secuestra preguntas operativas). DR-FLIT: opción **Ayuda → Normativa**, tarjeta destacada con «Abrir la norma (PDF)», enlace a fuente en cualquier resultado con `sources`. `Ayuda.tsx` con tarjeta Normativa. Portal renderiza bloque «Fuentes». `MANUAL_VERSION = 2.1.0`. Unitarias 478/478 (dr-flit, manual, atom/modules); E2E ampliado a 40 casos, 40/40. |
| — | Fase 2 | **Cerrada en código** | 389 tests verdes (dr-flit, lib/manual, atom/modules); `tsc` y `eslint` limpios. D1 aplicada como recomendación: audiencias de administración existen y se filtran por rol; el contenido llega en Fase 3. |
| — | Fase 1 | **Cerrada en código** | Tests DR-FLIT 37 → 72 verdes; `tsc` y `eslint` limpios. Pendiente: validación visual en navegador (la extensión de Chrome no alcanzó `localhost`). |

---

## 6. Premisa vigente y handoff para el siguiente tramo

### 6.1 La normativa es la base del criterio, no una sección

La **Resolución 20233040017145 de 2023 (MinTransporte)** —que modifica la 20223040045295 de 2022 y
habilita la virtualidad de los trámites del Registro Nacional Automotor— es la norma que avala a
FLIT y, por tanto, la **fuente principal** del conocimiento de DR-FLIT.

Quedó implementada como fuente de primera clase (`ManualArticle.primarySource` + `sources`, PDF
servido en `/legal/resolucion-20233040017145-2023-mintransporte.pdf`, sección `5-normativa`,
chip **Ayuda → Normativa**). **Eso es el piso, no el techo:** la sección «Normativa» es la puerta
más visible, pero el objetivo es que la norma se note en el *criterio* de las respuestas, no en que
el usuario tenga que ir a leerla.

### 6.2 Reglas de aplicación (obligatorias para las próximas mejoras)

1. **Criterio primero, cita después.** La respuesta útil va en el lenguaje del usuario
   («necesitas SOAT vigente y paz y salvo SIMIT»); el sustento legal acompaña como refuerzo
   (enlace a la fuente, artículo citado), nunca como un párrafo normativo que el usuario deba
   descifrar. Ningún flujo debe obligar a pasar por «Normativa» para obtener una respuesta correcta.
2. **Todo contenido con origen normativo cita su artículo.** Si una regla documentada nace de la
   resolución (requisitos de matrícula, 60 días de preasignación, autenticación RUNT, improntas/QR,
   carpeta digital, especies venales, corrección de errores de digitación…), el artículo del manual
   debe declararlo en el cuerpo y enlazar `sources` con el PDF. Así el asistente puede respaldar la
   afirmación cuando lo cuestionen.
3. **La norma acompaña, no desplaza.** `PRIMARY_SOURCE_BONUS` solo suma cuando la consulta casa con
   la norma: «cómo creo un trámite» sigue devolviendo el how-to primero y «qué dice la norma sobre
   la preasignación» devuelve la resolución. Esa garantía está cubierta por
   `lib/manual/__tests__/normativa.test.ts`; cualquier cambio de ranking debe mantenerla verde.
4. **Prevalece la norma.** Si una pantalla de FLIT contradice la resolución, la norma manda: el
   contenido debe decirlo y encaminar a soporte para corregir la plataforma.
5. **Si la norma cambia, cambia el paquete completo:** PDF en `frontend/public/legal/`, resumen del
   artículo `5-normativa/1-…`, `ref` del Diario Oficial y las citas dispersas en los artículos.
   `normativa.test.ts` verifica que el PDF exista y no esté vacío.
6. **Alcance:** audiencia «Todos». La normativa no se filtra por rol; lo que se filtra es el
   contenido operativo.

### 6.3 Ideas naturales para el siguiente tramo (no implementadas)

- Sustento legal contextual en los resultados de trámite (p. ej. en `preasignacion`, la pista de
  estado podría enlazar el art. 5.3.1.1 —60 días— desde `dr-flit-estados.ts`).
- Respuestas de requisitos por familia de trámite (matrícula / traspaso) construidas desde la norma.
- Extender `sources` a otras normas cuando entren (una fuente por artículo, mismo patrón).
- Fase 4 opcional (D2): capa LLM sobre el manual; con `primarySource` ya marcado, la norma es el
  documento a priorizar en el contexto del modelo.

### 6.4 Estado al momento del handoff

- 4 fases / 8 HUs (A–H) cerradas en código; manual `MANUAL_VERSION = 2.1.0`, 40 artículos,
  6 secciones, 4 audiencias.
- Unitarias de lo tocado: **478/478** (`components/dr-flit`, `lib/manual`, `components/atom/modules`).
  `tsc --noEmit` y `eslint` limpios en lo modificado. La suite completa del frontend mantiene los
  fallos heredados de `develop` (32 archivos); comparación registrada en §5.
- E2E en navegador: **40/40** (`docs/qa/dr-flit-v3-e2e-2026-09-21.md`, script reejecutable en
  `docs/qa/dr-flit-v3-e2e.playwright.js`, requiere `pnpm dev` y un Chromium de Playwright).
- **Sin validar en local por falta de datos en el seed:** alcance de red de una cabeza (no hay grupo
  padre), chip de historial con resultados (los trámites del seed no tienen placa) y el teléfono real
  de soporte (D4, `NEXT_PUBLIC_DR_FLIT_SUPPORT_PHONE`). Verificar en DEV.
- El resumen normativo del artículo lo redactó el equipo de desarrollo a partir del texto oficial:
  **conviene revisión legal antes de producción**; el PDF vinculante está siempre a un clic.
