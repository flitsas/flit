# Manual local — Descarga masiva de consolidados (épica #13216)

> Generado: 2026-10-08 · rama `feature/AB-13216-descarga-masiva-consolidados` (PR #572).
> Las referencias `archivo:línea` corresponden a esa rama. Si el código cambia, verifícalas antes de citarlas.

Este manual cubre cuatro cosas para tu máquina:

1. Levantar el motor de lotes.
2. Configurarlo.
3. Probar cada superficie a mano y con los tests automáticos.
4. Lo que hay que tener en cuenta antes de desplegar.

---

## 1. Qué hace la funcionalidad

El usuario marca trámites (casillas, «Seleccionar todos» o un filtro) y pulsa **Descargar ZIP**. El backend crea un **lote**, procesa cada trámite en segundo plano, arma una o varias **partes ZIP cifradas** y las deja disponibles **24 h**. El aviso global de la app muestra el progreso y los enlaces de descarga.

| Superficie | Quién | Documento | Ruta de alta |
|---|---|---|---|
| `/tramites` | Radicador / admin_tramites (Gestor) | `consolidado` | `POST /api/v1/tramites/consolidados/lotes` |
| `/tramites` como Super Admin (varias compañías) | SuperAdmin | `consolidado` o `consolidado_maestro` | la misma, con origen `superadmin` (opcional `X-Tenant-Id`) |
| Bandeja OT (`ClientProceduresSection`) | ot_admin | `consolidado_maestro` | `POST /api/v1/admin/ot/consolidados/lotes` |
| Vista de red (cabeza de red) | AdminCompany de la cabeza | `consolidado` | la de `/tramites` con `alcanceRed: "red" \| "<uuid hija>"` |
| Parámetros del motor | SuperAdmin | — | `GET/PUT /api/v1/admin/plataforma/consolidados/lotes/parametros` |

**Rutas comunes a todos los orígenes** (filtran por dueño, es decir, por el `sub` del token, e ignoran `X-Tenant-Id`):

- `GET /api/v1/consolidados/lotes/actual`: lote activo o último retenido, para el polling del aviso.
- `GET /api/v1/consolidados/lotes/{loteId}`
- `GET /api/v1/consolidados/lotes/{loteId}/partes/{n}`: descarga en streaming; nunca genera PDF.
- `POST /api/v1/consolidados/lotes/{loteId}/cancelacion`

### Reglas de negocio que no se negocian

- **El lote nunca regenera.** Si el trámite ya tiene consolidado, se entrega ese (`delivery_mode = existente`). Si no lo tiene, se genera **por primera vez** (`generado`) con `force:false`, `userId:null` y `soloSiNoExiste:true` (ADR-0070, nota v2). Nunca se sobrescribe un consolidado existente.
- **El lote nunca escribe en trámites de una compañía hija** (vista de red). Si la hija no tiene consolidado, el ítem se omite como `red_sin_consolidado`.
- **Un lote activo por usuario.** Si ya tiene uno, el alta responde 409 `lote_activo`.
- **Tope total**: `max_items_per_batch`, 10.000 por defecto, configurable entre 1 y 32.766. Por encima responde 422 `seleccion_excede_tope` con `total` y `tope`.
- **ZIP plano**: los PDF quedan en la raíz del ZIP con nombre `{radicado|SIN-RADICADO}_{placa|SIN-PLACA}.pdf`, y lo que no se pudo incluir va en `omitidos.csv` (`ConsolidadoLoteNombres.cs:29-58`).
- **Nombres de las partes:** una sola parte se llama `consolidados_yyyyMMdd_HHmm.zip` (hora de Colombia). Si hay varias, `consolidados_{marca}_parte-KK-de-TT.zip`.

---

## 2. Requisitos previos

1. Rama: `git checkout feature/AB-13216-descarga-masiva-consolidados`.
2. Infraestructura local: `pnpm docker:up:infra` (postgres, redis, rabbitmq, minio y mailhog; `package.json:89`).
3. `services/core-api/src/Flit.Api/appsettings.Development.json` con `ConnectionStrings:Core` apuntando a `flit_local` (`Host=localhost;Port=5432;Database=flit_local`).
4. Dependencias del frontend: `pnpm install --frozen-lockfile`.
5. SDK .NET 10.0.300 o superior (`services/core-api/global.json`, `rollForward: latestFeature`).

> **ImageSharp / NuGetAudit:** la rama suprime las 5 alertas de SixLabors.ImageSharp 2.1.11 en `Directory.Packages.props`, así que `dotnet restore` limpio ya no falla en local. Si ves `NU1902`/`NU1903`, estás en una rama sin esa supresión. Si ves `NU1508` («Duplicate NuGetAuditSuppress»), es el problema del CI que está revisando el LT (ver §9).

---

## 3. Configuración del backend

### 3.1 Migraciones

Las dos migraciones de la épica están **Pending** en todos los ambientes:

- `20261008031329_HU13367_ConsolidadoExportBatches`: DDL 133, que crea `consolidado_export_settings` y `consolidado_export_batches`.
- `20261008031425_HU13368_ConsolidadoExportItems`: DDL 134, que crea `consolidado_export_batch_parts`, `consolidado_export_batch_items` y `consolidado_export_audit`.

Hay dos formas de aplicarlas:

- **Automática:** `Flit.Api` aplica las migraciones pendientes al arrancar, salvo que `Database:AutoMigrate=false` (`src/Flit.Api/Program.cs:189-201`).
- **Manual:** `pnpm migrate:core-api` (`package.json:83`), que es `dotnet ef database update --project services/core-api/src/Flit.Infrastructure --startup-project services/core-api/src/Flit.Api`.

Para comprobarlo, desde `services/core-api`:

```bash
dotnet ef migrations list --project src/Flit.Infrastructure/Flit.Infrastructure.csproj \
  --startup-project src/Flit.Infrastructure/Flit.Infrastructure.csproj
```

Tras aplicarlas, las dos migraciones dejan de salir como `(Pending)`.

> ⚠️ Los DDL usan `CREATE TABLE IF NOT EXISTS`. Si en tu `flit_local` aplicaste una **versión anterior** de estas migraciones desde un worktree de la épica, las tablas se quedan con los CHECK viejos. Por ejemplo, sin el tope 1–32.766 ni los topes de tiempos (Security L1). Solución en local: `dotnet ef database update <migración anterior a HU13367>`, borrar las tablas `tramites.consolidado_export_*` si quedaron y volver a aplicar. **Nunca hagas esto en DEV/QA/PDN.**

### 3.2 Directorio temporal

- Clave única: `ConsolidadoLotes:DirectorioTemporal` (`InfrastructureExtensions.cs:337-344`).
- Si está vacía, usa `{TMPDIR}/flit-consolidado-lotes`. En Windows es `%TEMP%\flit-consolidado-lotes`.
- Al arrancar, `ConsolidadoLoteTemporalesLimpieza` borra los temporales huérfanos.
- Necesitas al menos **2 × `max_mb_per_part`** libres: con 250 MB, unos 600 MB. Si subes `max_mb_per_part` a 2048, necesitas unos 4,5 GB (runbook §core-api).
- En producción se fija con `ConsolidadoLotes__DirectorioTemporal=/var/tmp/flit-lotes` sobre el volumen `core-api-lotes-tmp` (`docker-compose.prod.yml`).

### 3.3 Parámetros del motor (tabla `tramites.consolidado_export_settings`)

Es una fila global, sin tenant, que se siembra con los valores por defecto (DDL 133:27-87).

| Columna | Default | Rango |
|---|---|---|
| `max_items_per_batch` | 10000 | 1–32766 |
| `max_pdfs_per_part` | 500 | 1–5000 |
| `max_mb_per_part` | 250 | 10–2048 |
| `item_slots` (carriles en paralelo) | 2 | 1–6 |
| `item_timeout_seconds` | 300 | 1–3600 |
| `item_lease_seconds` | 600 | > timeout y ≤ 7200 |
| `max_item_attempts` | 3 | 1–10 |
| `retry_delay_seconds` | 30 | 5–3600 |
| `part_timeout_seconds` | 1200 | 1–7200 |
| `part_lease_seconds` | 1800 | > timeout y ≤ 14400 |
| `max_part_attempts` | 3 | 1–10 |
| `retention_hours` | 24 | 1–168 |
| `is_active` | **true** | — |

**Cómo editarlos:**

- **Pantalla:** inicia sesión como SuperAdmin y entra en *Plataforma → Descarga masiva* (`/admin/plataforma/descarga-masiva`). La pantalla valida los rangos que manda el servidor en `limites`, y si otro admin guardó antes responde 409 con «Recargar sin perder lo escrito».
- **SQL** (solo en local), por ejemplo para forzar partes pequeñas al probar la partición:

  ```sql
  UPDATE tramites.consolidado_export_settings
     SET max_pdfs_per_part = 2, max_mb_per_part = 10;
  ```

El motor lee la fila en cada ciclo: **no hace falta reiniciar**.

### 3.4 Procesos en segundo plano (`InfrastructureExtensions.cs:717-726`)

Se registran siempre, sin condición de entorno:

| Hosted service | Ciclo | Qué hace |
|---|---|---|
| `ConsolidadoLoteProcessor` | 5 s / 10 s | Reclama ítems y partes con lease; genera o entrega PDF; empaqueta y cifra |
| `ConsolidadoLotePurgaProcessor` | 10 min | Purga lotes expirados y fallidos (borrado criptográfico) |
| `ConsolidadoLoteTemporalesLimpieza` | al arrancar | Borra temporales huérfanos |

- **Apagar el motor:** `is_active = false`, desde la pantalla o por SQL. El alta responde 503 `motor_inactivo` y los lotes en curso quedan en pausa.
- **La purga corre aunque el motor esté apagado.**
- **Sin fila de parámetros** el motor no reclama nada y la pantalla responde 404 `parametros_no_encontrados`.

### 3.5 Cifrado de las partes

- Formato `FLZ1`, AES-256-GCM por bloques (`src/Flit.Infrastructure/Security/ConsolidadoLoteCipher.cs:9-31`).
- La clave del lote (DEK, 32 bytes) va envuelta con Data Protection (propósito `Flit.Tramites.ConsolidadoLote.Dek.v1`), y cada parte usa su propia subclave HKDF-SHA256.
- El keyring de Data Protection vive en la BD (`PersistKeysToDbContext`). No requiere configuración, pero **si borras el keyring de tu `flit_local`, las partes ya generadas dejan de poder descifrarse**: crea un lote nuevo.
- **Purgar = destruir la DEK.** El archivo puede seguir en disco, pero es ilegible.

---

## 4. Usuarios, permisos y datos de prueba

### 4.1 Permiso

- `consolidado-masivo.download` lo siembra `DevelopmentAuthSeeder` (`DevelopmentAuthSeeder.cs:1518-1570`) con grant directo a **SuperAdmin, admin_tramites, Radicador y ot_admin**. Los demás roles del OT lo heredan por RBAC.
- El SuperAdmin tiene bypass aunque su token no traiga el claim.
- La bandeja OT exige **además** `OtModulePolicy` (`AdminOtEndpoints.ConsolidadoLotes.cs:20`).
- El aviso global solo se monta si `currentUser.puedeDescargaMasiva` (`frontend/components/atom/Shell.tsx:160`).

> Si cambiaste permisos a mano en `flit_local` y no ves el botón, cierra sesión y vuelve a entrar para que el token traiga el claim.

### 4.2 Trámites para probar

Con trámites del mismo tipo puedes cubrir todos los caminos del lote:

| Caso | Cómo prepararlo | Resultado esperado en el ZIP |
|---|---|---|
| Con consolidado ya generado | Abre el trámite y genera su consolidado desde el detalle | PDF incluido (`existente`); el trámite no cambia |
| Sin consolidado, con FUR | Trámite de matrícula inicial o traspaso con FUR y adjuntos | PDF generado por primera vez (`generado`) |
| Sin FUR | Trámite sin FUR | En `omitidos.csv`: `fur_requerido` |
| Modalidad no soportada | Trámite de un tipo distinto de matrícula inicial o traspaso | `modalidad_no_soportada` |
| Migrado sin consolidado | Trámite migrado de v1 | `migrado_solo_lectura` |
| Adjunto caído | Borra en MinIO un adjunto del expediente | `adjunto_no_disponible` |
| Organismo inactivo | OT del trámite inactivo | `organismo_requerido` |

Los 11 códigos de omisión, con su texto exacto, están en `ConsolidadoErrorTextos.cs:29-45`:

- `fur_requerido`
- `migrado_solo_lectura`
- `sin_adjuntos`
- `adjunto_no_disponible`
- `mimetype_no_soportado`
- `organismo_requerido`
- `modalidad_no_soportada`
- `quipux_solo_lectura`
- `acceso_revocado`
- `error_tecnico`
- `red_sin_consolidado`

> **Rechazado:** no es estado final para el lote. Si no tiene FUR, se omite como `fur_requerido` (decisión del responsable).

### 4.3 Jerarquía de red (solo para probar la vista de red)

Necesitas:

- una **cabeza** (`identity.tenants.is_group_parent = true`, tipo `CONCESION` o `MARCA_BLANCA`);
- al menos una **hija** con `parent_tenant_id = <cabeza>`;
- un usuario con rol **AdminCompany activo en la cabeza** (leído de la BD, no del JWT).

Lo recomendado es configurarlo desde las pantallas de compañías del Super Admin (jerarquía y concesión), para que se disparen las auditorías.

**Interruptores** (`identity.hierarchy_switches`, columnas `switch_key` / `is_enabled`). Se editan como SuperAdmin con `GET /api/v1/admin/platform/hierarchy-switches` y `PUT /api/v1/admin/platform/hierarchy-switches/{key}`.

| Clave | Para qué |
|---|---|
| `group_read_scope` | Habilita la vista de red. Si está apagada, `/network/**` responde 403 |
| `network_documents_concesion` | Una cabeza **CONCESIÓN** solo puede leer o descargar documentos de sus hijas si está encendida. **Apagada por defecto** según el comentario de la entidad. MARCA_BLANCA no la consulta |

---

## 5. Levantar y probar a mano

```bash
pnpm docker:up:infra
pnpm dev          # frontend, core-api :4003, gateway :4002, core-ict :4020
```

En local, el frontend reenvía `/api/v1/*` a `CORE_API_ORIGIN` (`http://localhost:4003` por defecto) con un `proxyTimeout` de 120 s (`frontend/next.config.ts`). En local **no hay nginx**.

### 5.1 Escenarios

| # | Escenario | Pasos | Esperado |
|---|---|---|---|
| 1 | Lote con casillas | `/tramites` como Radicador: marca 3 trámites → *Descargar ZIP* → confirma | 202; aparece el aviso global; al terminar, enlace(s) de parte; ZIP con PDF y `omitidos.csv` si aplica |
| 2 | «Seleccionar todos» con filtro | Filtra la tabla → *Seleccionar todos* → desmarca 1 → confirma | Modo `filtro` con `excluidos`; el total del lote coincide con el contador |
| 3 | Partición | Pon `max_pdfs_per_part = 2` (§3.3) y lanza 5 trámites con PDF | 3 partes `parte-01-de-03`… |
| 4 | Tope | `max_items_per_batch = 3` y selecciona 4 | 422 `seleccion_excede_tope`; el modal muestra «total 4 / tope 3» |
| 5 | Lote activo | Lanza un lote y, antes de que termine, otro | 409 `lote_activo`; el aviso recupera el lote en curso |
| 6 | Cancelación | Durante el proceso → *Cancelar* en el aviso | 202; ítems vivos `cancelado`, partes sin cerrar `descartada`; las partes ya cerradas dan **410**; el aviso muestra el cancelado hasta que crees otro |
| 7 | Motor apagado | `is_active = false` → intenta crear | 503 `motor_inactivo`; los lotes en curso se pausan; al encender, continúan |
| 8 | Super Admin | Como SuperAdmin en `/tramites` con trámites de 2 compañías | Lote sin compañía (`tenant_id` NULL), 2 partes planas; solo el SuperAdmin que lo creó lo descarga (otro SA recibe 404) |
| 9 | Bandeja OT | Como ot_admin en la bandeja del OT → *Descargar maestros* | `consolidado_maestro`; el `filter_summary` de la auditoría solo guarda estado, familia y orden de catálogo |
| 10 | Vista de red, toda la red | Como AdminCompany de la cabeza, activa la vista de red → marca un trámite propio y uno de una hija → ZIP | Cuerpo con `alcanceRed: "red"` en la raíz; el ítem de la hija con consolidado entra como `existente`; si no tiene consolidado, `red_sin_consolidado` |
| 11 | Vista de red acotada | Elige una hija en el selector → *Seleccionar todos* | `alcanceRed: "<uuid hija>"`; aviso «Red · nombre de la hija», que se mantiene al recargar |
| 12 | Documentos de red apagados | Cabeza CONCESIÓN con `network_documents_concesion = false` | En la vista de red no se ofrece *Descargar ZIP* desde el primer render; con «Mi compañía» sí |
| 13 | Revocación a mitad | Lote de red grande; mientras procesa, quita la hija de la red (o el rol AdminCompany, o apaga `group_read_scope`) | En ≤ 60 s, los ítems pendientes de esa hija pasan a `acceso_revocado`; los de la cabeza siguen |
| 14 | Parámetros | Pantalla de parámetros: valor fuera de rango; lease ≤ timeout; dos pestañas guardando | Error junto al campo sin llamar al PUT; 409 en la segunda pestaña |
| 15 | Retención | `retention_hours = 1`, espera más de 1 h, o adelanta `expires_at` por SQL | Tras la siguiente purga (≤ 10 min) la parte da 410/404 y la auditoría tiene `lote_purgado` |

### 5.2 Consultas útiles (local)

```sql
-- Último lote y su estado
SELECT id, origin, status, document_type, selection_mode, network_scope, total_items,
       included_count, omitted_count, parts_count, created_at, expires_at, purged_at
  FROM tramites.consolidado_export_batches ORDER BY created_at DESC LIMIT 5;

-- Ítems del lote
SELECT status, delivery_mode, omission_code, attempts, count(*)
  FROM tramites.consolidado_export_batch_items WHERE batch_id = '<id>'
 GROUP BY 1,2,3,4;

-- Partes
SELECT part_number, status, pdf_count, omitted_count, stored_size_bytes, closed_at, purged_at
  FROM tramites.consolidado_export_batch_parts WHERE batch_id = '<id>' ORDER BY part_number;

-- Auditoría (lote_creado, lote_finalizado, parte_descargada, lote_cancelado, lote_purgado)
SELECT event, origin, part_number, parts_count, included_count, omitted_count, occurred_at
  FROM tramites.consolidado_export_audit WHERE batch_id = '<id>' ORDER BY occurred_at;
```

> Columnas verificadas en los DDL 133/134 el 2026-10-08. El lote también tiene `included_count`, `omitted_count`, `generated_count`, `parts_count`, `network_scope`, `scope_tenant_id` y `error_code`.

---

## 6. Pruebas automatizadas

Reglas de la épica: **compila una vez** por proyecto, corre **por filtro** y PostgreSQL solo cuando aplique. Todo desde `services/core-api`.

```bash
# Compilar una vez
dotnet build Flit.slnx

# Application: motor, handlers, red, parámetros
dotnet test tests/Flit.Tramites.Application.Tests/Flit.Tramites.Application.Tests.csproj --no-build \
  --filter "FullyQualifiedName~ConsolidadoLotes|FullyQualifiedName~ConsolidadoErrorTextosTests"

# Infrastructure: repositorios, checker, cifrado, empaquetado, imágenes, paridad DDL
dotnet test tests/Flit.Infrastructure.Tests/Flit.Infrastructure.Tests.csproj --no-build \
  --filter "FullyQualifiedName~ConsolidadoLote|FullyQualifiedName~ConsolidadoExport|FullyQualifiedName~OtBandeja|FullyQualifiedName~PngJpegImageDecoding"

# Admin: endpoints, contratos OpenAPI, puertas de red, parámetros
dotnet test tests/Flit.Admin.Tests/Flit.Admin.Tests.csproj --no-build \
  --filter "FullyQualifiedName~ConsolidadoLote|FullyQualifiedName~CrearLoteSuperAdmin|FullyQualifiedName~ParametrosMotorLote|FullyQualifiedName~NetworkDocumentos|FullyQualifiedName~NetworkRoleGate"

# PostgreSQL real (por clase)
CI=true dotnet test tests/Flit.Integration.Tests/Flit.Integration.Tests.csproj --no-build \
  --filter "FullyQualifiedName~CrearLoteConsolidadosIntegrationTests|FullyQualifiedName~CrearLoteRedIntegrationTests|FullyQualifiedName~ConsolidadoLoteAccessCheckerRedIntegrationTests|FullyQualifiedName~ParametrosMotorLoteIntegrationTests|FullyQualifiedName~ConsolidadoExportSchemaMigrationTests"
```

**PostgreSQL para `Flit.Integration.Tests`** (`tests/Flit.Integration.Tests/Postgres/PostgresAvailability.cs:16-39`):

- Usa `ConnectionStrings__Core` o `FLIT_IT_PG_ADMIN`, una conexión de administración a `postgres`. El arnés crea **su propia BD**, y `flit_local`, `flit_dev` y `postgres` están protegidas.
- Sin `CI=true`, si no hay motor, los tests **se saltan en silencio**. Pon `CI=true` para que fallen si falta PostgreSQL.
- `FLIT_IT_CARGA=1` habilita los tests de carga.

**Frontend** (desde `frontend/`):

```bash
pnpm vitest run \
  lib/api/__tests__/consolidado-lotes-client*.test.ts \
  lib/api/__tests__/admin-plataforma-consolidado-lotes.test.ts \
  lib/api/__tests__/admin-ot-consolidado-lotes.test.ts \
  lib/api/__tests__/tramites-client.network-documentos.test.ts \
  components/operacion/__tests__/DescargaMasivaConfirmModal*.test.tsx \
  components/operacion/__tests__/TramitesTable.*lote*.test.tsx \
  components/shared/__tests__/LoteDescarga*.test.tsx \
  components/admin/plataforma/__tests__/ParametrosMotorLotePanel.test.tsx \
  components/admin/transit-offices/__tests__/ClientProceduresSection.descarga-maestros.test.tsx \
  --testTimeout=30000
pnpm exec tsc --noEmit -p tsconfig.json
```

### 6.1 Ruido conocido (no es de la épica)

| Suite | Fallos | Causa |
|---|---|---|
| Application | 2 | `DocumentOcrPromptsTests` (texto de los prompts de OCR) |
| Infrastructure | ~23 | `StandaloneDocument*`, `Sync*` |
| Admin | ~122 | Falta la columna `approval_origin` en `flit_local`; `Prefill*` falla en worktrees (busca un directorio `.git`); `HU13129_AC2`; `CoreApiServiceRegistrationSnapshotTests` (2) en Windows por registros del entorno |
| Admin, bajo carga | variable | `TaskCanceledException` tras 3–5 min si corres varias suites a la vez. Repítelos sin carga |
| Integración | 4 | `PlatformRbacTests`: `orderby r.Code` depende de la collation de tu BD |
| Integración, intermitente | — | Timeout al abrir la conexión o en `ResetAsync` si hay muchos procesos usando PostgreSQL a la vez |
| vitest | — | `hu12196` (5), `hu10494` (CRLF), `inventario-paso-resumen`, `network-scope-analytics`; timeouts de `tramites-table.test.tsx` y `operacion` «Track A» bajo carga; el mock de `modo-consulta.test.tsx` sin `TramitesApiError` |

**Trampas del arnés de integración:**

- `TenantSeed.New` deriva el `tax_id` de los primeros 14 hex del Guid. Usa GUIDs con prefijos distintos.
- `uq_ura_active_user_tenant_product` admite un solo rol activo por usuario, compañía y producto. Siembra `AdminCompany` con `ProductCode = "plataforma"`.

---

## 7. Qué tener en cuenta (decisiones del responsable)

| Tema | Decisión |
|---|---|
| Regeneración | El lote **nunca** regenera ni sobrescribe |
| Hija de red sin consolidado | Se omite como `red_sin_consolidado`; no se genera nada en la hija |
| Hija en estado final sin FUR | Se queda como `fur_requerido` / `migrado_solo_lectura` (las guardas previas mandan) |
| Interruptor de documentos de red | Se respeta. 403 `network_documents_disabled`; si se apaga a mitad de lote, `acceso_revocado` |
| Auditoría de la vista de red | **No** se escribe en `network_access_audit` ni en «Accesos de mi red»; la traza queda solo en `consolidado_export_audit` |
| ZIP | Plano, igual que hoy |
| Tope total | Configurable (`max_items_per_batch`), editable en la pantalla del Super Admin |
| Rechazado | No es final para el lote; sin FUR se omite |
| ImageSharp | No se sube a 4.x (licencia). Se suprimen las 5 alertas y se limita la decodificación a PNG/JPEG acotado y recodificado |

---

## 8. Riesgos y puntos de atención

### 8.1 Despliegue e infraestructura

1. **Pasos manuales en cada VPS** (`docs/runbook-descarga-lotes-consolidados-nginx.md`):
   - disco temporal: al menos 600 MB, o al menos 4,5 GB si `max_mb_per_part = 2048`;
   - bloque nginx `location ~ ^/api/v1/consolidados/lotes/[^/]+/partes/[0-9]+$` con `proxy_buffering off`. En sesión `oidc` va hacia el frontend (BFF); en `legacy`, en el `server{}` de `api.<env>`;
   - `docker compose config -q`, `nginx -t` y reload.

   Sin nginx, cada descarga se vuelca a disco en el borde.
2. **Timeouts de proxies:** YARP corta tras 30 s sin bytes, el BFF tras 300 s entre bytes y el rewrite de Next tras 120 s. Una parte grande con red lenta puede cortarse; si pasa, baja `max_mb_per_part`.
3. **El navegador carga la parte entera en memoria.** El cliente descarga con `fetch` + `response.blob()` (`frontend/lib/api/download.ts:55-77`): el servidor hace streaming, pero el navegador junta todo el ZIP antes de guardarlo. Con 250 MB es manejable; **no subas `max_mb_per_part` mucho más** sin cambiar la descarga a un enlace directo o a streams.
4. **Migraciones con `CREATE TABLE IF NOT EXISTS`:** si alguna base ya tuviera una versión vieja aplicada, conservaría los CHECK antiguos. Hoy están Pending en todos los ambientes, así que no aplica, pero **no apliques versiones intermedias en ningún ambiente compartido**.
5. **CI del PR (#572):** `build-test` falla en restore con `NU1508` (duplicado de `NuGetAuditSuppress`) con el SDK 10.0.401 del runner. En local, con el mismo SDK, no se reproduce. Lo está revisando el LT.

### 8.2 Seguridad y datos personales (Ley 1581)

1. Los ZIP contienen **datos personales** (FUR, documentos). Por eso:
   - van cifrados;
   - se retienen 24 h y se purgan destruyendo la clave;
   - la auditoría registra quién descargó qué parte (`parte_descargada`, escrita antes del primer byte).

   No compartas ZIP de pruebas con datos reales y no uses `context/muestras/`.
2. **Hijas inactivas:** `ReadTenantIds` incluye hijas suspendidas o inactivas, así que una cabeza puede exportar en bloque el histórico de una hija suspendida. **Pendiente de decisión de producto/DPO.**
3. **ImageSharp 2.1.11:** 3 alertas High suprimidas como excepción. La superficie se redujo:
   - solo PNG/JPEG, ≤ 25 Mpx y sin metadatos (ICC);
   - recodificado a PNG antes de PdfSharpCore.

   Queda pendiente la HU de fondo (licencia 4.x o SkiaSharp). Efecto colateral: una firma **JPEG fotográfica** aumenta unas 9,5 veces el peso del PDF (las rúbricas reales son PNG).
4. **Keyring de Data Protection sin cifrar en BD** (deuda reportada). Quien lea la BD puede desenvolver las DEK de lotes vivos.
5. **Auditoría de parámetros:** `trg_audit_log` no rellena `audit_logs.changed_by`; el autor queda en `new_data.updated_by`. Un PUT sin `sub` responde 401 `usuario_no_identificado`.
6. **Retención de `client_ip`** en la auditoría (deuda reportada).
7. **SMTP en claro** en `docker-compose.yml` (preexistente): rotarlo.

### 8.3 Operación

1. **Un lote por usuario.** Un lote atascado bloquea al usuario hasta que termine, falle, se cancele o expire. Los topes de tiempo (L1) evitan arrendamientos indefinidos, y la cancelación libera al usuario.
2. **Carga sobre el generador de PDF:** `item_slots` (2) procesa en paralelo. Subirlo acelera los lotes, pero compite con la generación interactiva de consolidados.
3. **El lote de red revalida cada 60 s.** Perder el acceso no corta al instante; puede pasar hasta un minuto.
4. **El Super Admin no tiene compañía en el lote:** no aparece en las auditorías por tenant y solo él lo ve (otro SA recibe 404).
5. **Errores de BD en la revalidación de red** se reintentan como fallo técnico (`max_item_attempts`), nunca como acceso revocado.

---

## 9. Problemas frecuentes (troubleshooting)

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| No aparece *Descargar ZIP* | Falta el permiso o el claim | Revisa `consolidado-masivo.download` y vuelve a iniciar sesión |
| 503 `motor_inactivo` | `is_active = false` o no hay fila de parámetros | Enciéndelo en la pantalla, o verifica la fila del DDL 133 |
| El lote se queda «en cola» | Processor sin arrancar o motor apagado | Revisa los logs de `ConsolidadoLoteProcessor`; confirma `is_active` |
| 409 `lote_activo` | Ya tienes uno en curso | Espera, cancélalo o déjalo expirar |
| La descarga da 410 | Lote cancelado o purgado | Crea un lote nuevo |
| La descarga da 500 `parte_no_disponible` | Archivo temporal borrado (reinicio, limpieza) o keyring perdido | Crea un lote nuevo |
| 403 `network_*` en la vista de red | Falta rol, scope, hija fuera de red o documentos apagados | Revisa §4.3 |
| Vista de red sin botón | `documentosRed = false` o la consulta falló (cierre seguro) | Revisa `network_documents_concesion` y que `/network/documentos` responda 200 |
| `NU1902`/`NU1903` al compilar | Rama sin la supresión de ImageSharp | Usa esta rama; temporalmente, `-p:NuGetAudit=false` **solo en línea de comandos** |
| Tests de integración «verdes» sin correr | Sin `CI=true` y sin PostgreSQL se saltan | Exporta `CI=true` |
| `dotnet build` bloqueado | `Flit.Api` corriendo bloquea los `bin` | Detén la API antes de compilar |

---

## 10. Referencias

- ADR-0070: `services/core-api/docs/adr/ADR-0070-motor-lotes-descarga-masiva-consolidados.md` (Propuesto, adendas v2–v7).
- Runbook nginx y volumen: `docs/runbook-descarga-lotes-consolidados-nginx.md`.
- Contrato: `contracts/openapi/core-api.v1.yaml` (`CrearLoteConsolidadosRequest`, `LoteConsolidados`, `ParametrosMotorLote`, `NetworkDocumentosDisponibilidad`).
- DDL: `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/133-*.sql` y `134-*.sql`.
- Inventario multi-tenant: `services/core-api/tests/Flit.Integration.Tests/Tenancy/CoveredQueries.cs` (Q51–Q55).
- ADO: épica #13216; Features #13306, #13366, #13307, #13308 y #13415; HUs #13367–#13394 y #13417–#13420.
- PR: https://github.com/flitsas/flit/pull/572
