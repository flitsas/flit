# Plan técnico — Módulo de historial operativo por placa

> Generado: 2026-09-08 · rama base `develop` @ `43110709` · origen: `docs/PLACA-historial-operativo.md`
> (borrador de Feature del PO) · **Feature ADO: #12189** (New).
>
> Diagnóstico por lectura estática (5 barridos `explore-agent` + verificación manual de los puntos
> decisivos). Las anclas `archivo:línea` derivan con cualquier commit: reverificar antes de citarlas.

---

## 0. Resumen ejecutivo

**La mayor parte del backend ya existe.** `GET /api/v1/tramites/instances?placa=X` filtra trámites por
placa desde hace tiempo, y su DTO `InstanceSummary` ya sirve **todos** los campos que pide el criterio
funcional del PO (id, tipo, estado, fechas, gestor, OT, comprador, vendedor, VIN). De hecho **DR. FLIT
ya consume ese mismo endpoint** para su búsqueda por placa.

Por tanto esto **no es "construir un módulo de historial"**, es:

1. **envolver** lo que ya hay en un módulo dedicado con su permiso RBAC y su entrada de menú,
2. **garantizar** el orden cronológico y la paginación completa que hoy dependen de parámetros opcionales,
3. **hacer sargable** el filtro de placa (hoy no usa índice), y
4. **añadir el punto de entrada** desde el Dashboard de Trámites.

Lo único que **sí** es trabajo de fondo en backend es el alcance mixto por rol que fijó el PO en D1:
`SuperAdmin` consulta todas las compañías, el resto solo la propia.

Estimación: **7 HUs, 26 SP**. Tres PRs para respetar el límite de 800 líneas.

---

## 1. Verificación de la propuesta del PO

Tres afirmaciones del borrador necesitan corrección antes de descomponer.

### 1.1 Cuatro de los cinco roles nombrados no existen en el código — pero eso **no bloquea**

La propuesta pide acceso para `Admin`, `Documentador`, `OperarioFull`, `Validador` y `Gestor`.
En el código solo existen cuatro roles: **`SuperAdmin`, `AdminCompany`, `ot_admin`, `Radicador`**
(`Flit.Infrastructure/Security/DevelopmentAuthSeeder.cs:128-138,303,396,781`).
`Documentador`, `OperarioFull`, `Validador` y `Gestor` no aparecen como *role code* en ningún archivo.

**Pero los roles son datos, no código.** SuperAdmin los gobierna por CRUD:
`POST /api/v1/superadmin/roles`, `PUT /roles/{id}/permissions`, `DELETE /roles/{id}`
(`Flit.Api/Endpoints/SuperAdmin/SecurityRolesEndpoints.cs:45,70,135`). El seeder solo puebla DEV;
QA y PDN pueden tener perfectamente roles creados con esos nombres.

**Decisión de diseño derivada:** el módulo se protege con un **permiso**, no con nombres de rol.
Se crea el slug `historial-placa.read` siguiendo la convención existente
(`dashboard.read`, `tramites.read`, `validaciones.read`, `improntas.read`, `reportes.read`…),
y el SuperAdmin lo asigna a los roles que existan en cada ambiente. Así el criterio del PO se cumple
sin cablear nombres de rol en el código ni inventar roles nuevos.

> **Resuelto (D4):** el PO acotó el acceso a `Radicador`, `AdminCompany` y `SuperAdmin` — los tres
> existen en el código, así que no hay que crear roles nuevos. El diseño por permiso se mantiene:
> si mañana aparece un rol `Documentador` creado por SuperAdmin, basta asignarle el slug.

### 1.2 "Solo existe una búsqueda puntual en DR. FLIT" — cierto en alcance, engañoso en implementación

DR. FLIT no es una ruta propia: es el módulo `Ayuda` dentro de la SPA (`components/atom/modules/Ayuda.tsx`)
con un FAB y un chat (`components/dr-flit/DrFlitFab.tsx`, `DrFlitAssistant.tsx`).
Su búsqueda por placa (`components/dr-flit/dr-flit-search.ts:72-79`) llama a
`tramitesClient.listInstances({ placa: value.toUpperCase(), take: 50, skip: 0 })`.

Es decir: **ya usa exactamente el endpoint que necesita el módulo nuevo**, solo que capado a 50
resultados, sin orden explícito y sin pantalla propia. El módulo nuevo **no necesita backend de búsqueda
nuevo** — necesita una superficie propia y garantías que hoy no están.

### 1.3 "Dashboard de Trámites" = la ruta `/tramites`

Es `frontend/app/tramites/page.tsx` → `OperacionView` → `TramitesTable.tsx` (el listado operativo),
no el módulo analítico `?m=dashboard`. `TramitesTable` **ya pinta la placa por fila** en la columna
compuesta «Vehículo» (placa + VIN) — `TramitesTable.tsx:296,620,1362`. El punto de entrada del criterio
"acceder al historial de la placa asociada a cada trámite" cuelga naturalmente de ahí.

---

## 2. Lo que ya existe y se reutiliza

| Pieza | Dónde | Estado |
|---|---|---|
| Filtro por placa en el listado | `GET /api/v1/tramites/instances?placa=` → `ListProcedureInstancesFilteredQuery` | funciona |
| Comparación de placa | `x.Plate.ToUpper() == placa` (`ProcedureInstanceRepository.cs:1827-1831`) | case-insensitive |
| DTO con los campos pedidos | `InstanceSummary` (`frontend/lib/api/types/procedure-runtime.ts:132-215`) | completo |
| Orden por whitelist | `ProcedureInstanceSortFields.Resolve` acepta `createdAt` / `updatedAt` | existe |
| Paginación con total | `tramitesClient.listInstancesPage` / `POST /instances/search` | existe |
| Cliente frontend | `tramitesClient.listInstances({ placa })` (`tramites-client.ts:501-518,569-577`) | existe |
| Patrón "input placa → tabla" | `OtImprintValidationSection.tsx` (PR #348) + `DataTable` + `UiStateBoundary` | clonable |
| Autorización por permiso | `RequirePermission` + `PermissionAuthorizationHandler.cs:19-46` | existe (1 solo uso hoy) |
| Modal de detalle «Ver» | `TramiteDetalleModal.tsx`, montado en `TramitesTable.tsx:1225` | **montado y ya de solo lectura** |
| Timelines de detalle | `TimelineTrackPanel.tsx` + `timeline-mappers.ts` | montados dentro del modal |

---

## 3. Brechas reales

### G1 · No hay módulo, ni permiso, ni entrada de menú

No existe ruta dedicada, ni `ModuleId` en `frontend/lib/nav/modules.ts`, ni slug de permiso.
Registrar un módulo exige: backend — seed de `SecurityModule` + fila en `security.permissions`
(slug / module_id / http_method / route_pattern) + `security.role_permissions`; frontend —
`ALL_MODULE_IDS` y `SPA_DOCK_MODULE_IDS` en `lib/nav/modules.ts` y el dock en `components/atom/Shell.tsx`.

### G2 · El orden cronológico no está garantizado

El criterio pide "más reciente primero por defecto". Hoy `SortDescending` es `true` por defecto
(`ProcedureInstanceEndpoints.cs:182-184`) pero **`SortBy` es opcional**: sin él, el orden real lo decide
el handler, no el contrato. El módulo debe fijar `sortBy=createdAt&sortDir=desc` explícitamente.

### G3 · El filtro de placa no usa índice — hallazgo de rendimiento

El único índice es `ix_procedure_instances_tenant_id_plate ON (tenant_id, plate)`
(`Persistence/Sql/Ddl/47-tramites-campos-busqueda.sql:33-34`), pero el predicado que genera EF es
`upper(plate) = @placa` (`ProcedureInstanceRepository.cs:1830`). **Una función sobre la columna anula el
índice**: Postgres puede aprovechar el prefijo `tenant_id` y luego filtra fila a fila. Con un tenant
grande eso degrada.

Corrección: índice funcional `(tenant_id, upper(plate))`. Es una migración de una línea y resuelve
también el mismo problema en el filtro de VIN (`x.Vin.ToUpper() == vin`, línea 1824).

### G4 · Alcance de los datos: tenant y borrados

El repositorio filtra siempre `TenantId == tenantId` **y** `DeletedAt == null`
(`ProcedureInstanceRepository.cs:38,95,228`). Consecuencias sobre el criterio *"todos los trámites
históricos asociados a esa placa dentro de FLIT"*:

- **Es el historial dentro del tenant del usuario**, no de todo FLIT. Un traspaso de la misma placa
  gestionado por otra compañía **no aparecerá**.
- **Los trámites con borrado lógico quedan fuera** del "historial completo".

Ambas quedaron resueltas por el PO en §4: el alcance pasa a ser **mixto por rol** (SuperAdmin ve todas
las compañías, el resto solo la suya) y los borrados lógicos **no** entran. La primera obliga a tocar el
repositorio; la segunda ya es el comportamiento actual y solo hay que no romperlo.

### G5 · "Información completa de cada trámite" está sin cerrar

`InstanceSummary` cubre la fila resumen. El criterio dice "y demás campos disponibles", lo que puede
significar también el **historial de estados** de cada trámite
(`tramites.procedure_instance_status_history`: from/to, `reason`, `metadata`, autor —
`TramiteTransitionRecorder.cs:31`). Eso es un detalle expandible, no una columna, y cambia el tamaño.

### G6 · No hay acción por fila en el Dashboard

`TramitesTable` pinta la placa pero no ofrece "ver historial de esta placa". Hay que añadir la acción
y respetar el permiso (una fila puede verse sin tener acceso al historial).

---

## 4. Decisiones del PO (cerradas 2026-09-08)

| # | Decisión | Resolución |
|---|---|---|
| D1 | Alcance de tenant | **Mixto por rol.** `SuperAdmin` ve los registros de la placa en **todas** las compañías; el resto de roles ve únicamente los de su propia compañía. |
| D2 | ¿Entran los trámites con borrado lógico? | **No.** |
| D3 | Nivel de detalle | Fila **resumen y de solo lectura**; el detalle del trámite se abre en un **modal equivalente al del módulo de Trámites**. |
| D4 | Roles con acceso | **`Radicador`, `AdminCompany`, `SuperAdmin`** — los tres existen en el código, así que no hace falta crear roles. El permiso `historial-placa.read` se les asigna. |

### 4.1 Consecuencia técnica de D1 — el endpoint deja de ser trivial

**Corrección al diagnóstico inicial (2026-09-08):** la ruta cross-tenant **ya existe en el contrato**.
`ProcedureInstanceListRequest.TenantId` es `Guid?`, el repositorio aplica el filtro de forma condicional
(`query = query.Where(x => x.TenantId == tid)` solo si `tenantId is { } tid`) y el endpoint ya resuelve
el contexto con `ResolveTenantContext(http)`, que devuelve `(Guid? TenantId, bool IsSuperAdmin)` y entrega
`null` al SuperAdmin. Las lecturas puntuales por id sí filtran siempre por tenant
(`ProcedureInstanceRepository.cs:23,137,143…`), pero el **listado filtrado** no.

Por tanto la HU-2 no tiene que abrir la ruta cross-tenant: tiene que **cerrarla para todos menos
SuperAdmin** de forma explícita y verificable, en vez de confiar en un `null` que hoy llega por middleware.

Dos cuidados no negociables:

- **La ruta cross-tenant solo puede abrirse para `SuperAdmin`.** Cualquier otro rol conserva el filtro
  por su tenant. El bug de scope que ya mordió dos veces en el módulo de improntas nace justo de
  confundir el tenant del dato con el del consultante.
- **La respuesta debe identificar la compañía de cada fila.** `InstanceSummary` ya trae `tenantId` y
  `companiaNombre`, así que no hace falta ampliar el DTO — pero la columna solo se pinta para SuperAdmin.

Sobre PII: `SuperAdmin` es un rol global que ya ve todos los tenants en el resto del producto, así que
esto no amplía su superficie de datos y **no requiere ADR nuevo**. Sí conviene que la consulta quede en
la bitácora de auditoría del módulo.

---

## 5. Descomposición en HUs

### HU-1 · [BACKEND] Permiso `historial-placa.read` y módulo en RBAC — 3 SP

Seed de `SecurityModule` + `security.permissions` + `role_permissions`; el módulo queda visible en la
pantalla RBAC de SuperAdmin para asignarlo a los roles de cada ambiente.
**AC clave:** un usuario sin el permiso recibe 403 y no ve la entrada de menú.

### HU-2 · [BACKEND] Endpoint de historial por placa con alcance por rol — 5 SP

`GET /api/v1/tramites/instances/plate-history?placa=X&skip&take`, protegido con
`RequirePermission("historial-placa.read")`, delegando en `ListProcedureInstancesFilteredQuery`
con `SortBy=createdAt`, `SortDescending=true` y `total` en la respuesta.
Normaliza la placa (`Trim().ToUpperInvariant()`) **en el endpoint**, no en el cliente.
Resuelve el alcance según D1: `SuperAdmin` sin filtro de tenant, resto acotado a su tenant.
Excluye siempre los trámites con borrado lógico (D2).
**AC clave:** un `AdminCompany` que consulta una placa con trámites en otra compañía recibe
únicamente los suyos; el mismo caso como `SuperAdmin` los devuelve todos con su `companiaNombre`.
Placa inexistente → `200` con `items: []` y `total: 0`, nunca 404.

> Extraer de paso un helper compartido de normalización de placa: hoy `Trim().ToUpperInvariant()`
> está duplicado literal en `VehicleSignatureImprintRepository.cs:109` y
> `ValidateImprintSignatureHandler.cs:110`. Este sería el tercer consumidor.

### HU-3 · [BACKEND] Índices funcionales para la búsqueda por placa — 2 SP

Migración con dos índices, porque D1 abre **dos** caminos de consulta:

- `(tenant_id, upper(plate))` — para los roles acotados a su compañía;
- `(upper(plate))` — para la consulta global de `SuperAdmin`, que no lleva `tenant_id` en el predicado
  y por tanto no puede apoyarse en el anterior.

Validada con la skill `db-schema-validator`.
**AC clave:** `EXPLAIN` de ambos caminos usa el índice correspondiente, sin seq scan.

### HU-4 · [FRONTEND] Módulo dedicado de historial por placa — 5 SP

Ruta nueva + entrada de menú condicionada por `accessibleCodes`, buscador de placa, tabla de resultados
con los campos del criterio funcional y los 4 estados de UI (vacío / cargando / error / lleno) vía
`UiStateBoundary` + `DataTable`. Clonar la estructura de `OtImprintValidationSection.tsx`.
**AC clave:** orden cronológico descendente visible y verificable; WCAG 2.1 AA.

### HU-5 · [FRONTEND] Modal de detalle del trámite en solo lectura — 5 SP

Desde cada fila del historial se abre el **mismo** modal de detalle del módulo de Trámites
(`components/operacion/TramiteDetalleModal.tsx`). **Corrección al diagnóstico inicial (2026-09-08):**
el modal NO está desmontado — se monta en `TramitesTable.tsx:1225`, es el modal «Ver» y ya es de solo
lectura, con `TimelineTrackPanel` y los mappers de historial dentro. La HU se reduce a reutilizarlo
desde el módulo nuevo y verificar que no expone ninguna acción de escritura.
**AC clave:** el modal no expone ninguna acción de escritura; se cierra con `Esc` y devuelve el foco
al disparador (WCAG 2.1 AA).

### HU-6 · [FRONTEND] Acceso al historial desde el Dashboard de Trámites — 3 SP

Acción por fila en `TramitesTable` que abre el historial de la placa de ese trámite, respetando el
permiso. Misma información que el módulo dedicado (criterio explícito del PO).
**AC clave:** un usuario sin `historial-placa.read` no ve la acción.

### HU-7 · [FRONTEND] Calidad de interfaz del módulo — 3 SP

Fidelidad al design system de FLIT bajo la skill `flit-design-guardian`: tokens de color, tipografía y
espaciado; responsive; modo oscuro; los cuatro estados de interfaz con vacíos redactados (no genéricos);
foco visible y navegación completa por teclado.
**AC clave:** auditoría de fidelidad sin desviaciones de token; contraste AA verificado en claro y oscuro.

**Total: 26 SP** (Fibonacci: 3 + 5 + 2 + 5 + 5 + 3 + 3).

*Fuera de alcance, candidato a HU posterior:* línea de tiempo de estados dentro del modal con
`procedure_instance_status_history` (montaría `EstadoTimeline` / `TimelineTrackPanel`).

---

## 6. Secuencia de ejecución

```
PR 1 (backend)     HU-1 → HU-2 → HU-3               ~450 líneas
PR 2 (frontend)    HU-4 → HU-5                      ~550 líneas
PR 3 (frontend)    HU-6 → HU-7                      ~350 líneas
```

HU-1 va primero: HU-2 depende del slug de permiso, y todo el frontend depende de que el módulo exista
en `accessibleCodes`. HU-3 es independiente y puede paralelizarse. Tres PRs en vez de dos porque
el modal (HU-5) y la HU de interfaz elevan el volumen por encima del límite de 800 líneas por PR.

---

## 7. Riesgos y notas

1. **El módulo puede parecer "vacío" en DEV.** El seeder de DEV solo tiene 4 roles; hasta que el
   SuperAdmin asigne `historial-placa.read`, nadie salvo SuperAdmin verá el módulo. No es un bug.
2. **Solapamiento con DR. FLIT.** El criterio dice que el módulo es independiente del módulo de ayuda,
   pero ambos consultarán el mismo backend. Conviene que DR. FLIT pase a **enlazar** al módulo nuevo
   en vez de mantener su propia lista capada a 50 — no está en alcance, pero deja el sistema coherente.
3. **La placa no es una entidad.** No existe tabla `vehicles`; la placa se denormaliza desde
   `procedure_instance_field_values` (`field_key='plate'`) a `procedure_instances.plate` por trigger
   (`47-tramites-campos-busqueda.sql:54-76`). Sirve para este módulo, pero cualquier ampliación futura
   ("ficha del vehículo") choca contra esa ausencia.
4. **A favor:** `tr_procedure_instance_field_values_immutable` (`06-HU10150-procedure-instances.sql:119-137`)
   congela los `field_values` en cuanto la instancia sale de `draft`. La placa de un trámite radicado
   no cambia → el histórico es estable y no hay que reconstruir versiones de la placa.
5. **Proceso:** el Sprint activo es **Sprint 4** (2026-09-07 → 2026-09-11) y **no existe Sprint 5**.
   La regla FLIT exige registrar en el sprint siguiente al activo: hay que crear Sprint 5 antes de
   registrar el Feature y las HUs en ADO.
