# Anexo A — Permisos del módulo OT (Épica #12753, Feature #13100 F-A, criterio FA-1)

> Estado: **Aprobado** por Willyn Londoño el 2026-10-16 (HU #13637). Desbloquea FA-2 (#13638), FA-3/FA-4 (#13639) y FA-6 (#13641).
>
> Decisiones de la Parte 4:
> - **P1 — Sí:** las rutas de usuarios, invitaciones, webhooks y api-logs del OT quedan solo para `ot_admin` (slugs `ot.usuarios.manage` y `ot.integracion.manage` asignados solo a `ot_admin`). Lo implementa #13639.
> - **P2 — Flag:** la exigencia del slug OT (FA-4) queda detrás de un flag de configuración apagado por defecto; se enciende al menos 12 h después del despliegue que asigna los slugs.
> - **P3, P4, P5:** según la recomendación del documento (los usuarios OT sin rol pierden las pestañas que ya les daban 403; un slug por pestaña; siete módulos).
> Rama: `feature/AB-13100-catalogo-permisos-admin-ot` (base `origin/develop` 96b576800). Solo documento: no se tocó código.
> Convención de rutas: todas llevan el prefijo `/api/v1` (omitido en las tablas). Los archivos de la Parte 2 están en `services/core-api/src/Flit.Api/Endpoints/`.

## Resumen ejecutivo

- **8 slugs nuevos** en **7 módulos RBAC nuevos**, todos de producto `tramites`.
- **123 rutas** inventariadas bajo `OtModule` / `OtAdminOrSuperAdmin` (la política `OtAdmin` está definida pero **no tiene ningún uso**). Desglose: **90** quedan cubiertas por un slug OT, 19 siguen siendo solo SuperAdmin (`SuperAdmin` se suma encima de `OtModule`), 12 responden `410 Gone` y 2 son la "base OtModule" (perfil y catálogo de organismos) que no llevan slug.
- Hallazgos que cambian el plan: ver §0.

## 0. Hallazgos que cambian el plan (verificados en código)

1. **`ot_admin` no está sin permisos.** El seeder le concede `consolidado-masivo.download` (`DevelopmentAuthSeeder.cs:1671`, `SeedConsolidadoMasivoPermissionsAsync`, HU #13369). Esa ruta (`POST /admin/ot/consolidados/lotes`, `AdminOtEndpoints.cs:348`) ya usa el patrón `RequirePermission` **en AND** con `OtModule`. Ese es el patrón que FA-4 debe generalizar: no hace falta inventar otro.
2. **`ot_admin` es de producto `tramites`** (`Ddl/120-HU12964-rbac-por-producto.sql`, líneas 32-37, y trigger `tr_role_permissions_same_product`, líneas 223-252). Por eso **ningún slug OT puede vivir en un módulo `plataforma`**: el módulo `usuarios` es `plataforma` y el trigger rechazaría el grant. Los slugs de usuarios OT van en un módulo propio (`ot-usuarios`, `tramites`).
3. **El rol "Operador OT" `gestor_tramites_ot` no existe en el repo**: no está en el seeder, ni en migraciones ni en DDL; solo se cita en comentarios y tests. Son filas de datos creadas desde RBAC Admin (catálogo global `TRANSIT_OFFICE` y, desde HU #13440, también roles por tenant con `tenant_id`). **El backfill de FA-3 no puede enumerar códigos de rol: debe seleccionar por `target_entity_type = 'TRANSIT_OFFICE'`.** El comentario de `IdentityModuleAccessFilter.cs:13` confirma que el Operador OT trae hoy `dashboard.read` y `tramites.read`.
4. **Las rutas de usuarios OT están abiertas a cualquier usuario de un tenant OT** (`/admin/ot/users*` y `/admin/ot/invitations*`: solo `OtModule`; `ResolveOtUserScopeAsync`, `AdminOtEndpoints.cs:3290`, no mira el rol). La UI las oculta a los no-admin (`tramitesNav.ts:171-190`), la API no. Lo mismo ocurre con webhooks y api-logs. Aplicar un slug ahí es un **endurecimiento**, no una equivalencia; se decide en P1.
5. **Los permisos viajan en el JWT** (`PermissionAuthorizationHandler.cs`, claim `permissions`; vida del token 12 h, `appsettings.json:27`). Un grant nuevo no llega a las sesiones abiertas hasta el re-login. Si FA-4 se despliega junto con FA-3, los usuarios OT con sesión viva reciben 403 hasta renovar. Ver P2.
6. **El seeder no basta como único vehículo.** El DDL 120 declara que el seeder solo corre en Development (en la práctica los 3 VPS corren con `ASPNETCORE_ENVIRONMENT=Development`), pero el backfill de grants de roles ya existentes debe ser **DDL idempotente** (patrón de los DDL 120/133), con el seeder solo para bases nuevas.
7. **Cifras del encargo vs. código.** Usos de las políticas en `src/`: 25 `RequireAuthorization(...)` en 13 archivos; 178 menciones del símbolo en todo `services/` (incluye tests y comentarios). El catálogo del seeder tiene **34 slugs literales + 6 `AdminTramite*` = 40** (no 37). Ningún otro servicio (`core-ict`, `core-identity`, `core-comparendos`, `core-consultas`, `core-diagnostico`, `core-notificaciones`, `core-plantilla`, `python-ml`) usa estas políticas.
8. **`PermissionGate` no tiene ningún uso** fuera de su propio archivo y de tests; el frontend gobierna casi todo lo OT por rol/`entity_type`, no por slug (Parte 3).

---

## Parte 1 — Slugs OT propuestos

Convención: `modulo.accion` en minúsculas (como `usuarios.manage`, `improntas.generate`). Un slug por pestaña/módulo del hub OT; lectura y escritura separadas **solo** donde hoy ya hay diferencia de política (únicamente mandatarios: lectura `OtModule`, escritura `OtAdminOrSuperAdmin`).

### 1.1 Lista literal

| # | Slug | Módulo (nuevo) | Producto | Qué cubre | Rutas | Hoy lo abre |
|---|------|----------------|----------|-----------|-------|-------------|
| 1 | `ot.tramites.manage` | `ot-tramites` | tramites | Bandeja del organismo: listar/filtrar/contadores, detalle, aprobar/rechazar, revocatorias (aprobar/rechazar/consultar), consolidado (generar/descargar/entrega/maestro/lotes), documentos, adjunto de licencia, asignar/liberar/actualizar placa del trámite, cola Quipux (listar/reintentar/cancelar) | 29 | `OtModule` |
| 2 | `ot.improntas.manage` | `ot-improntas` | tramites | Validar impronta: listar, validar firma, previsualizar, historial de validaciones | 4 | `OtModule` |
| 3 | `ot.reportes.manage` | `ot-reportes` | tramites | Reportes del organismo: métricas, consultas (campos/ejecutar/guardadas), reglas de alerta y eventos, informes programados | 23 | `OtModule` |
| 4 | `ot.mandatos.manage` | `ot-mandatos` | tramites | Pestaña Mandatos: configuración del OT (firmante por defecto, reglas por compañía, vista previa, formatos) y **lectura** de mandatarios (listado, compañías, imagen de firma) | 11 | `OtModule` |
| 5 | `ot.mandatarios.manage` | `ot-mandatos` | tramites | **Escritura** de mandatarios: alta, edición, inactivar/reactivar, eliminar, impacto, validación de identidad (conciliar/reenviar), compañías asociables, baúl de firmas | 11 | `OtModule` + `OtAdminOrSuperAdmin` (3 con solo `OtAdminOrSuperAdmin`) |
| 6 | `ot.documentos.manage` | `ot-documentos` | tramites | Prelación documental del organismo (leer y ordenar) | 2 | `OtModule` + `OtAdminOrSuperAdmin` |
| 7 | `ot.usuarios.manage` | `ot-usuarios` | tramites | Usuarios del organismo: invitar, listar, editar, reenviar/cancelar/reactivar invitación | 6 | `OtModule` |
| 8 | `ot.integracion.manage` | `ot-integracion` | tramites | Webhooks y bitácora API del organismo (hoy fuera de la oferta de UI) | 4 | `OtModule` |

Total: **8 slugs, 7 módulos** (`ot-mandatos` aloja dos slugs). Los módulos nuevos llevan `product_code = 'tramites'` explícito (es también el DEFAULT de la columna). Los códigos de módulo coinciden con los ids de pestañas del hub, para que FC-2 (ocultar módulo en menú/URL) reutilice `accessibleCodes`. Nombre alterno si el PO prefiere: `.access` en lugar de `.manage` (los grupos mezclan lectura y escritura; `usuarios.manage` es el precedente de `.manage` para ambos).

**Rutas sin slug OT (decisión explícita):**

- `GET /admin/ot/profile` y `GET /admin/transit-offices` (búsqueda): son la **base de resolución del organismo** (comentario en `AdminOtEndpoints.cs:94-102`: `Shell.goOtHub`/`OtHubLayout` las usan antes de saber el rol). Se quedan en `OtModule` (tenant OT / ot_admin / SuperAdmin). Quitarle acceso a un usuario OT se hace con los slugs de pestaña, no aquí.
- 19 rutas con `SuperAdmin` encima del grupo `OtModule` (Reglas, Requisitos, perfil PATCH, feature flags, etiquetas, suspender/eliminar usuario, política de prenda, requisitos documentales, estado operativo, ajustes Quipux): siguen **solo SuperAdmin**; no se publican al Admin OT (Épica #12751).
- 12 rutas `410 Gone` (rangos de placa y `identity/*` deprecados): sin slug; se retiran con su deprecación.
- `POST /consolidados/lotes` conserva `consolidado-masivo.download` (existente, `ConsolidadoLotePermisos.Descargar`) y suma `ot.tramites.manage`. `POST /api/v1/consolidados/lotes/{loteId}/cancelacion` (`Tramites/ConsolidadoLoteEndpoints.Cancelacion.cs:38`) **no** usa las políticas OT (solo `RequirePermission`): queda fuera del inventario.

### 1.2 Slugs descartados (para no reabrirlos)

- `ot.consola.read` (perfil/búsqueda): descartado, ver arriba.
- `ot.revocatorias.manage`: las revocatorias se resuelven desde el detalle del trámite y la pestaña "Revocatorias" solo la ve SuperAdmin (`ot-nav.ts:22-49`); se pliegan en `ot.tramites.manage`.
- `ot.quipux.manage`: la cola Quipux no tiene UI para OT (ningún componente usa `fetchQuipuxCola`); se pliega en `ot.tramites.manage`.
- Separar lectura/escritura en trámites y reportes: hoy no hay diferencia de política; se puede añadir después sin regresión (P4).
- Reutilizar `reportes.ot.read`: ese slug gobierna la pestaña "Organismo de Tránsito" de `/analytics` para compañías (`Reportes.tsx:46`), otras rutas y otro público. Mezclarlos daría a un gestor de compañía acceso accidental al módulo del organismo.

### 1.3 Asignación por rol en la migración (FA-3)

| Grupo de roles | Slugs que recibe | Motivo |
|----------------|------------------|--------|
| `ot_admin` (catálogo global, `TRANSIT_OFFICE`) | **Los 8**, siempre | Hoy accede a todo lo OT (todas las rutas del inventario salvo las SuperAdmin) |
| Cualquier otro rol activo con `target_entity_type = 'TRANSIT_OFFICE'` (global o con `tenant_id`; p. ej. `gestor_tramites_ot` y personalizados) | **Recomendado: 1, 2, 3, 4** (`ot.tramites.manage`, `ot.improntas.manage`, `ot.reportes.manage`, `ot.mandatos.manage`) | Es lo que la UI les ofrece hoy en el dock (`tramitesNav.ts:174-190`: Trámites, Reportes, Mandatos, Validar impronta). **No** reciben 5 ni 6: la API ya los rechaza hoy (`OtAdminOrSuperAdmin`, HU #12859 / #13123) |
| Los mismos roles, paridad estricta de API | Opcional: además 7 y 8 | Solo si el usuario prefiere cero cambio de comportamiento de API (P1) |
| Usuarios del tenant OT **sin rol activo** | Ninguno | Hoy entran por `entity_type`; tras FA-4 pierden las pestañas (siguen resolviendo perfil/catálogo). Intencional (P3) |
| `SuperAdmin` | Todos (bypass por `role_code` en `PermissionAuthorizationHandler`; exento del trigger de producto) | Sin cambio |
| Roles de producto `plataforma` (`AdminCompany`) y roles `COMPANY` | Ninguno | El trigger los rechazaría; además no operan el organismo |

Reglas del backfill: `INSERT ... ON CONFLICT DO NOTHING`; selección por `r.target_entity_type = 'TRANSIT_OFFICE' AND r.deleted_at IS NULL`; módulos y acciones creados idempotentemente antes. Los roles de OT creados **después** del backfill no reciben nada por defecto (decisión del administrador, como `AdminTramite*`). Verificación previa en cada ambiente (database-agent), para confirmar que un tenant OT solo tiene roles `TRANSIT_OFFICE` (la suposición sale de `UserRoleAssignmentRepository.cs:55-59` y no se comprobó contra datos reales):

```sql
SELECT DISTINCT r.code, r.target_entity_type
FROM security.user_role_assignments a
JOIN security.roles r ON r.id = a.role_id
JOIN admin.transit_office_profiles p ON p.tenant_id = a.tenant_id
WHERE a.deleted_at IS NULL;
```

### 1.4 Patrón de enforcement recomendado para FA-4 (no se implementa aquí)

Mantener `OtModule` en el grupo (defensa en profundidad: tenant OT y filtro de alcance) y **añadir** `.RequirePermission("ot.xxx.manage")` por ruta, en AND, exactamente como `POST /consolidados/lotes`. Para las rutas `OtAdminOrSuperAdmin` (mandatarios, prelación, baúl de firmas, asociables) se **sustituye** esa política por el slug (`ot.mandatarios.manage`, `ot.documentos.manage`): el Admin OT puede entonces delegarlas por usuario, que es el propósito de la épica. El test `ConsolidadoLoteOtPermisoArchitectureTests` fija hoy que ninguna otra ruta del grupo gane el requisito por accidente: debe actualizarse con FA-4, no esquivarse. El alcance por organismo (`TransitOfficeScopeFilter`, `ForbidIfOfficeOutOfScopeAsync`) no lo sustituye ningún slug: sigue.

---

## Parte 2 — Inventario cerrado de rutas

Columnas: n.º, verbo, ruta (prefijo `/api/v1` omitido, `MapGroup` resuelto), archivo:línea (línea del `Map*`; en los grupos en línea del mandatario `associable-companies`, la de la sentencia), política efectiva (la del grupo más las que añade la ruta) y slug propuesto.

| n.º | Verbo | Ruta | Archivo:línea | Política efectiva | Slug propuesto |
|-----|-------|------|---------------|-------------------|----------------|
| 1 | GET | `/admin/ot/profile` | AdminOtEndpoints.cs:90 | OtModule | (sin slug: base OtModule, ver 1.4) |
| 2 | PATCH | `/admin/ot/profile` | AdminOtEndpoints.cs:103 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 3 | PATCH | `/admin/ot/feature-flags/{id}` | AdminOtEndpoints.cs:116 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 4 | GET | `/admin/ot/requirements` | AdminOtEndpoints.cs:128 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 5 | PUT | `/admin/ot/requirements` | AdminOtEndpoints.cs:139 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 6 | POST | `/admin/ot/webhooks` | AdminOtEndpoints.cs:151 | OtModule | `ot.integracion.manage` |
| 7 | GET | `/admin/ot/webhooks` | AdminOtEndpoints.cs:159 | OtModule | `ot.integracion.manage` |
| 8 | PATCH | `/admin/ot/webhooks/{id}` | AdminOtEndpoints.cs:166 | OtModule | `ot.integracion.manage` |
| 9 | GET | `/admin/ot/api-logs` | AdminOtEndpoints.cs:175 | OtModule | `ot.integracion.manage` |
| 10 | GET | `/admin/ot/client-procedures` | AdminOtEndpoints.cs:182 | OtModule | `ot.tramites.manage` |
| 11 | GET | `/admin/ot/client-procedures/fields` | AdminOtEndpoints.cs:194 | OtModule | `ot.tramites.manage` |
| 12 | POST | `/admin/ot/client-procedures/search` | AdminOtEndpoints.cs:201 | OtModule | `ot.tramites.manage` |
| 13 | GET | `/admin/ot/client-procedures/health` | AdminOtEndpoints.cs:209 | OtModule | `ot.tramites.manage` |
| 14 | GET | `/admin/ot/client-procedures/counters` | AdminOtEndpoints.cs:216 | OtModule | `ot.tramites.manage` |
| 15 | POST | `/admin/ot/client-procedures/counters` | AdminOtEndpoints.cs:226 | OtModule | `ot.tramites.manage` |
| 16 | GET | `/admin/ot/client-procedures/{id}` | AdminOtEndpoints.cs:234 | OtModule | `ot.tramites.manage` |
| 17 | POST | `/admin/ot/client-procedures/{id}/approve` | AdminOtEndpoints.cs:242 | OtModule | `ot.tramites.manage` |
| 18 | POST | `/admin/ot/client-procedures/{id}/reject` | AdminOtEndpoints.cs:251 | OtModule | `ot.tramites.manage` |
| 19 | POST | `/admin/ot/client-procedures/{id}/revocation-requests/approve` | AdminOtEndpoints.cs:263 | OtModule | `ot.tramites.manage` |
| 20 | POST | `/admin/ot/client-procedures/{id}/revocation-requests/reject` | AdminOtEndpoints.cs:272 | OtModule | `ot.tramites.manage` |
| 21 | GET | `/admin/ot/client-procedures/{id}/revocation-requests/active` | AdminOtEndpoints.cs:284 | OtModule | `ot.tramites.manage` |
| 22 | GET | `/admin/ot/revocation-requests` | AdminOtEndpoints.cs:296 | OtModule | `ot.tramites.manage` |
| 23 | POST | `/admin/ot/client-procedures/{id}/consolidado` | AdminOtEndpoints.cs:304 | OtModule | `ot.tramites.manage` |
| 24 | GET | `/admin/ot/client-procedures/{id}/consolidado` | AdminOtEndpoints.cs:313 | OtModule | `ot.tramites.manage` |
| 25 | POST | `/admin/ot/client-procedures/{id}/consolidado-maestro` | AdminOtEndpoints.cs:321 | OtModule | `ot.tramites.manage` |
| 26 | GET | `/admin/ot/client-procedures/{id}/consolidado/entrega` | AdminOtEndpoints.cs:335 | OtModule | `ot.tramites.manage` |
| 27 | POST | `/admin/ot/consolidados/lotes` | AdminOtEndpoints.cs:348 | OtModule + RequirePermission | `consolidado-masivo.download` (existente) + `ot.tramites.manage` |
| 28 | GET | `/admin/ot/client-procedures/{id}/documents` | AdminOtEndpoints.cs:361 | OtModule | `ot.tramites.manage` |
| 29 | GET | `/admin/ot/client-procedures/{id}/documents/{attachmentId}/preview-url` | AdminOtEndpoints.cs:369 | OtModule | `ot.tramites.manage` |
| 30 | GET | `/admin/ot/client-procedures/{id}/documents/{attachmentId}/download` | AdminOtEndpoints.cs:377 | OtModule | `ot.tramites.manage` |
| 31 | POST | `/admin/ot/client-procedures/{id}/attachments` | AdminOtEndpoints.cs:385 | OtModule | `ot.tramites.manage` |
| 32 | POST | `/admin/ot/rules` | AdminOtEndpoints.cs:398 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 33 | GET | `/admin/ot/rules` | AdminOtEndpoints.cs:409 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 34 | PATCH | `/admin/ot/rules/{id}` | AdminOtEndpoints.cs:419 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 35 | GET | `/admin/ot/document-precedence` | AdminOtEndpoints.cs:435 | OtModule + OtAdminOrSuperAdmin | `ot.documentos.manage` |
| 36 | PATCH | `/admin/ot/document-precedence` | AdminOtEndpoints.cs:446 | OtModule + OtAdminOrSuperAdmin | `ot.documentos.manage` |
| 37 | POST | `/admin/ot/document-tags` | AdminOtEndpoints.cs:460 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 38 | GET | `/admin/ot/document-tags` | AdminOtEndpoints.cs:472 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 39 | DELETE | `/admin/ot/document-tags/{id}` | AdminOtEndpoints.cs:482 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 40 | POST | `/admin/ot/users/invite` | AdminOtEndpoints.cs:499 | OtModule | `ot.usuarios.manage` |
| 41 | GET | `/admin/ot/users` | AdminOtEndpoints.cs:512 | OtModule | `ot.usuarios.manage` |
| 42 | PATCH | `/admin/ot/users/{userId}` | AdminOtEndpoints.cs:524 | OtModule | `ot.usuarios.manage` |
| 43 | POST | `/admin/ot/users/{userId}/suspend` | AdminOtEndpoints.cs:538 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 44 | DELETE | `/admin/ot/users/{userId}/suspend` | AdminOtEndpoints.cs:549 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 45 | DELETE | `/admin/ot/users/{userId}` | AdminOtEndpoints.cs:562 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 46 | POST | `/admin/ot/invitations/{invitationId}/resend` | AdminOtEndpoints.cs:576 | OtModule | `ot.usuarios.manage` |
| 47 | DELETE | `/admin/ot/invitations/{invitationId}` | AdminOtEndpoints.cs:592 | OtModule | `ot.usuarios.manage` |
| 48 | POST | `/admin/ot/invitations/{invitationId}/reactivate` | AdminOtEndpoints.cs:606 | OtModule | `ot.usuarios.manage` |
| 49 | GET | `/admin/ot/imprint-signatures` | AdminOtEndpoints.cs:619 | OtModule | `ot.improntas.manage` |
| 50 | POST | `/admin/ot/imprint-signatures/{id}/validate` | AdminOtEndpoints.cs:629 | OtModule | `ot.improntas.manage` |
| 51 | GET | `/admin/ot/imprint-signatures/{id}/preview-url` | AdminOtEndpoints.cs:638 | OtModule | `ot.improntas.manage` |
| 52 | GET | `/admin/ot/imprint-signatures/{id}/validations` | AdminOtEndpoints.cs:648 | OtModule | `ot.improntas.manage` |
| 53 | GET | `/admin/ot/offices/{officeId}/mandatos` | AdminOtMandatosEndpoints.cs:28 | OtModule | `ot.mandatos.manage` |
| 54 | PATCH | `/admin/ot/offices/{officeId}/mandatos/default-signer` | AdminOtMandatosEndpoints.cs:29 | OtModule | `ot.mandatos.manage` |
| 55 | GET | `/admin/ot/offices/{officeId}/mandatos/preview` | AdminOtMandatosEndpoints.cs:30 | OtModule | `ot.mandatos.manage` |
| 56 | GET | `/admin/ot/offices/{officeId}/mandatos/company-rules` | AdminOtMandatosEndpoints.cs:31 | OtModule | `ot.mandatos.manage` |
| 57 | PATCH | `/admin/ot/offices/{officeId}/mandatos/company-rules/{companyTenantId}/default-signer` | AdminOtMandatosEndpoints.cs:32 | OtModule | `ot.mandatos.manage` |
| 58 | DELETE | `/admin/ot/offices/{officeId}/mandatos/company-rules/{companyTenantId}` | AdminOtMandatosEndpoints.cs:34 | OtModule | `ot.mandatos.manage` |
| 59 | GET | `/admin/ot/offices/{officeId}/mandatos/templates/{templateCode}/preview` | AdminOtMandatosEndpoints.cs:36 | OtModule | `ot.mandatos.manage` |
| 60 | GET | `/admin/ot/offices/{officeId}/mandatos/formatos` | AdminOtMandatosEndpoints.cs:39 | OtModule | `ot.mandatos.manage` |
| 61 | GET | `/admin/ot/metrics/operational` | AdminOtMetricsEndpoints.cs:31 | OtModule | `ot.reportes.manage` |
| 62 | GET | `/admin/ot/metrics/performance` | AdminOtMetricsEndpoints.cs:43 | OtModule | `ot.reportes.manage` |
| 63 | GET | `/admin/ot/metrics/rejection-reasons` | AdminOtMetricsEndpoints.cs:52 | OtModule | `ot.reportes.manage` |
| 64 | GET | `/admin/ot/metrics/drilldown` | AdminOtMetricsEndpoints.cs:64 | OtModule | `ot.reportes.manage` |
| 65 | GET | `/admin/ot/metrics/report` | AdminOtMetricsEndpoints.cs:76 | OtModule | `ot.reportes.manage` |
| 66 | GET | `/admin/ot/metrics/reviewers` | AdminOtMetricsEndpoints.cs:89 | OtModule | `ot.reportes.manage` |
| 67 | GET | `/admin/ot/metrics/reviewer-options` | AdminOtMetricsEndpoints.cs:102 | OtModule | `ot.reportes.manage` |
| 68 | GET | `/admin/ot/metrics/client-companies` | AdminOtMetricsEndpoints.cs:113 | OtModule | `ot.reportes.manage` |
| 69 | GET | `/admin/transit-offices/{transitOfficeId}/prenda-document-policies/` | AdminOtPrendaDocumentPolicyEndpoints.cs:28 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 70 | PUT | `/admin/transit-offices/{transitOfficeId}/prenda-document-policies/{tenantId}` | AdminOtPrendaDocumentPolicyEndpoints.cs:36 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 71 | GET | `/admin/ot/queries/fields` | AdminOtQueriesEndpoints.cs:33 | OtModule | `ot.reportes.manage` |
| 72 | POST | `/admin/ot/queries/run` | AdminOtQueriesEndpoints.cs:44 | OtModule | `ot.reportes.manage` |
| 73 | GET | `/admin/ot/queries/saved` | AdminOtQueriesEndpoints.cs:57 | OtModule | `ot.reportes.manage` |
| 74 | POST | `/admin/ot/queries/saved` | AdminOtQueriesEndpoints.cs:67 | OtModule | `ot.reportes.manage` |
| 75 | DELETE | `/admin/ot/queries/saved/{id}` | AdminOtQueriesEndpoints.cs:77 | OtModule | `ot.reportes.manage` |
| 76 | GET | `/admin/transit-offices/{transitOfficeId}/signature-vault` | AdminOtSignatureVaultEndpoints.cs:32 | OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 77 | POST | `/admin/transit-offices/{transitOfficeId}/signature-vault` | AdminOtSignatureVaultEndpoints.cs:40 | OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 78 | GET | `/admin/plate-ranges/` | AdminPlateRangesEndpoints.cs:34 | OtModule | (410 Gone: sin slug) |
| 79 | GET | `/admin/plate-ranges/plates` | AdminPlateRangesEndpoints.cs:38 | OtModule | (410 Gone: sin slug) |
| 80 | GET | `/admin/plate-ranges/eligible-companies` | AdminPlateRangesEndpoints.cs:42 | OtModule | (410 Gone: sin slug) |
| 81 | POST | `/admin/plate-ranges/` | AdminPlateRangesEndpoints.cs:46 | OtModule | (410 Gone: sin slug) |
| 82 | PUT | `/admin/plate-ranges/{rangeId}` | AdminPlateRangesEndpoints.cs:50 | OtModule | (410 Gone: sin slug) |
| 83 | POST | `/admin/plate-ranges/plates/{plateId}/block` | AdminPlateRangesEndpoints.cs:54 | OtModule | (410 Gone: sin slug) |
| 84 | POST | `/admin/plate-ranges/plates/{plateId}/unblock` | AdminPlateRangesEndpoints.cs:58 | OtModule | (410 Gone: sin slug) |
| 85 | POST | `/admin/plate-ranges/plates/{plateId}/revoke` | AdminPlateRangesEndpoints.cs:62 | OtModule | (410 Gone: sin slug) |
| 86 | POST | `/admin/plate-ranges/procedures/{instanceId}/assign-plate` | AdminPlateRangesEndpoints.cs:68 | OtModule | `ot.tramites.manage` |
| 87 | POST | `/admin/plate-ranges/procedures/{instanceId}/release-plate` | AdminPlateRangesEndpoints.cs:73 | OtModule | `ot.tramites.manage` |
| 88 | POST | `/admin/plate-ranges/procedures/{instanceId}/revoke` | AdminPlateRangesEndpoints.cs:75 | OtModule | `ot.tramites.manage` |
| 89 | POST | `/admin/plate-ranges/procedures/{instanceId}/update-plate` | AdminPlateRangesEndpoints.cs:79 | OtModule | `ot.tramites.manage` |
| 90 | GET | `/admin/transit-offices` | AdminTransitOfficesEndpoints.cs:31 | OtModule | (sin slug: base OtModule, ver 1.4) |
| 91 | GET | `/admin/transit-offices/operational-status` | AdminTransitOfficesEndpoints.cs:43 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 92 | PUT | `/admin/transit-offices/{id}/quipux-settings` | AdminTransitOfficesEndpoints.cs:57 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 93 | GET | `/admin/transit-offices/{id}/quipux-submissions` | AdminTransitOfficesEndpoints.cs:80 | OtModule | `ot.tramites.manage` |
| 94 | POST | `/admin/transit-offices/{id}/quipux-submissions/{submissionId}/retry` | AdminTransitOfficesEndpoints.cs:94 | OtModule | `ot.tramites.manage` |
| 95 | POST | `/admin/transit-offices/{id}/quipux-submissions/{submissionId}/cancel` | AdminTransitOfficesEndpoints.cs:109 | OtModule | `ot.tramites.manage` |
| 96 | GET | `/admin/document-requirement-overrides/` | AdminDocumentRequirementOverridesEndpoints.cs:36 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 97 | PUT | `/admin/document-requirement-overrides/` | AdminDocumentRequirementOverridesEndpoints.cs:49 | OtModule + SuperAdmin | SuperAdmin (se mantiene) |
| 98 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{id}/identity/send` | AdminMandateSignerIdentityEndpoints.cs:23 | OtModule | (410 Gone: sin slug) |
| 99 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{id}/identity/resend` | AdminMandateSignerIdentityEndpoints.cs:28 | OtModule | (410 Gone: sin slug) |
| 100 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{id}/identity/link` | AdminMandateSignerIdentityEndpoints.cs:33 | OtModule | (410 Gone: sin slug) |
| 101 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{id}/identity/mock` | AdminMandateSignerIdentityEndpoints.cs:38 | OtModule | (410 Gone: sin slug) |
| 102 | GET | `/admin/transit-offices/{transitOfficeId}/mandate-signers` | AdminMandateSignersEndpoints.cs:41 | OtModule | `ot.mandatos.manage` |
| 103 | GET | `/admin/transit-offices/{transitOfficeId}/mandate-signers/companies` | AdminMandateSignersEndpoints.cs:49 | OtModule | `ot.mandatos.manage` |
| 104 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers` | AdminMandateSignersEndpoints.cs:57 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 105 | PUT | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}` | AdminMandateSignersEndpoints.cs:69 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 106 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/inactivate` | AdminMandateSignersEndpoints.cs:81 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 107 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/reactivate` | AdminMandateSignersEndpoints.cs:92 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 108 | DELETE | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}` | AdminMandateSignersEndpoints.cs:104 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 109 | GET | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/impact` | AdminMandateSignersEndpoints.cs:116 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 110 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/identity-validation/reconcile` | AdminMandateSignersEndpoints.cs:129 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 111 | POST | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/identity-validation/resend` | AdminMandateSignersEndpoints.cs:140 | OtModule + OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 112 | GET | `/admin/transit-offices/{transitOfficeId}/mandate-signers/{mandateSignerId}/signature-image` | AdminMandateSignersEndpoints.cs:153 | OtModule | `ot.mandatos.manage` |
| 113 | GET | `/admin/transit-offices/{transitOfficeId}/mandate-signers/associable-companies` | AdminMandateSignersEndpoints.cs:164 | OtAdminOrSuperAdmin | `ot.mandatarios.manage` |
| 114 | GET | `/admin/ot/alert-rules/` | AdminOtAlertRulesEndpoints.cs:32 | OtModule | `ot.reportes.manage` |
| 115 | POST | `/admin/ot/alert-rules/` | AdminOtAlertRulesEndpoints.cs:40 | OtModule | `ot.reportes.manage` |
| 116 | PUT | `/admin/ot/alert-rules/{id}` | AdminOtAlertRulesEndpoints.cs:48 | OtModule | `ot.reportes.manage` |
| 117 | DELETE | `/admin/ot/alert-rules/{id}` | AdminOtAlertRulesEndpoints.cs:57 | OtModule | `ot.reportes.manage` |
| 118 | GET | `/admin/ot/alert-events/` | AdminOtAlertRulesEndpoints.cs:71 | OtModule | `ot.reportes.manage` |
| 119 | POST | `/admin/ot/alert-events/{id}/ack` | AdminOtAlertRulesEndpoints.cs:79 | OtModule | `ot.reportes.manage` |
| 120 | GET | `/admin/ot/report-schedules/` | AdminOtReportSchedulesEndpoints.cs:41 | OtModule | `ot.reportes.manage` |
| 121 | POST | `/admin/ot/report-schedules/` | AdminOtReportSchedulesEndpoints.cs:49 | OtModule | `ot.reportes.manage` |
| 122 | PUT | `/admin/ot/report-schedules/{id}` | AdminOtReportSchedulesEndpoints.cs:57 | OtModule | `ot.reportes.manage` |
| 123 | DELETE | `/admin/ot/report-schedules/{id}` | AdminOtReportSchedulesEndpoints.cs:66 | OtModule | `ot.reportes.manage` |

### Rutas `SuperAdmin` puras del mismo archivo (fuera del alcance de las políticas OT; no cuentan en las 123)

`AdminMandateSignersEndpoints.cs:178, 190, 202, 219, 226` (`/admin/mandate-signers/*`: informes y retiro de asociaciones).

### Conteos

| Concepto | Total |
|----------|-------|
| Rutas inventariadas bajo `OtModule` / `OtAdminOrSuperAdmin` / `OtAdmin` | **123** |
| Política efectiva `OtModule` sola | 91 (12 de ellas `410 Gone`) |
| `OtModule` + `SuperAdmin` | 19 |
| `OtModule` + `OtAdminOrSuperAdmin` | 10 |
| `OtAdminOrSuperAdmin` sola (baúl de firmas ×2, asociables ×1) | 3 |
| `OtAdmin` (policy `"OtAdmin"`) | **0 usos** (solo definición: `AdminAuthorization.cs:38`, `ApiSecurityExtensions.cs:116`) |
| Con slug OT | **90** = `ot.tramites.manage` 29 + `ot.reportes.manage` 23 + `ot.mandatos.manage` 11 + `ot.mandatarios.manage` 11 + `ot.usuarios.manage` 6 + `ot.improntas.manage` 4 + `ot.integracion.manage` 4 + `ot.documentos.manage` 2 |
| Sin slug | 33 = SuperAdmin 19 + `410 Gone` 12 + base 2 |

90 + 33 = 123.

### Cómo se verificó (reproducible)

```bash
cd "C:/FLIT 2.0/wt-permisos"
# 1) Usos de las políticas en código de producción (grupo + ruta): 25 en 13 archivos
grep -rnE "RequireAuthorization\(AdminAuthorization\.(OtModulePolicy|OtAdminOrSuperAdminPolicy|OtAdminPolicy)\)" services --include=*.cs | grep -vE "/(tests|obj|bin)/"
# 2) OtAdmin sin uso y ningún otro servicio
grep -rnE "OtAdminPolicy|\"OtAdmin\"" services --include=*.cs | grep -vE "/(obj|bin)/"
# 3) Todos los MapGroup y Map* de los 13 archivos
grep -nE "MapGroup\(|\.Map(Get|Post|Put|Patch|Delete)\(|RequireAuthorization|RequirePermission" \
  services/core-api/src/Flit.Api/Endpoints/{AdminOtEndpoints,AdminOtMandatosEndpoints,AdminOtMetricsEndpoints,AdminOtPrendaDocumentPolicyEndpoints,AdminOtQueriesEndpoints,AdminOtSignatureVaultEndpoints,AdminPlateRangesEndpoints,AdminTransitOfficesEndpoints,AdminDocumentRequirementOverridesEndpoints,AdminMandateSignerIdentityEndpoints,AdminMandateSignersEndpoints}.cs \
  services/core-api/src/Flit.Api/Endpoints/Analytics/{AdminOtAlertRulesEndpoints,AdminOtReportSchedulesEndpoints}.cs
```

El paso 3 se resolvió con un script de Node (no versionado) que une cada sentencia `MapX(...)` con el `MapGroup` de su variable (prefijo y política del grupo) más las `RequireAuthorization`/`RequirePermission` encadenadas; arrojó 128 filas (123 bajo políticas OT + 5 `SuperAdmin` puras). Los tres grupos en línea de `AdminMandateSignersEndpoints.cs` (`:164`, `:178-226`) se leyeron a mano. Cualquier ruta nueva en esos archivos cambia el conteo: el test de arquitectura de FA-6 debe cubrirlo.

Superficie adyacente que **no** usa estas políticas pero toca al Admin OT: `UserAdminPolicy` (SuperAdmin, AdminCompany, ot_admin) en `SecurityEndpoints.cs:606` y `:744` (asignación de roles/usuarios). FA-4 no debe tocarla sin una decisión aparte.

---

## Parte 3 — Uso actual de slugs en el frontend

`frontend-hub/` existe. `frontend-comparendos/` y `frontend-diagnostico/` no usan `usePermissions`/`hasPermission`. Los tests (`__tests__`) se omiten.

### 3.1 Infraestructura

| Pieza | Archivo:línea | Nota |
|-------|---------------|------|
| `usePermissions()` | `frontend/hooks/usePermissions.ts:31` | Lee `payload.permissions` del JWT (`:56`); no pasa a minúscula |
| `PermissionGate` | `frontend/components/auth/PermissionGate.tsx:18` | **Sin ningún uso** |
| `hasPermission(payload, slug)` | `frontend/lib/auth/jwt.ts:126` | Case-insensitive |
| Filtro de `NavItem.permission` | `packages/shell/src/nav.ts:74-76` | Compartido con `frontend-hub`; `some` sobre la lista |
| Permisos en el shell | `frontend/components/atom/Shell.tsx:66-67, 143` | Los pasa en minúscula |

### 3.2 Slug → uso

| Slug | Archivo:línea | Qué oculta / habilita |
|------|---------------|-----------------------|
| `logqx.read` | `jwt.ts:119, 155`; `LogQxLink.tsx:23-24`; `tramitesNav.ts:201`; `app/page.tsx:55`; `useNavigableModules.ts:62` | Enlace Log QX y entrada de dock |
| `ict.logs.read`, `ict.trazabilidad.read`, `ict.reportes.read` | `jwt.ts:162-168, 180, 185`; `tramitesNav.ts:212-213`; `app/page.tsx:57-59`; `useNavigableModules.ts:63-65`; `lib/nav/modules.ts:110-111` | Módulos ICT en dock y SPA |
| `ict.clients.manage` | `jwt.ts:189, 193`; `Usuarios.tsx:72, 110, 433` | Pestaña "Clientes ICT" |
| `generacion-documental.read` | `jwt.ts:197, 206`; `guard.ts:128` | Ruta `/admin/generacion-documental` y su entrada de dock |
| `consolidado-masivo.download` | `jwt.ts:210, 218` | Helper `canDescargarConsolidadosMasivo` (descarga masiva) |
| `runt_confirmation.settings.manage`, `runt_confirmation.history.read` | `jwt.ts:222, 225, 232, 240, 249`; `guard.ts:138`; `tramitesNav.ts:147`; `AdminTramiteAcciones.tsx:762` (vía `admin-tramite-permissions.ts:18`) | Confirmación RUNT y acción "Consultar ahora en RUNT" |
| `security.users.reset_password` | `jwt.ts:262, 272` | Helper `canAdminResetPassword`; la UI de `Usuarios.tsx:74` decide por rol, no por slug |
| `AdminTramiteCambiarEstado`, `AdminTramiteAnular`, `AdminTramiteLimpiarConsolidado`, `AdminTramiteCargarConsolidado`, `AdminTramiteReenviarValidacion`, `AdminTramiteReasignarGestor` | `admin-tramite-permissions.ts:9-14`; `AdminTramiteAcciones.tsx:667-674, 708-753`; `Validaciones.tsx:454` | Acciones del menú admin del trámite |
| `reportes.{resumen,operacion,detallados,ot,uso,productividad,consultas}.read`, `reportes.read` (legacy) | `Reportes.tsx:41-53, 107-121` | Pestañas de Reportes |
| `reportes.programacion.manage` | `Reportes.tsx:56, 183` | Panel de programación/alertas |
| `reportes.detallados.export` | `DetailedReportPanel.tsx:38-39` | Botón exportar |
| `usuarios.manage` | `frontend-hub/components/HubShell.tsx:21` | Entrada "Usuarios y roles" del hub |
| `banners.manage` | No se evalúa en frontend: decide el rol (`canManageBanners` en `jwt.ts`; `guard.ts`) | Banners exclusivo SuperAdmin |

No hay **ningún** uso de slugs `ot.*`, `improntas.*`, `dashboard.read` ni `tramites.read/create` en el frontend (solo en backend/seeder).

### 3.3 Módulos OT que hoy se muestran por rol / `entity_type`, no por slug

| Módulo OT | Dónde se decide | Criterio actual | Slug que lo sustituiría |
|-----------|-----------------|-----------------|-------------------------|
| Acceso a `/admin/transit-offices/*` | `lib/auth/guard.ts:107-109`; `app/admin/transit-offices/page.tsx:42` | `isOtUser` = rol `ot_admin` o `entity_type=TRANSIT_OFFICE` (`jwt.ts:337-338`) | Base `OtModule`; subrutas por slug de pestaña |
| Documentos (solo ot_admin) | `guard.ts:35, 109` (`isOtAdmin`); `tramitesNav.ts:183` | Rol | `ot.documentos.manage` |
| Usuarios (solo ot_admin) | `tramitesNav.ts:183` | Rol (`isOtAdmin`) | `ot.usuarios.manage` |
| Trámites, Reportes, Mandatos, Validar impronta en el dock OT | `tramitesNav.ts:174-190` (`ctx.isOtUser`) | Pertenecer al tenant OT | `ot.tramites.manage`, `ot.reportes.manage`, `ot.mandatos.manage`, `ot.improntas.manage` |
| Registrar mandatario | `OtMandatosSection.tsx:135-139` | `isSuperAdmin \|\| isOtAdmin` | `ot.mandatarios.manage` |
| Reglas / Requisitos / Configuración | `guard.ts:27` | Solo SuperAdmin | Sin cambio |
| Ocultar SPA genéricas a usuarios OT | `lib/nav/modules.ts:40-44, 94`; `tramitesNav.ts:110` (`OT_ADMIN_SPA_OMIT`) | `isOtUser` | Sin cambio (vía `accessibleCodes`) |
| Dashboard del organismo | `app/page.tsx:54, 72, 178-179` | `isOtUser` (la variable se llama `isOtAdminUser` pero incluye a todo usuario OT) | Base `OtModule` |
| Flags `isOtUser`/`isOtAdmin` del shell | `Shell.tsx:73-75, 123-129`; `useNavigableModules.ts:61` | Claims | — |
| Contexto de Dr. FLIT | `components/dr-flit/dr-flit-context.ts:49` | `isOtUser` | — |
| Barra de pestañas del hub | `OtHubLayout.tsx:66-69` | Solo SuperAdmin la ve; el OT usa el dock | — |
| Webhooks y Revocatorias | `ot-nav.ts:22-49` | Sin entrada de dock para OT; solo por URL / SuperAdmin | `ot.integracion.manage`; revocatorias en `ot.tramites.manage` |

Consecuencia para FC: la UI del usuario OT depende de `isOtUser`/`isOtAdmin`, así que FC-1/FC-2 deben reemplazar esos criterios por `permissions.includes(slug)` (o por `accessibleCodes` de los módulos nuevos) en `tramitesNav.ts`, `guard.ts`, `OtMandatosSection.tsx` y `modules.ts`, y dejar `isOtUser` solo como filtro de contexto (qué organismo, qué dashboard).

---

## Parte 4 — Riesgos y preguntas para aprobar

**P1. ¿Endurecer usuarios OT y webhooks/api-logs para los roles no-admin?**
Hoy cualquier usuario de un tenant OT puede invitar/editar usuarios y gestionar webhooks por API (la UI se lo oculta). Dar `ot.usuarios.manage` y `ot.integracion.manage` solo a `ot_admin` cierra el hueco pero cambia el comportamiento de la API.
*Recomendación:* sí, endurecer (solo ot_admin). Es consistente con la UI y con la intención de HU #12859/#13123. Si alguna integración de terceros llama estas rutas con un rol no-admin, se detecta con la bitácora de API antes de FA-4.

**P2. Sesiones vivas y orden de despliegue.**
Los permisos están en el JWT de 12 h. Si FA-4 sale junto con FA-3, los usuarios OT con sesión abierta reciben 403 en cada pestaña hasta renovar el token.
*Recomendación:* desplegar FA-3 (grants) primero y activar FA-4 al menos 12 h después, o detrás de un flag de configuración (apagado por defecto). Coordinar con C-2 de la Épica (la renovación de token no existe hoy).

**P3. Usuarios OT sin rol activo.**
Pasan hoy por `entity_type`; con FA-4 pierden todas las pestañas (conservan perfil/catálogo).
*Recomendación:* aceptar (es lo correcto bajo permisos granulares) y contar cuántos hay por ambiente antes del corte para avisar al Admin OT.

**P4. Granularidad: un slug por pestaña o separar lectura/escritura en trámites y reportes.**
Hoy no hay diferencia de política, así que propongo un solo `*.manage`. Un usuario "solo consulta" no sería posible hasta separar.
*Recomendación:* un slug por pestaña ahora. Si el PO exige "solo lectura", añadir `ot.tramites.read`/`ot.reportes.read` después, concediéndolos junto al `manage` a los mismos roles (sin regresión).

**P5. Siete módulos RBAC nuevos o un único módulo `ot`.**
El módulo es la unidad que el administrador activa/desactiva por usuario (O-1 de la épica) y la que usa `accessibleCodes`.
*Recomendación:* siete módulos (coinciden con pestañas y con la lógica menú/URL de FC-2). Con un único módulo `ot` la lista de slugs no cambia, pero se pierde el apagado por pestaña a nivel de módulo.

---

## Anexo — Checklist para FA-2..FA-4 una vez aprobado

- FA-2: DDL idempotente (número ≥ 137; el último es `136-HU13604`) que crea los 7 módulos con `product_code='tramites'` y las 8 acciones; el seeder replica solo para bases nuevas; tests de siembra al estilo de `ConsolidadoMasivoPermissionSeedTests`.
- FA-3: DDL de backfill según la tabla 1.3, sin enumerar códigos de rol.
- FA-4: `RequirePermission` por ruta (patrón `AdminOtEndpoints.cs:348-349`); sustituir `OtAdminOrSuperAdmin` por slug en las 13 rutas de mandatarios/baúl/asociables/prelación; actualizar `ConsolidadoLoteOtPermisoArchitectureTests` y los tests `AdminOtFeatureB/C*Authorization*`.
- FA-6 (arquitectura): el test debe reconocer `RequirePermission` y los 8 slugs `ot.*` como `enforced = true`.
