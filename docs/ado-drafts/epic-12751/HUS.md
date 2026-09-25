# Descomposición HU — Épica #12751 (borrador local, no registrado en ADO)

> **Estado:** borrador. Espera aprobación humana antes de crear en Azure DevOps (`flit-crear-hu`).
> **Sprint:** no se fija — lo asigna la sesión principal (sprint siguiente al activo).
> **AssignedTo propuesto:** Willyn Londoño Calle (`willyn.londono@flitsas.com`). Tags: `DOR`.
> `Custom.Refinement = "True"` (string, no booleano) al registrar.
> **Diseño técnico:** mapa de código verificado (ver `FEATURES.md`); sin schema nuevo, sin ADR de
> persistencia.
> **Convención de PR:** cada HU cabe holgadamente en ≤800 líneas; las HUs backend/frontend de una
> misma Feature comparten rama (memoria: una rama por Feature, no por HU).

Totales: **13 HUs** · Feature A 5 HUs / 19 SP · Feature B 4 HUs / 15 SP · Feature C 4 HUs / 18 SP.
Total épica: **52 SP**.

| Feature | HUs locales |
|---|---|
| A — Preasignación de rango | HU-A1 · HU-A2 · HU-A3 · HU-A4 · HU-A5 |
| B — Reglas/Requisitos/Configuración | HU-B1 · HU-B2 · HU-B3 · HU-B4 |
| C — Documentos y Prelación | HU-C1 · HU-C2 · HU-C3 · HU-C4 |

---

## Feature A — Eliminar módulo de Preasignación de rango (5 HUs · 19 SP)

### HU-A1 `[BACKEND] – Preasignación – Deprecar consola de rangos y forzar reserva fuera de rango`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna — bloquea a HU-A2 y HU-A4 |

**Como** Super Admin o Admin OT que ya no gestiona rangos
**quiero** que la consola de preasignación de rango deje de operar en la API y que asignar placa a
un trámite en `preasignacion` siempre reserve la placa fuera de rango
**para** que ningún rango configurado antes decida la asignación y la plataforma deje de depender
del módulo eliminado

```gherkin
AC1 — positivo
Dado un cliente que llama POST /api/v1/admin/plate-ranges/procedures/{instanceId}/assign-plate
  con outOfRange=false
Cuando el backend procesa la solicitud
Entonces ignora el flag recibido y ejecuta ReserveOutOfRangePlateAsync
Y no invoca TryReservePlateAsync bajo ninguna condición

AC2 — negativo
Dado GET/POST/PUT sobre /api/v1/admin/plate-ranges/ , /plates, /eligible-companies
  o /plates/{id}/block|unblock|revoke
Cuando cualquier rol autenticado los invoca
Entonces el backend responde 410 Gone
Y el cuerpo referencia que el módulo fue retirado (no 200, no 404)

AC3 — borde
Dado un trámite con una placa asignada por un rango histórico (fila preexistente en
  plate_range_details)
Cuando se consulta o se libera esa placa vía release-plate, update-plate o revoke (alias) del trámite
Entonces los tres endpoints siguen funcionando sin cambios de contrato
Y no dependen de que el rango histórico siga existiendo
```

**Notas técnicas:** `Endpoints/AdminPlateRangesEndpoints.cs` — 410 Gone en
`ListRangesAsync`/`ListPlatesAsync`/`EligibleCompaniesAsync`/`AssignRangeAsync`/`EditRangeAsync`/
`SetStateAsync` (block/unblock/revoke de placa de rango); conservar sin cambios
`AssignPlateToProcedureAsync`/`ReleaseProcedurePlateAsync`/`UpdateProcedurePlateAsync`.
`Persistence/Repositories/OtClientProcedureRepository.cs` — eliminar la rama `TryReservePlateAsync`
del método de asignación (la que hoy se ejecuta cuando `outOfRange=false`); dejar una sola vía que
llama siempre a `ReserveOutOfRangePlateAsync`. Tests a actualizar:
`tests/Flit.Admin.Tests/PlatePreassign/PlatePreassignTests.cs`,
`tests/Flit.Integration.Tests/TransitOffices/PlateRangeHierarchyEligibilityIntegrationTests.cs`.

---

### HU-A2 `[FRONTEND] – Preasignación – Retirar consola y navegación`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-A1 |

**Como** Admin OT o Super Admin
**quiero** dejar de ver la consola de Preasignación de rango en el hub OT (ruta, pestaña, dock)
**para** que la plataforma no ofrezca un módulo que ya no existe en el backend

```gherkin
AC1 — positivo
Dado cualquier usuario OT o Super Admin en el dock o en la barra de pestañas del hub
Cuando abre la lista de módulos disponibles
Entonces no aparece "Preasignación" en el dock (el grupo `preasignacion` desaparece de
  DOCK_GROUP_ORDER)
Y OT_HUB_TABS ya no incluye la pestaña `plate-ranges`

AC2 — negativo
Dado un usuario que navega directo a /admin/transit-offices/{id}/plate-ranges
Cuando la ruta intenta resolver
Entonces no renderiza la consola (página y PlateRangesConsole retirados)
Y no queda un enlace roto en ningún menú

AC3 — borde
Dado el resto de grupos del dock (Trámites, Documentos, Reportes, Usuarios…)
Cuando se retira el grupo `preasignacion` de dockGroups.ts
Entonces DOCK_GROUP_ORDER, DOCK_GROUP_SIDE, DOCK_GROUP_LABEL, DOCK_GROUP_ICON y DOCK_ITEM_GROUP
  quedan consistentes entre sí (mismas claves en los cinco mapas)
Y los tests de Shell/dockGroups siguen en verde
```

**Notas técnicas:** eliminar `app/admin/transit-offices/[id]/plate-ranges/page.tsx` y
`PlateRangesConsole.tsx`; quitar `OT_ADM_DOCK.preasignacion` y la entrada `plate-ranges` de
`OT_HUB_TABS` en `ot-nav.ts`; quitar el `push` de la entrada Preasignación del bloque `isOtUser` en
`Shell.tsx`; quitar el grupo `preasignacion` de los cinco mapas de `dockGroups.ts` — **registrar el
borrado en `DOCK_GROUP_ORDER` Y en `DOCK_ITEM_GROUP`** (memoria del proyecto: un registro a medias
descarta el ítem en silencio, no lo elimina visiblemente). `lib/api/admin-plate-ranges.ts` — retirar
las funciones de consola (listar rangos/placas/elegibles, asignar/editar rango, cambiar estado de
placa de rango) y dejar solo `assignPlateToProcedure`/`releaseProcedurePlate`/`updateProcedurePlate`.

---

### HU-A3 `[FRONTEND] – Preasignación – Retirar visor de empresa y switches de configuración`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | Ninguna |

**Como** Admin OT, Super Admin o AdminCompany
**quiero** dejar de ver el visor de placas preasignadas y los switches que configuran esa ruta
**para** que la UI no ofrezca ajustes de un módulo que ya no tiene consola detrás

```gherkin
AC1 — positivo
Dado el panel de configuración de una compañía (CompanyConfigTabs)
Cuando un AdminCompany o Super Admin lo abre
Entonces ya no existe la pestaña ni el panel PlatePreassignViewer

AC2 — negativo
Dado el panel Requisitos del hub OT (accesible solo a Super Admin tras Feature B)
Cuando se renderiza
Entonces no aparece el ToggleRow "Ruta de placa preasignada" (id ot-req-plate)
Dado ConfiguracionEmpresaTab
Cuando se renderiza
Entonces tampoco aparece el switch equivalente de la compañía

AC3 — borde
Dado el Centro de Ayuda
Cuando se abre el índice de artículos del manual
Entonces el artículo de preasignación (lib/manual/articles/ot.ts) ya no aparece
Y ningún otro artículo enlaza a él (sin enlaces rotos)
```

**Notas técnicas:** retirar `PlatePreassignViewer.tsx` y su montaje en
`app/admin/companies/[tenantId]/page.tsx` y `CompanyConfigTabs.tsx`; retirar el `ToggleRow`
`ot-req-plate` de `RequirementsSection.tsx` y el switch equivalente + campo asociado de
`ConfiguracionEmpresaTab.tsx` / `settingsForm.ts`; retirar el artículo de `lib/manual/articles/ot.ts`.
Retirar también de `lib/api/admin-plate-ranges.ts` las funciones sin llamador
`listAvailablePlatesForCompany` y `getPlatePreassignStatus` (el wizard dejó de usarlas en la
Épica #12550) y sus tipos asociados. **No toca backend:** el apagado de los flags
`PlatePreassignEnabled` (compañía) y `allowPlatePreassign` (Requisitos OT) y de los endpoints
`/plate-preassign/*` lo hace HU-A5. No depende de HU-A1 porque no toca ningún endpoint de la consola.

---

### HU-A5 `[BACKEND] – Preasignación – Apagar la ruta de placa preasignada de la compañía`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | Ninguna |

**Como** Líder Técnico
**quiero** que el backend deje de exponer y de evaluar la ruta de placa preasignada de la compañía
(endpoints `/plate-preassign/*` y los flags que la habilitaban)
**para** que no quede un flujo vivo que ofrezca placas de rangos que la plataforma ya ignora

```gherkin
AC1 — positivo
Dado un usuario de compañía autenticado
Cuando llama GET /api/v1/tramites/plate-preassign/available o GET /plate-preassign/status
Entonces el backend responde 410 Gone
Y el cuerpo indica que la ruta de placa preasignada fue retirada

AC2 — negativo
Dado un Super Admin o AdminCompany que envía PUT de configuración de empresa con
  preasignacionPlacaActiva=true, o un PUT de Requisitos OT con allowPlatePreassign=true
Cuando el backend procesa la solicitud
Entonces ignora ese campo (no lo persiste ni lo registra en el diff de auditoría)
Y el resto de la configuración se guarda igual que hoy (200)

AC3 — borde
Dado una compañía u organismo con el flag en true persistido antes de esta HU
Cuando se consulta su configuración o se radica un trámite de matrícula inicial
Entonces el flag histórico no altera ningún comportamiento
Y las columnas plate_preassign_enabled y allow_plate_preassign permanecen en BD sin migración
```

**Notas técnicas:** `Endpoints/Tramites/ProcedureInstanceEndpoints.cs` — `ListAvailablePreassignPlates`
y `PlatePreassignStatus` responden 410 Gone (misma forma de cuerpo que HU-A1); revisar la entrada de
`Middleware/TenantEnforcementMiddleware.cs` para ese prefijo. `UpdateTenantSettingsHandler.cs:142` /
`SettingsDiff.cs:30` / `TenantSettingsRepository.cs` — dejar de escribir `PlatePreassignEnabled`
(conservar la propiedad en el contrato como ignorada para no romper clientes).
`UpdateOtRequirementsHandler.cs:37` / `OtRequirementsRepository.cs:98` — dejar de escribir
`AllowPlatePreassign`. `PlateRangeRepository.IsAssignmentAllowedAsync` queda sin llamadores tras
HU-A1 y esta HU: marcarlo `[Obsolete]` o retirarlo junto con su declaración en
`IPlateRangeRepository`. Sin migración: las columnas se quedan (decisión de la épica: no borrar
tablas). Tests: `TenantSettingsHandlerTests.cs`, `OtRequirementsTests.cs`,
`PlatePreassignTests.cs`, `TenantEnforcementMiddlewareTests.cs`.

---

### HU-A4 `[FRONTEND] – Preasignación – Simplificar el modal de placa del trámite a "fuera de rango"`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-A1 |

**Como** Admin OT u Operador OT
**quiero** que el formulario de asignar o corregir placa de un trámite en la bandeja ya no ofrezca
elegir entre "del rango" y "fuera de rango"
**para** que el flujo visible coincida con el backend, que ahora siempre reserva fuera de rango

```gherkin
AC1 — positivo
Dado el modal de asignar placa en ClientProceduresSection
Cuando el operador lo abre
Entonces solo ve un campo de placa libre, sin selector de modo en-rango/fuera-de-rango

AC2 — negativo
Dado el código que antes armaba el payload con outOfRange=false para el modo "en rango"
Cuando se revisa el cliente tras esta HU
Entonces esa rama ya no existe (el payload siempre equivale a outOfRange=true)

AC3 — borde
Dado release-plate y update-plate del mismo modal
Cuando se usan tras esta HU
Entonces su comportamiento no cambia respecto a hoy (nunca tuvieron selector de rango)
```

**Notas técnicas:** `ClientProceduresSection.tsx` — el modal de asignación de placa del trámite y la
rama que arma el payload en modo "en rango"/"fuera de rango"; `lib/api/admin-plate-ranges.ts` — tras
HU-A2, `assignPlateToProcedure` puede fijar `outOfRange: true` de forma implícita, sin que la UI lo
decida. Depende de HU-A1 porque el contrato del backend (que ya ignora `outOfRange`) debe estar en
firme antes de simplificar el cliente.

---

## Feature B — Reglas, Requisitos y Configuración exclusivos de Super Admin (4 HUs · 15 SP)

### HU-B1 `[BACKEND] – Admin OT – Resolver transitOfficeId de Super Admin en Reglas, Perfil y Feature Flags`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna — bloquea a HU-B2 |

**Como** Super Admin FLIT
**quiero** que crear/listar/editar reglas, actualizar el perfil OT y activar o desactivar feature
flags resuelvan el organismo por `?transitOfficeId` cuando quien llama es Super Admin
**para** dejar de escribir por accidente en mi propio tenant en lugar del organismo que administro

```gherkin
AC1 — positivo
Dado un Super Admin que envía POST/GET/PATCH /api/v1/admin/ot/rules con ?transitOfficeId=X
Cuando el handler resuelve el tenant objetivo
Entonces usa el tenant del organismo X (mismo patrón que GetOtRequirementsAsync/
  UpdateRequirementsAsync)
Y no el tenant del JWT del Super Admin

AC2 — negativo
Dado un Super Admin que llama PATCH /profile o PATCH /feature-flags/{id} sin ?transitOfficeId
Cuando el handler resuelve el scope
Entonces responde 400 (mismo contrato que ya usan GET/PUT de requirements)
Y no escribe en ningún tenant por defecto

AC3 — borde
Dado un ot_admin (no Super Admin) que llama estos mismos endpoints con su propio JWT
Cuando se resuelve el scope
Entonces sigue usando su propio tenant sin exigir ?transitOfficeId
Y el caso vigente no se rompe
```

**Notas técnicas:** reutilizar `ResolveOtUserScopeAsync` (`AdminOtEndpoints.cs`, ya usado por
Requisitos) dentro de `CreateOtRuleAsync`/`ListRulesAsync`/`UpdateRuleAsync`, `UpdateProfileAsync` y
`UpdateFeatureFlagAsync`. Tests: extender `tests/Flit.Admin.Tests/…` con el mismo patrón de
`AdminOtRequirementsScopeTests.cs` para Reglas, Perfil y Feature Flags.

---

### HU-B2 `[BACKEND] – Admin OT – Restringir Reglas, Requisitos y Configuración a Super Admin`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-B1 |

**Como** Líder Técnico
**quiero** que Reglas, Requisitos y Configuración exijan la policy `SuperAdmin` en vez de la policy
general del módulo OT
**para** que ni Admin OT ni Operador OT puedan leer ni escribir esas configuraciones desde la API

```gherkin
AC1 — positivo
Dado un ot_admin o un gestor_tramites_ot autenticado
Cuando llama GET/POST/PUT/PATCH sobre /rules, /requirements, PATCH /profile o PATCH /feature-flags/{id}
Entonces la API responde 403 con AdminAuthorization.ForbiddenMessage

AC2 — negativo
Dado un Super Admin autenticado con ?transitOfficeId válido
Cuando llama esos mismos endpoints
Entonces la API los procesa con 200/201 igual que antes de esta HU

AC3 — borde
Dado GET /profile (sin PATCH)
Cuando cualquier usuario de un tenant OT lo llama
Entonces sigue respondiendo 200 sin exigir Super Admin
Y Shell.goOtHub / OtHubLayout no se rompen
```

**Notas técnicas:** agregar `.RequireAuthorization(AdminAuthorization.SuperAdminPolicy)` como
override por endpoint (mismo patrón usado en `AdminCompanyChildrenConfigEndpoints.cs`) sobre Reglas
(POST/GET/PATCH), Requisitos (GET/PUT), `PATCH /profile` y `PATCH /feature-flags/{id}` — **sin**
tocar `GET /profile`, que hereda la policy general del grupo. Actualizar
`Authorization/OtModuleAuthorizationHandlerTests.cs` con casos 403 para `ot_admin` y
`gestor_tramites_ot` en estos endpoints.

---

### HU-B3 `[FRONTEND] – Admin OT – Ocultar Reglas, Requisitos y Configuración del dock y bloquear acceso directo`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-B2 |

**Como** Admin OT u Operador OT
**quiero** dejar de ver Reglas, Requisitos y Configuración en el dock, y que si escribo la URL
directamente me redirija
**para** que la restricción del backend también se refleje en la interfaz (bloqueo en dos capas)

```gherkin
AC1 — positivo
Dado cualquier usuario de un tenant OT (admin u operador) en el dock
Cuando el Shell arma las entradas del bloque isOtUser
Entonces no incluye las entradas de Reglas, Requisitos ni Configuración

AC2 — negativo
Dado un ot_admin que navega directamente a /admin/transit-offices/{id}/rules, /requirements
  o /configuracion
Cuando la página se monta
Entonces redirige a /403 (guard específico, no solo el guard genérico que hoy permite todo
  /admin/transit-offices/*)

AC3 — borde
Dado un Super Admin en esas mismas 3 rutas dentro de OtHubLayout (con su barra de pestañas)
Cuando navega
Entonces sigue viendo y usando las 3 pestañas sin cambios
```

**Notas técnicas:** `Shell.tsx` — retirar del bloque `if (currentUser?.isOtUser)` los objetos de
Reglas/Requisitos/Configuración; `dockGroups.ts` — retirar esas 3 claves de `DOCK_ITEM_GROUP` (el
grupo `administracion` sigue vivo porque Documentos permanece ahí, ver Feature C); nuevo guard —
extender `frontend/lib/auth/guard.ts` (patrón `evaluateAdminAccess`) o un chequeo local en
`[id]/rules/page.tsx`, `[id]/requirements/page.tsx`, `[id]/configuracion/page.tsx` que exija
`isSuperAdmin`. Actualizar `components/atom/__tests__/Shell.test.tsx` (hoy afirma que Admin OT los
ve — esta HU invierte esa aserción).

---

### HU-B4 `[FRONTEND] – Admin OT – Retirar la ruta legacy [id]/tramites`

| Campo | Valor |
|---|---|
| SP | 2 |
| Depende | Ninguna |

**Como** Super Admin
**quiero** dejar de tener una ruta legacy duplicada sin enlace en ningún menú
**para** no mantener dos formas de llegar a la misma configuración ahora que existe /configuracion

```gherkin
AC1 — positivo
Dado que /configuracion ya expone el modo Dashboard/QX, la ventana de revocatoria y los feature
  flags operativos
Cuando se retiran app/admin/transit-offices/[id]/tramites/page.tsx y TramitesSuperSection.tsx
Entonces /configuracion sigue exponiendo esas mismas funciones sin pérdida

AC2 — negativo
Dado un usuario que ya tenía guardada la URL /admin/transit-offices/{id}/tramites
Cuando la visita tras esta HU
Entonces recibe 404 (ruta eliminada), no un error 500 ni una pantalla en blanco

AC3 — borde
Dado OT_HUB_TABS y OT_ADM_DOCK
Cuando se revisan tras el borrado
Entonces ninguno referencia el id "tramites" de la pantalla legacy
Y el id `tramites`/`client-procedures` que sigue vivo es el de la bandeja, sin confusión
```

**Notas técnicas:** eliminar `app/admin/transit-offices/[id]/tramites/page.tsx` y
`TramitesSuperSection.tsx`; confirmar que ningún test los referencia antes de borrarlos.

---

## Feature C — Documentos y Prelación: el Admin OT solo ordena (4 HUs · 18 SP)

### HU-C1 `[BACKEND] – Admin OT – Resolver transitOfficeId de Super Admin en Prelación y Etiquetas`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | Ninguna — bloquea a HU-C2 |

**Como** Super Admin FLIT
**quiero** que la prelación documental y las etiquetas resuelvan el organismo por
`?transitOfficeId` cuando quien llama es Super Admin
**para** dejar de leer o escribir por accidente en mi propio tenant en vez del organismo que
administro

```gherkin
AC1 — positivo
Dado un Super Admin que llama GET/PATCH /api/v1/admin/ot/document-precedence
  o POST/GET/DELETE /document-tags con ?transitOfficeId=X
Cuando el handler resuelve el tenant
Entonces usa el tenant de X (mismo patrón que Requisitos)

AC2 — negativo
Dado un Super Admin que omite ?transitOfficeId
Cuando llama esos endpoints
Entonces responde 400
Y no escribe en su propio tenant de Super Admin

AC3 — borde
Dado un ot_admin que llama con su propio JWT (sin transitOfficeId)
Cuando se resuelve el scope
Entonces sigue usando su tenant como hoy, sin romper el caso vigente
```

**Notas técnicas:** reutilizar `ResolveOtUserScopeAsync` en `ListDocumentPrecedenceAsync`/
`UpdateDocumentPrecedenceAsync` y `CreateDocumentTagAsync`/`ListDocumentTagsAsync`/
`DeleteDocumentTagAsync` (`AdminOtEndpoints.cs`). Tests: nuevos, siguiendo el patrón de
`AdminOtRequirementsScopeTests.cs`.

---

### HU-C2 `[BACKEND] – Admin OT – Policy "solo ordena": Prelación para ot_admin + Super Admin, resto solo Super Admin`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-C1 |

**Como** Líder Técnico
**quiero** una policy nueva que permita Prelación a `ot_admin` y a Super Admin (sin abrirla al resto
de roles OT), y dejar Etiquetas, el switch de prenda y los overrides de exigencias documentales
exclusivos de Super Admin
**para** que el Admin OT conserve solo el ordenamiento y pierda todo lo demás, como exige la épica

```gherkin
AC1 — positivo
Dado un ot_admin autenticado
Cuando llama GET/PATCH /document-precedence
Entonces la API responde 200 (sigue pudiendo ordenar)
Cuando llama POST/GET/DELETE /document-tags, el PATCH de prenda o los overrides de exigencias
Entonces responde 403 en los tres

AC2 — negativo
Dado un gestor_tramites_ot u otro rol OT personalizado
Cuando llama GET/PATCH /document-precedence
Entonces responde 403 (la policy nueva no es la genérica del módulo OT: no deja pasar por
  entity_type de tenant, solo por rol SuperAdmin u ot_admin)

AC3 — borde
Dado un Super Admin con ?transitOfficeId válido
Cuando llama cualquiera de los 4 grupos de endpoints (precedence, tags, prenda, overrides)
Entonces todos responden 200/201 sin restricción
```

**Notas técnicas:** crear `AdminAuthorization.OtAdminOrSuperAdminPolicy` + su
`AuthorizationRequirement`/handler (exige rol `SuperAdmin` **o** `ot_admin`, a diferencia de
`OtModuleRequirement`, que además deja pasar a cualquier `entity_type=TRANSIT_OFFICE`) y registrarla
en `ApiSecurityExtensions.cs`; aplicarla a `document-precedence` GET/PATCH. Cambiar a
`AdminAuthorization.SuperAdminPolicy` `document-tags` POST/GET/DELETE,
`AdminOtPrendaDocumentPolicyEndpoints.cs` (ya *scoped* por oficina, solo cambia la policy) y
`AdminDocumentRequirementOverridesEndpoints.cs` (sin consumidor en UI hoy). Actualizar
`OtModuleAuthorizationHandlerTests.cs`.

---

### HU-C3 `[FRONTEND] – Admin OT – Restringir el dock "Documentos" a ot_admin y Super Admin`

| Campo | Valor |
|---|---|
| SP | 3 |
| Depende | HU-C2 |

**Como** Operador OT u otro rol OT personalizado
**quiero** dejar de ver la entrada "Documentos" en el dock
**para** no entrar a una pantalla cuya API ya no me autoriza, salvo la función de ordenar que
tampoco debo tener

```gherkin
AC1 — positivo
Dado un ot_admin en el dock
Cuando el Shell arma las entradas del bloque isOtUser
Entonces "Documentos" sigue apareciendo, sin cambios para Admin OT

AC2 — negativo
Dado un gestor_tramites_ot u otro rol OT personalizado en el dock
Cuando el Shell arma las entradas
Entonces "Documentos" ya no aparece — se mueve dentro del condicional currentUser.isOtAdmin,
  igual que hoy hace "Usuarios"

AC3 — borde
Dado un Super Admin navegando el hub vía OtHubLayout/OtTabBar
Cuando abre Documentos
Entonces la pestaña sigue visible sin cambios (Super Admin no pasa por este bloque del dock)
```

**Notas técnicas:** `Shell.tsx` — mover el objeto `key: OT_ADM_DOCK.documents` dentro del spread
condicional `...(currentUser.isOtAdmin ? [...] : [])`, mismo patrón que hoy usa "Usuarios".
Actualizar `Shell.test.tsx` con el caso operador-sin-Documentos.

---

### HU-C4 `[FRONTEND] – Admin OT – Solo Prelación visible; Etiquetas y Prenda quedan para Super Admin`

| Campo | Valor |
|---|---|
| SP | 5 |
| Depende | HU-C2 |

**Como** Admin OT
**quiero** abrir Documentos y ver solo la pestaña Prelación, sin Etiquetas ni el switch de documento
de prenda
**para** no ver controles que la épica reserva a Super Admin y que mi rol ya no puede usar en la API

```gherkin
AC1 — positivo
Dado un ot_admin que abre DocumentsSection
Cuando la página resuelve su rol
Entonces solo renderiza la pestaña "Prelación" (DocumentPrecedenceList + handleReorder)
Y no ofrece selector de tabs porque Etiquetas no existe para este rol

AC2 — negativo
Dado ese mismo ot_admin
Cuando se inspecciona el árbol renderizado
Entonces no existen ni el tab "Etiquetas" ni el switch "Documento de prenda por compañía"
  (PledgeDocumentOverrideToggle no se monta)

AC3 — borde
Dado un Super Admin en la misma página (vía OtHubLayout)
Cuando la abre
Entonces ve ambas pestañas (Prelación y Etiquetas) y el switch de prenda, sin cambios respecto a hoy
```

**Notas técnicas:** `DocumentsSection.tsx` — condicionar con `isSuperAdmin(payload)` (mismo helper
que usa `OtHubLayout.tsx`) el tab bar de Etiquetas y el switch de prenda
(`PledgeDocumentOverrideToggle.tsx`); si no es Super Admin, fijar `tab` siempre en `"precedence"` sin
ofrecer el selector. Actualizar `__tests__/DocumentsSection.test.tsx` con el caso ot_admin (solo
Prelación) y el caso Super Admin (ambas pestañas).

---

## Orden de implementación

```
A1 → A2, A4
A3, A5 (independientes)

B1 → B2 → B3
B4 (independiente)

C1 → C2 → C3, C4
```

Entre Features: A no depende de B ni C. B y C comparten archivo (`AdminOtEndpoints.cs`, `Shell.tsx`)
sin dependencia funcional — se recomienda mergear B antes que C para minimizar conflictos.

## DoR de las HUs (al crear)

PASS previsto: título `[FRONTEND]`/`[BACKEND]` con guion largo, Como/quiero/para, 3 AC Gherkin
(positivo/negativo/borde), SP Fibonacci, `Custom.Refinement="True"`, dependencias explícitas,
AssignedTo humano, tag `DOR`, sin TODO/TBD, sin datos sensibles.
Sprint: siguiente al activo (a definir por la sesión principal al registrar).
Parent Feature en `New` (no Active): la HU se crea igual; no se activa implementación sin
confirmación humana explícita (Motivo A del ciclo de vida HU).
