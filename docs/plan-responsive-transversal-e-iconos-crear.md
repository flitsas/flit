# Plan — Responsive transversal + rediseño de íconos "crear/agregar"

| Campo | Valor |
|---|---|
| **Feature base** | #10813 (deriva de `docs/FEATURE-09-ui-transversal-y-reportes.md`, ítem **R09 — barrido global de responsive**) |
| **Alcance** | Todo el frontend (`frontend/app/**` + `frontend/components/**`, 304 `.tsx`) |
| **No incluye** | Dark mode (R08, ya resuelto) ni lógica de reportes |
| **Método** | Auditoría por 4 superficies (SPA `?m=`, admin, wizard/portales/auth, inventario de íconos) → catálogo de defectos → corrección por lotes con capturas en 375 / 768 / 1440 px |
| **Rama sugerida** | reutilizar `feature/AB-10813-...` o abrir `feature/AB-10813-responsive-ui` |

---

## 0. Diagnóstico general

La base ya está **mayormente bien**: tokens de tema unificados, portales públicos y pantallas de auth (`AuthCard`) construidos con `w-full max-w-md`, y muchas tablas/grids nuevos ya envuelven en `overflow-x-auto` y usan breakpoints. Los defectos se concentran en **superficies antiguas (admin CRUD, RBAC, Usuarios) y en primitivos compartidos** que, arreglados una vez, cascadean a decenas de pantallas.

**Cuatro palancas transversales** resuelven ~60 % del impacto con muy poco código:

1. `Modal.tsx` sin control de alto → **todos** los diálogos se cortan en móvil.
2. `Shell.tsx` (dock inferior + header) no se adapta a pantallas angostas.
3. Un patrón estándar de **tabla con scroll** que ~14 tablas antiguas no aplican.
4. Un componente **`CreateButton`** reutilizable que además centraliza el cambio de ícono "+".

Severidad: **Alta** = rompe/oculta contenido en móvil; **Media** = usable pero apretado/degradado; **Baja** = pulido.

---

## 1. FASE 0 — Fundaciones transversales (hacer primero)

### 1.1 `Modal.tsx` — control de alto y scroll interno  ⚑ Alta · cascadea a TODOS los modales
`components/atom/Modal.tsx:93` (overlay) y `:102` (panel).
- Overlay: añadir `overflow-y-auto py-6` (hoy solo `flex items-center justify-center px-4`).
- Panel: añadir `max-h-[90dvh] overflow-y-auto` y padding responsive `p-4 sm:p-6` (hoy `p-6` fijo).
- **Impacto:** arregla de un solo cambio `CreateCompanyDialog`, `EditCompanyDialog`, `CreateDocumentTypeDialog`, `DocumentInUseDialog`, y todos los diálogos de `users/` y RBAC que usan este Modal.

### 1.2 `Shell.tsx` — dock inferior + header  ⚑ Alta
`components/atom/Shell.tsx`.
- **Dock** (`:385`, `flex items-center gap-1 px-3 py-2 rounded-full`, sin `flex-wrap`): con rol **SuperAdmin** hay ~14 entradas + FAB (~700 px) sobre un contenedor centrado con `-translate-x-1/2` → se sale por ambos bordes en móvil/tablet. Opciones (elegir en diseño):
  - **A (recomendada):** en `<lg` colapsar el dock a un menú tipo "grid de apps" (botón que abre overlay con las entradas en `grid grid-cols-4`), y mantener el dock horizontal solo en `lg:`.
  - **B (mínima):** permitir `max-w-[95vw] overflow-x-auto` en el dock y `flex-nowrap` con scroll — funcional pero menos elegante.
- **Header** (`:285`, `px-6 py-3`): pasar a `px-4 md:px-6`. El bloque de identidad ya es `hidden sm:flex` (correcto).
- **Padding de contenido** `pb-28` (`:380`): OK, pero revisar tras rediseño del dock.

### 1.3 Componente `CreateButton` + estándar de tabla-scroll  ⚑ Media
- Crear `components/atom/CreateButton.tsx` (o en `components/ui/`) que encapsule el patrón hoy duplicado a mano (`inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white` + gradiente `linear-gradient(135deg,#557EFF,#00DBD5)`). Props: `label`, `onClick`, `icon?` (opcional, semántico), `size?`. Ver **§4** para el ícono.
- Documentar el **patrón de tabla responsive** (envolver en `<div className="overflow-x-auto"><table className="min-w-[…]">`) tomando como referencia los que ya lo hacen bien: `Validaciones.tsx:734`, `Auditoria.tsx:297`, `DashboardGrid` (`min-w-[920px]`), `OtDocumentosTab`, `PlateRangesConsole`, `SignatureVaultTab`.

---

## 2. FASE 1 — SPA de módulos (`?m=`)  ·  `components/atom/**`

| # | archivo:línea | problema (className) | sev | corrección |
|---|---|---|---|---|
| 2.1 | `modules/Ayuda.tsx:15` | `grid grid-cols-4 gap-3` sin breakpoints → tarjetas de ~80px a 375px | **Alta** | `grid grid-cols-2 md:grid-cols-4 gap-3` |
| 2.2 | `modules/RbacAdmin.tsx:150-159` | tabla de módulos en `overflow-hidden` + grid de 7 columnas px fijas (`40px 120px 1fr 1fr 80px 80px 120px`) | **Alta** | wrapper `overflow-x-auto` + contenido `min-w-[760px]` |
| 2.3 | `modules/RbacAdmin.tsx:431-461` | tablas de roles `grid "1fr 1fr 90px 90px 110px"` dentro de `overflow-hidden` | Media | `overflow-x-auto` + `min-w-[640px]` |
| 2.4 | `modules/RbacAdmin.tsx:584, 684` | `grid-cols-2` sin breakpoint (checklist permisos / código-tipo) | Baja | `grid-cols-1 sm:grid-cols-2` |
| 2.5 | `modules/Usuarios.tsx:220-261` | tabla usuarios `grid "3fr 2fr 2fr 1.5fr 1.5fr 40px"` **sin** scroll ni `min-w` → email/fecha ilegibles | **Alta** | envolver cabecera+filas en `overflow-x-auto` + `min-w-[720px]` |
| 2.6 | `modules/Usuarios.tsx:395-423 / 492-505` | tablas "Eliminados" (`3fr 2fr 2fr 40px`) y roles (`grid-cols-12`) sin scroll | Media | `overflow-x-auto` + `min-w-[…]` |
| 2.7 | `modules/Usuarios.tsx:192, 210` | barra de tabs + buscador en una fila **sin** `flex-wrap` → overflow a 375px | Media | `flex-wrap`; buscador a fila propia `w-full sm:w-auto sm:ml-auto` |
| 2.8 | `modules/Usuarios.tsx:744-745` | InviteModal (overlay propio) `max-w-md` sin `max-h`/scroll | Media | overlay `overflow-y-auto`; panel `max-h-[90vh] overflow-y-auto` |
| 2.9 | `modules/Usuarios.tsx:851` | `grid grid-cols-2 gap-x-4` (checklist roles) | Baja | `grid-cols-1 sm:grid-cols-2` |
| 2.10 | `modules/_reportes/.../ProcedureDetailPanel.tsx:126` | `<table className="w-full text-xs">` sin `overflow-x-auto` (panel `max-w-xl` a ancho completo en móvil) | Media | envolver tabla en `overflow-x-auto` |
| 2.11 | `modules/_reportes/.../ProductivityCards.tsx:79` | `grid grid-cols-3 gap-3` sin breakpoint | Baja | `grid-cols-1 sm:grid-cols-3` |
| 2.12 | `ProfileZone.tsx:88-89` | modal contraseña `max-w-md` sin `max-h`/scroll | Baja | `max-h-[90vh] overflow-y-auto` en panel |
| 2.13 | `DashboardGrid.tsx:96-97` | menú hover `absolute … w-56` (224px fijo) dentro de scroll horizontal | Baja | `max-w-[80vw]` |

> **Sin cambios (ya correctos, usar de referencia):** `Validaciones.tsx`, `Auditoria.tsx`, `DetailedReportGrid`, `PeakHoursHeatmap`, filtros (`*FilterToolbar`, `GlobalFilters`, `DetailedReportFiltersPanel`), KPIs de todos los módulos, `Pagination`, `ModuleTitle`, `Login`.

---

## 3. FASE 2 — Administración  ·  `app/admin/**` + `components/admin/**`

### 3.1 Tablas sin `overflow-x-auto`  ⚑ Alta (mayor impacto móvil del área)
Todas son tablas de 5+ columnas con `border-separate ... text-xs` que desbordan a 375px. Patrón de fix: envolver en `<div className="overflow-x-auto">` y dar `min-w-[…]` a la tabla.

| archivo:línea | `min-w` sugerido |
|---|---|
| `companies/CompanyListTable.tsx:38-39` | `min-w-[640px]` |
| `companies/AuditLogTable.tsx:21-22` | `min-w-[720px]` (valores ant./nuevo largos) |
| `documents/panels/ResolvedMatrixTable.tsx:14-15` | `min-w-[720px]` |
| `documents/DocumentTypeListTable.tsx:34-35` | `min-w-[640px]` |
| `documents/CompanyDocumentParamsPanel.tsx:99` | `overflow-x-auto` |
| `improntas/ImprontaHistorialTable.tsx:30-31` | `min-w-[640px]` |
| `transit-offices/ClientProceduresTable.tsx:61-62` | `min-w-[720px]` |
| `transit-offices/MandatariosSection.tsx:141` | `overflow-x-auto` |
| `transit-offices/RulesSection.tsx:89` | `min-w-[640px]` |
| `transit-offices/WebhooksSection.tsx:149` y `:282` | `overflow-x-auto` / `min-w-[720px]` |

### 3.2 Tablas con `overflow-hidden` que recorta  ⚑ Media
| archivo:línea | corrección |
|---|---|
| `transit-offices/OtUsersSection.tsx:233-234` y `:298-301` | cambiar el borde `overflow-hidden` por wrapper interno `overflow-x-auto` (no recortar columna Acciones) |

### 3.3 Modales admin sin `max-h`/scroll  ⚑ Media
Overlay `flex items-center justify-center`, panel `max-w-md ... p-6` sin scroll → formularios largos se cortan. Añadir a cada panel `max-h-[90dvh] overflow-y-auto` (o migrarlos al `Modal.tsx` ya corregido en §1.1):
- `transit-offices/OtUsersSection.tsx:591`
- `transit-offices/ClientProceduresSection.tsx:510, 558, 643, 669, 709`
- `transit-offices/QuipuxQueueList.tsx:250`
- `transit-offices/TramitesSuperSection.tsx:393, 430`
- `transit-offices/DocumentsSection.tsx:248` (Baja — contenido corto)
- **Referencia de drawer correcto:** `transit-offices/OtSidePanel.tsx` (`flex-1 overflow-y-auto`).

### 3.4 Grids / anchos / padding  ⚑ Baja-Media
| archivo:línea | problema | corrección |
|---|---|---|
| `companies/signature-vault/SignatureVaultFormPanel.tsx:240` | `grid grid-cols-2` sin breakpoint | `grid-cols-1 sm:grid-cols-2` |
| `quipux/QuipuxSettingsForm.tsx:435` | `grid-cols-2 md:grid-cols-3` (2 cols ya en móvil) | `grid-cols-1 sm:grid-cols-2 md:grid-cols-3` |
| `companies/CompanyFiltersPanel.tsx:49` | salto directo `1 → md:grid-cols-6` (6 campos estrechos en tablet) | `sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6` |
| `transit-offices/PlateRangesConsole.tsx:111` | `select w-72` fijo | `w-full sm:w-72` |
| `app/admin/companies/page.tsx:114`, `app/admin/improntas/page.tsx:16` | `px-6` fijo | `px-4 md:px-6` (ref: `OtHubLayout.tsx:75` `px-4 md:px-8`) |

> **Sin cambios (correctos):** tabs `OtTabBar`, `CompanyConfigTabs`, `DocumentProcedureTabs`, `ImprontasTabs` (todos `overflow-x-auto` + `shrink-0`); toolbars `flex flex-wrap`; `TransitOfficesList`, `PlatePreassignViewer`, `SignatureVaultTab` (ya con scroll).

---

## 4. FASE 3 — Wizard de trámites, operación y superadmin

| # | archivo:línea | problema | sev | corrección |
|---|---|---|---|---|
| 4.1 | `operacion/ActorsForm.tsx:728` | `grid grid-cols-3` en datos de persona RUNT → ilegible a 375px | **Alta** | `grid-cols-1 sm:grid-cols-2 lg:grid-cols-3` |
| 4.2 | `operacion/ActorsForm.tsx:679` | `grid grid-cols-2` (datos empresa RUES) sin breakpoint | Media | `grid-cols-1 sm:grid-cols-2` |
| 4.3 | `operacion/TramiteWizard.tsx:712-714` | nav "Anterior / Guardar y continuar" `flex justify-between` sin `flex-wrap` → se aprietan en ~375px | Media | `flex-wrap gap-2` |
| 4.4 | `operacion/ExpedienteVisor.tsx:290` | `grid grid-cols-3 gap-2` sin breakpoint | Media | `grid-cols-1 sm:grid-cols-3` |
| 4.5 | `operacion/TramitesTable.tsx:368` | buscador `min-w-[220px]` puede desbordar junto a otros controles | Baja | `min-w-0` o `sm:min-w-[220px]` |
| 4.6 | `operacion/TramitesTable.tsx:578-579` | tabla `min-w-[1180/1340px]` (scroll ya intencional en `overflow-x-auto`) | Baja | opcional: vista de tarjetas apiladas en `<md` |
| 4.7 | `shared/DocumentPreviewModal.tsx:149` | `<iframe style={{minHeight:480}}>` desborda viewport bajo | **Alta** | `min-h-[60vh]` o `min(480px,60vh)` |
| 4.8 | `shared/DocumentPreviewModal.tsx:66,72,161` | `min-h-[400px]` (loading/cuerpo) y `max-h-[520px]` (img) fijos | Media/Baja | `min-h-[50vh]` y `max-h-[70vh]` |
| 4.9 | `superadmin/ParametrizationWizard.tsx:99` | contenedor raíz `h-full … overflow-hidden` puede recortar en pantallas bajas | Media | revisar `<md`; `overflow-y-auto` o stepper colapsable |
| 4.10 | `superadmin/wizard/Step8Guardar.tsx:34` | `grid grid-cols-3` (resumen) sin breakpoint | Media | `grid-cols-1 sm:grid-cols-3` |

> **Sin cambios (correctos):** stepper del wizard (colapsa `col-span-12 md:col-span-3`), `FirmaFurStep`, `BiometricStep` (tabla ya en `overflow-x-auto`), `BiometricCapture` (captura móvil con `capture="environment"`), `CommercialForm`, `PrendaForm`, `MatriculaResumen`, `DocumentChecklist`, `EstadoFunnel`. **Portales públicos (`portal/[token]`, `biometric/[token]`) y auth (`AuthCard`, login, forgot/reset/activate) están correctos** — no requieren cambios.

---

## 5. Rediseño de íconos "crear / agregar" (el signo +)

**Hallazgo:** solo hay **7 usos** del ícono lucide `Plus` como acción de crear (cero "+" literales como texto). Todos acompañan un botón que **ya tiene label descriptivo**, así que el "+" es decoración redundante. Además **no existe un componente reutilizable**: cada pantalla repite el `<button>` con el mismo gradiente, con tamaños (`h-4` vs `h-3.5`) y `aria` inconsistentes.

| archivo:línea | botón | acción |
|---|---|---|
| `app/admin/documents/page.tsx:170` y `:187` | "Crear documento" (header + estado vacío) | crear tipo de documento |
| `app/admin/companies/page.tsx:135` | "Crear compañía" | crear compañía |
| `app/admin/transit-offices/page.tsx:99` | "Dar de alta Organismo de Tránsito" | alta de OT |
| `components/admin/transit-offices/TransitOfficesList.tsx:164` | RowAction (`aria-label` "Dar de alta …") | alta de OT |
| `components/atom/modules/_reportes/scheduling/SchedulesSection.tsx:104` | "Nuevo informe" | crear informe programado |
| `components/atom/modules/_reportes/scheduling/AlertsSection.tsx:116` | "Nueva alerta" | crear regla de alerta |

**Recomendación (decisión de diseño — elegir 1):**
- **Opción A (recomendada): centralizar + ícono semántico.** Crear `CreateButton` (§1.3) y en cada sitio pasar el ícono que describe *qué* se crea, no un genérico `+`: `FilePlus`/`FileText` (documento), `Building2` (compañía), `Landmark` (OT), `CalendarPlus`/`BellPlus` (informe/alerta). Mantiene una acción reconocible pero descriptiva y homogénea (tamaño y `aria` únicos).
- **Opción B: solo texto.** Quitar el ícono; el label ("Crear compañía") ya es explícito. Más limpio y minimalista.

Ambas pasan por el mismo refactor (crear `CreateButton`, reemplazar 7 sitios). El test de regresión `__tests__/hu10497-controles.test.tsx` ya verifica ausencia de `Plus` en otros controles: **extenderlo** para cubrir los 7 sitios tras el cambio.

> **No tocar** (usos de `+` que NO son crear): deltas de métricas (`DashboardGrid` "+12 hoy"), teléfonos (`ProfileZone` "+57…"), "+120 artículos" (`Ayuda`), concatenaciones de className.

---

## 6. Estrategia de ejecución y verificación

1. **Orden:** Fase 0 (fundaciones) → Fase 1 (SPA) → Fase 2 (admin) → Fase 3 (wizard/operación) → Fase 4 (íconos). Fase 0 primero porque `Modal.tsx` y `CreateButton` reducen trabajo aguas abajo.
2. **Breakpoints de verificación:** 375 px (móvil), 768 px (tablet), 1440 px (desktop) — el proyecto ya tiene `e2e-audit/` + `playwright.audit.config.ts`; usarlo para capturas antes/después por pantalla (evidencia del PR, criterio de aceptación 4 de F09).
3. **PRs por fase**, ≤ 800 líneas (regla FLIT 9), contra `develop`.
4. **Checklist de aceptación (por pantalla):** sin scroll horizontal del `body` a 375px; tablas anchas con scroll propio contenido; modales con scroll interno y tope `90dvh`; ningún grid con más de 1 columna útil a 375px salvo pares cortos; dock/menú navegable en móvil.

## 7. Descomposición sugerida en HUs (FRONTEND)

| HU | Alcance | Pts (Fib) |
|---|---|---|
| HU-A | Fundaciones: `Modal.tsx` (max-h/scroll) + `Shell.tsx` (dock + header) + `CreateButton` | 3 |
| HU-B | Barrido SPA `atom` (Fase 1: Ayuda, RBAC, Usuarios, paneles reportes) | 3 |
| HU-C | Barrido Admin (Fase 2: tablas overflow, modales, grids/padding) | 5 |
| HU-D | Barrido Wizard/Operación/Superadmin (Fase 3) + `DocumentPreviewModal` | 3 |
| HU-E | Rediseño íconos "crear" (Fase 4) + extender test regresión hu10497 | 1 |

**Total estimado:** ~15 pts. Todas requieren capturas 375/768/1440 en el PR como evidencia.
