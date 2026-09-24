# Features bajo la épica #12751 — borradores (no registrados en ADO)

> **Estado:** borrador local. Espera aprobación explícita antes de registrar en Azure DevOps
> (`feature-creator` / tech-lead Modo A). La creación real la ejecuta la sesión principal.
> **Padre:** Epic #12751 — proyecto **FLIT - EVOLUTION**, organización `FlitDevOps`.
> **Título épica:** "Restricción de módulos y submódulos administrativos en la perspectiva del Admin OT".
> **AssignedTo propuesto:** Willyn Londoño Calle (`willyn.londono@flitsas.com`).
> **Sprint:** no se fija aquí — lo decide la sesión principal (regla: sprint **siguiente** al activo, nunca el activo). Tag `DOR` obligatorio antes de Active.
> **Área:** FLIT - EVOLUTION.

No hay entidades ni tablas nuevas: no hace falta HU de schema/migración ni ADR de persistencia.
El diseño técnico es el mapa de código verificado por el supervisor (endpoints, componentes y
policies existentes) más los ajustes descritos en cada Feature.

## Decisiones humanas ya cerradas (no se re-preguntan)

- **Preasignación de rango:** solo se oculta en UI y se deprecan endpoints; no se borran tablas ni
  entidades. Los rangos existentes quedan **ignorados**: toda asignación de placa a un trámite pasa
  por la ruta fuera de rango (`ReserveOutOfRangePlateAsync`). Se conservan `assign-plate`,
  `release-plate`, `revoke` (alias) y `update-plate` del trámite, y el estado `preasignacion`.
- **Operador OT** (`gestor_tramites_ot`) y roles OT personalizados **también** pierden Reglas,
  Requisitos, Configuración y Preasignación. El ordenamiento documental (Prelación) queda solo para
  Admin OT (`ot_admin`) y Super Admin — ni el operador ni otros roles OT lo conservan.
- El arreglo de *scoping* para Super Admin (varios endpoints hoy escriben en el tenant del JWT del
  Super Admin en vez del organismo que está viendo) entra en el alcance de esta épica.
- La ruta de placa preasignada de la compañía también se apaga en backend (decisión humana
  2026-09-24): `/plate-preassign/available` y `/status` responden 410 Gone y los flags
  `PlatePreassignEnabled` / `AllowPlatePreassign` se dejan de persistir y evaluar (columnas se
  conservan, sin migración).
- Código de respuesta para los endpoints deprecados de la consola de rangos: **410 Gone** (no 404 —
  el recurso existió y se retiró a propósito; 404 induce a pensar que la ruta nunca existió).

## Supuestos tomados (a validar por el humano)

- "Documentos y Prelación" en el dock deja de ofrecerse a cualquier rol OT que no sea `ot_admin` o
  Super Admin (hoy lo ve cualquier usuario de un tenant OT). Esto se desprende de la decisión "el
  ordenamiento documental queda solo para Admin OT y Super Admin", no de una frase literal de la
  épica sobre el dock.

---

## Feature A

Título: `[ADMIN-OT] - Eliminar módulo de Preasignación de rango`

# OBJETIVO

Retirar el módulo de Preasignación de rango de toda la plataforma (UI y API), para todos los roles
incluido Super Admin, sin borrar los datos históricos ni romper la asignación de placa dentro de un
trámite, que en adelante siempre se resuelve fuera de rango.

# DESCRIPTION

La consola de gestión de rangos (`/admin/transit-offices/{id}/plate-ranges`, endpoints
`GET/POST/PUT /api/v1/admin/plate-ranges*`) se deprecia por completo. El backend deja de decidir la
asignación de placa por rango: `AssignPlateToProcedureAsync` siempre reserva fuera de rango
(`ReserveOutOfRangePlateAsync`), sin importar lo que envíe el cliente. Se conservan intactos
`assign-plate`, `release-plate`, `revoke` (alias) y `update-plate` del trámite (HU #10654 / ADR-0059
/ HU #12167 / HU #12598): esas rutas no pertenecen a la consola, son parte del ciclo de vida del
trámite en estado `preasignacion`.

En frontend se retiran: la página y el componente de la consola, la pestaña `plate-ranges` de
`OT_HUB_TABS`, la entrada y el grupo `preasignacion` del dock, el visor de la compañía
(`PlatePreassignViewer`), los dos switches que configuran la ruta de placa preasignada
(Requisitos OT y Configuración de empresa) y el artículo del manual. El modal de asignar/corregir
placa dentro de la bandeja OT deja de ofrecer un modo "en rango" (ya no tiene sentido si el backend
siempre reserva fuera de rango).

Fuentes de código: `Endpoints/AdminPlateRangesEndpoints.cs`,
`Persistence/Repositories/OtClientProcedureRepository.cs`, `app/admin/transit-offices/[id]/plate-ranges/`,
`components/admin/transit-offices/PlateRangesConsole.tsx`, `components/atom/dock/dockGroups.ts`,
`components/admin/transit-offices/ot-nav.ts`, `components/atom/Shell.tsx`,
`components/admin/companies/panels/PlatePreassignViewer.tsx`, `lib/api/admin-plate-ranges.ts`,
`components/admin/transit-offices/RequirementsSection.tsx`,
`components/admin/companies/panels/ConfiguracionEmpresaTab.tsx`, `lib/manual/articles/ot.ts`,
`components/admin/transit-offices/ClientProceduresSection.tsx`.

# CRITERIOS FUNCIONALES

- [ ] Ningún endpoint de la consola de rangos (listar/crear/editar rangos, listar placas, elegibles,
      bloquear/desbloquear/revocar placa de rango) responde 200; todos responden **410 Gone**.
- [ ] `assign-plate` del trámite ignora cualquier valor de `outOfRange` recibido y siempre reserva
      fuera de rango; `release-plate`, `revoke` (alias) y `update-plate` no cambian de comportamiento.
- [ ] Ningún rol (incluido Super Admin) ve la consola, la pestaña ni la entrada de dock
      "Preasignación"; la ruta directa a la consola ya no renderiza el componente retirado.
- [ ] El visor de empresa y los dos switches de configuración de la ruta preasignada desaparecen de
      la UI; el artículo del manual correspondiente se retira del índice.
- [ ] El modal de asignación de placa en la bandeja OT ofrece un único campo de placa (sin selector
      de modo en-rango/fuera-de-rango).
- [ ] La ruta de placa preasignada de la compañía queda apagada en backend: `/plate-preassign/*`
      responden 410 Gone y los flags de compañía y de Requisitos OT se ignoran al guardar.

---

## Feature B

Título: `[ADMIN-OT] - Reglas, Requisitos y Configuración del Organismo exclusivos de Super Admin`

# OBJETIVO

Que los submódulos Reglas, Requisitos y Configuración del Organismo del hub OT solo sean accesibles
por Super Admin FLIT, en UI y API, y que de paso quede corregido el bug de *scoping* por el que
varios de estos endpoints escriben en el tenant del JWT de Super Admin en vez del organismo que
está administrando.

# DESCRIPTION

Hoy el grupo `/api/v1/admin/ot` exige `OtModulePolicy` (Super Admin, `ot_admin` o cualquier usuario
de un tenant OT) como política por defecto, y Requisitos ya resuelve el organismo objetivo con
`ResolveOtUserScopeAsync` (`?transitOfficeId` para Super Admin, tenant propio para el resto) — pero
Reglas, el perfil OT (`PATCH /profile`) y los feature flags (`PATCH /feature-flags/{id}`) todavía
leen el tenant directo del JWT, así que un Super Admin que los llama termina escribiendo en su
propio tenant "fantasma", no en el organismo que ve en pantalla. Primero se lleva ese mismo patrón
de *scoping* a Reglas/Perfil/Feature-flags (HU-B1); solo después se puede cerrar el acceso a
Super Admin exclusivamente (HU-B2), porque si se restringe antes de arreglar el scoping, Super Admin
pierde la forma de operar sobre el organismo ajeno.

`GET /profile` se mantiene abierto a cualquier usuario de un tenant OT: lo usan `Shell.goOtHub` y
`OtHubLayout` para resolver el id del organismo antes de saber si el usuario es admin u operador; no
es una vista de configuración.

En frontend se retiran del dock (bloque `isOtUser` en `Shell.tsx`) las tres entradas — hoy visibles
para cualquier usuario de un tenant OT, no solo el admin — y se agrega un guard de página que
redirige a `/403` a quien no sea Super Admin y entre por URL directa (bloqueo en dos capas). También
se retira la ruta legacy `[id]/tramites` (`TramitesSuperSection.tsx`), que duplicaba sin enlace de
menú lo que hoy ya cubre `/configuracion`.

Fuentes de código: `Endpoints/AdminOtEndpoints.cs` (rules, profile, feature-flags, requirements),
`Authorization/ApiSecurityExtensions.cs`, `Authorization/AdminAuthorization.cs`,
`components/atom/Shell.tsx`, `components/admin/transit-offices/ot-nav.ts`,
`app/admin/transit-offices/[id]/rules/`, `/requirements/`, `/configuracion/`, `/tramites/`
(retirada), `lib/auth/guard.ts`.

# CRITERIOS FUNCIONALES

- [ ] Reglas, `PATCH /profile` y `PATCH /feature-flags/{id}` resuelven el organismo por
      `?transitOfficeId` cuando el caller es Super Admin (mismo contrato 400 que Requisitos).
- [ ] Reglas, Requisitos, `PATCH /profile` y `PATCH /feature-flags/{id}` exigen la policy
      `SuperAdmin`; `ot_admin` y cualquier otro rol OT reciben 403. `GET /profile` sigue abierto.
- [ ] El dock no ofrece Reglas, Requisitos ni Configuración a ningún usuario de un tenant OT (admin
      u operador); solo Super Admin las ve, vía su barra de pestañas del hub.
- [ ] Un `ot_admin` que navega por URL directa a `/rules`, `/requirements` o `/configuracion` es
      redirigido a `/403` (bloqueo también en la capa de UI, no solo en la API).
- [ ] La ruta legacy `[id]/tramites` deja de existir; `/configuracion` cubre sin pérdida de función
      el modo Dashboard/QX, la ventana de revocatoria y los feature flags operativos.

---

## Feature C

Título: `[ADMIN-OT] - Documentos y Prelación: el Admin OT solo ordena`

# OBJETIVO

Que en el submódulo Documentos el Admin OT conserve exclusivamente el ordenamiento documental
(Prelación) y pierda Etiquetas, el switch de documento de prenda y cualquier control de exigencias
documentales, que quedan exclusivos de Super Admin; que el operador y otros roles OT personalizados
pierdan también el acceso a este submódulo por completo.

# DESCRIPTION

Igual que en la Feature B, primero se corrige el *scoping* de Prelación (`document-precedence`) y
Etiquetas (`document-tags`) para que Super Admin opere sobre el organismo que ve (`?transitOfficeId`)
en vez de su propio tenant (HU-C1). Después se introduce una policy nueva —no existe hoy una que
combine exactamente "Super Admin o `ot_admin`, nada más"— para Prelación, y se cierra a
`SuperAdminPolicy` Etiquetas, el switch de prenda (`AdminOtPrendaDocumentPolicyEndpoints`, ya
*scoped* por oficina) y los overrides de exigencias documentales
(`AdminDocumentRequirementOverridesEndpoints`, sin consumidor en UI hoy) (HU-C2).

En frontend, la entrada "Documentos" del dock se restringe a `ot_admin` (se retira para el operador
y roles personalizados, igual que ya ocurre con "Usuarios"), y dentro de `DocumentsSection.tsx` el
Admin OT ve solo la pestaña Prelación; Etiquetas y el switch de prenda quedan condicionados a
Super Admin.

Fuentes de código: `Endpoints/AdminOtEndpoints.cs` (document-precedence, document-tags),
`Endpoints/AdminOtPrendaDocumentPolicyEndpoints.cs`,
`Endpoints/AdminDocumentRequirementOverridesEndpoints.cs`, `Authorization/AdminAuthorization.cs`,
`Authorization/ApiSecurityExtensions.cs`, `components/atom/Shell.tsx`,
`components/admin/transit-offices/DocumentsSection.tsx`.

# CRITERIOS FUNCIONALES

- [ ] Prelación y Etiquetas resuelven el organismo por `?transitOfficeId` cuando el caller es
      Super Admin, con el mismo contrato 400 que Requisitos.
- [ ] Un `ot_admin` puede seguir leyendo y reordenando Prelación (200); no puede usar Etiquetas, el
      switch de prenda ni los overrides de exigencias (403 en los tres).
- [ ] Cualquier otro rol OT (operador incluido) recibe 403 incluso en Prelación: la policy nueva no
      es la genérica del módulo OT.
- [ ] El dock ya no ofrece "Documentos" a roles OT distintos de `ot_admin`.
- [ ] Un `ot_admin` que abre Documentos ve solo la pestaña Prelación; Super Admin sigue viendo
      Prelación, Etiquetas y el switch de prenda sin cambios.

---

## DoR-Feature (previo a Active)

| Criterio | A | B | C |
|---|---|---|---|
| Módulo en título (`[ADMIN-OT]`) | PASS | PASS | PASS |
| Objetivo | PASS | PASS | PASS |
| Descripción extendida con fuentes de código | PASS | PASS | PASS |
| ≥3 criterios funcionales | PASS | PASS | PASS |
| Sprint (siguiente al activo) | PENDING — lo asigna la sesión principal | PENDING | PENDING |
| Area FLIT - EVOLUTION | PASS | PASS | PASS |
| Tag `DOR` | PENDING — se agrega al registrar | PENDING | PENDING |
| AssignedTo humano | propuesto Willyn Londoño Calle | propuesto Willyn Londoño Calle | propuesto Willyn Londoño Calle |
| Sin placeholders TODO/TBD | PASS | PASS | PASS |
| Sin datos sensibles | PASS | PASS | PASS |

No se registra nada en ADO hasta aprobación humana explícita. HUs hijas: ver `HUS.md`
(13 HUs: 5 en A, 4 en B, 4 en C).

## Orden de implementación entre Features

1. **Feature A** (Preasignación) — sin dependencia de B o C; toca archivos propios
   (`AdminPlateRangesEndpoints.cs`, `OtClientProcedureRepository.cs`, componentes de rango).
2. **Feature B** (Reglas/Requisitos/Configuración) — toca `AdminOtEndpoints.cs` (rules, profile,
   feature-flags) y `Shell.tsx`; conviene mergear antes que C para reducir conflictos en los mismos
   archivos.
3. **Feature C** (Documentos/Prelación) — toca `AdminOtEndpoints.cs` (document-precedence,
   document-tags) y `Shell.tsx`; depende en la práctica de que B ya haya tocado esos archivos para
   minimizar conflictos de merge (no hay dependencia funcional entre B y C, solo de archivo).

Recomendación: una rama por Feature (memoria del proyecto), PRs en el orden A → B → C.
