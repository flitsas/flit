# Handoff — ADR-0050: el tipo de trámite como fuente única de conformación

**Rama:** `feature/adr-0050-tipo-tramite-fuente-unica` (20 commits sobre `develop`)
**Fecha de cierre de sesión:** 2026-08-24
**ADR:** [`services/core-api/docs/adr/ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md`](../services/core-api/docs/adr/ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md) (estado: Propuesto)
**Diagnóstico de origen:** [`docs/diagnostico-habilitacion-dinamica-tipos-tramite.md`](diagnostico-habilitacion-dinamica-tipos-tramite.md)
**Trazabilidad acumulada:** `.cursor/state/pending-ado-comments.md` (276 líneas, pendiente de publicar en ADO)

---

## 1. El problema

FLIT modelaba los trámites en **tres vocabularios paralelos que no coincidían**:

| Vocabulario | Valores | Dónde vivía |
|---|---|---|
| `procedure_types.family` | 3 familias, 21 tipos | catálogo |
| `procedure_instances.modalidad_entrada` | 2 (`matricula_inicial`, `traspaso`) | columna del expediente |
| `procedure_instances.tipologia_codigo` | 2 | columna del expediente |

`TipologiaResolver.FromFamily` colapsaba `MATRICULAS`, `OTROS` y cualquier familia desconocida en
`matricula_inicial`. Consecuencia concreta: **un `BLINDAJE` o un `DUPLICADO_PLACA` nacía con el
flujo, el checklist, el FUR, la portada y las causales de rechazo de una matrícula inicial**, y nada
fallaba.

El objetivo: que el gestor elija **familia → tipo** desde el catálogo y que habilitar un tipo sea
**configuración, no despliegue**.

### Hallazgo que redefinió el plan

**FEATURE-08 ya había construido el motor dinámico** y el diagnóstico no lo mencionaba.
`DynamicGateEvaluator` (8 de 9 `section_type`), `gate_profile` tipado, `procedure_type_snapshots`
—que ya escribía en producción— y 30 tests estaban mergeados en `develop` detrás de un flag.
El trabajo no era *construir un motor* sino **terminarlo y conectarlo**.

---

## 2. Decisiones tomadas (todas del usuario, no inferidas)

| Tema | Decisión |
|---|---|
| Fuente de verdad | `tramites.procedure_types`, única y viva; congelada por expediente en `procedure_type_snapshots` |
| `modalidad_entrada` | **Eliminada.** La clasificación es `family` |
| `tipologia_codigo` | **Eliminada.** La tipología es `procedure_types.code` |
| Barrera | Columna `wizard_enabled boolean NOT NULL DEFAULT false` |
| Causales de rechazo | Tercer bucket por familia (`MATRICULAS` / `TRASPASO` / `OTROS`) |
| Datos existentes | **Reset total del esquema `tramites`** en DEV, QA y producción (sin trámites reales) |
| Frontend F08 | Rehacer contra `develop` actual; la rama `feature/AB-10823-08-frontend-configurador` solo como referencia de diseño |
| Alcance | Los 21 tipos del catálogo operables al cierre |
| Integraciones | ICT, Quipux, documentos legales y analítica, todas dentro |

### Aclaración de negocio sobre la familia OTROS

Textual del usuario, y es la que gobierna el diseño del recorrido:

> La ruta de OTROS pide la placa y el dueño del vehículo, y luego algunos documentos dependientes
> del trámite, que debe ser parametrizable. La familia TRASPASO pide vendedor y comprador; MATRÍCULAS
> normalmente solo comprador; y OTROS también un solo actor, **el mismo dueño del vehículo**, que no
> lo vende ni lo compra — solo le hace cambios: color, combustible, carrocería, blindaje,
> levantamiento de prenda, duplicado de tarjeta, etc.

Implementación: el titular se persiste con el rol `comprador` (es el modelo de datos), pero el paso
se titula **«Propietario»**.

---

## 3. Arquitectura resultante

```
procedure_types (code, name, family, gate_profile, wizard_enabled)
  ├─ procedure_steps → procedure_sections (section_type)   ← recorrido
  ├─ procedure_document_requirements                        ← checklist
  └─ external_refs.quipux                                   ← radicación
        │  al crear la instancia
        ▼
procedure_type_snapshots ── congela code/name/family/gateProfile/stepSectionTypes
        │
        ▼
procedure_instance (procedure_type_id)   ← sin modalidad_entrada, sin tipologia_codigo
        │
        ├─ WizardStateQuery → DynamicGateEvaluator → pasos, gates y CAPACIDADES
        ├─ FUR / portada / mandato → por code y name del snapshot
        ├─ Quipux → external_refs.quipux
        └─ Reportes → family y procedure_type_id
```

**Pieza clave añadida al final:** el estado del wizard publica ahora `capabilities`, una proyección
**parcial** del `gate_profile` (`entryMode`, `requiresSeller`, `requiresBuyer`,
`requiresCommercialValue`, `requiresBiometrics`, `biometricActors`, `hasPrendaGate`). Es lo que
permite que el frontend deje de decidir con `modalidad === 'traspaso'`.

Lo que **no** viaja, a propósito: `validateOtOperability`, `simitMode`, `validateDuplicateProcedure`.
Publicarlos invitaría al frontend a reimplementar un gate que solo el backend puede resolver. Hay un
test que lo fija.

---

## 4. Estado por HU

| HU | Estado | Qué entregó |
|---|---|---|
| HU-00 | ✅ | ADR-0050 (cierra el TODO que el DDL de F08 arrastraba desde julio) |
| HU-01 | ✅ | DDL 79 (aditivo) + DDL 80 (destructivo, **aplicado**) |
| HU-02 | ✅ | `ProcedureFamily` como enum con parser único; `Family`/`TypeCode`/`TypeName` derivadas del tipo; 608 ocurrencias migradas en 262 archivos |
| HU-03 | ✅ | 8 de 8 gaps de FEATURE-08 cerrados; motor dinámico como único camino |
| HU-04 | ✅ | Barrera movible + exigida en el servidor (último commit) |
| HU-05 | ✅ | DDL 81 + 82: los 21 tipos con recorrido, en 5 perfiles |
| HU-06 | ✅ | Selector familia → tipo, rutas por `code`, registry de secciones, wizard por capacidades |
| HU-07 | ✅ | Documentos legales nombran el trámite real |
| HU-08 | ✅ | ICT y Quipux por datos, no por substring |
| HU-09 | ✅ | Filtros OT por familia + analítica unificada |

---

## 5. Inventario

### DDL nuevos (core-api)

| Script | Migración | Qué hace |
|---|---|---|
| `79-tipo-tramite-barrera-y-familia.sql` | `20260822090000` | `wizard_enabled`, CHECK de 3 familias, reescribe `trg_autoset_plate_flow_status` para decidir por `gate_profile->>'requiresPlateRequest'` |
| `80-tramites-reset-fuente-unica.sql` | `20260822110000` | **DESTRUCTIVO.** `DELETE` de instancias, `DROP COLUMN modalidad_entrada, tipologia_codigo`, `rejection_reasons.modalidad → family` |
| `81-parametrizacion-tipos-operativos.sql` | `20260822093000` | Recorridos de `MATRICULA_NUEVA` (5 pasos) y `TRASPASO_STANDARD` (6 pasos) |
| `82-parametrizacion-catalogo-completo.sql` | `20260822100000` | 17 tipos restantes, 5 recorridos, 87 requisitos documentales |
| `83-quipux-refs-variantes-propias.sql` | `20260824120000` | `external_refs.quipux` de `MATRICULA_LEASING` (13/MIL) y `TRASPASO_UNILATERAL` (213/TRU) |
| `84-bi-view-sin-categoria-vehicular.sql` | `20260824123000` | Redefine `analytics.v_procedure_detail_report` sin la categoría `vehicular` |
| `85-habilitar-tipos-operativos.sql` | `20260824150000` | Enciende `wizard_enabled` en los dos canónicos |

### DDL nuevo (core-ict)

`21-ICT-procedure-type-mapping-v2.sql` — alinea los 16 `transaction_type` a codes canónicos y añade
`family`, `requires_commercial_value`, `resolves_transit_office_from_runt`.
core-ict no usa migraciones EF: los scripts corren en orden al arrancar (`IctSchemaBootstrapper`).

### Piezas nuevas de código

**Backend**
- `Flit.Tramites.Domain/Enums/ProcedureFamily.cs` — enum + parser único tolerante
- `Flit.Tramites.Domain/Enums/ProcedureSectionTypes.cs`
- `Flit.Tramites.Domain/Tramites/ValueObjects/ProcedureClassification.cs`
- `Flit.Tramites.Application/UseCases/ProcedureTypes/SetWizardEnabledCommand.cs` — la palanca

**Frontend**
- `components/operacion/wizardCapabilities.ts` — traduce capacidades a decisiones de render
- `components/operacion/sectionRendererRegistry.ts` — `section_type` → cuerpo de paso
- `components/operacion/SelectorTipoTramite.tsx` — selector familia → tipo
- `hooks/useTiposHabilitados.ts`
- `lib/api/types/familia-labels.ts` — reemplazó 5 `MODALIDAD_LABEL` duplicados
- `app/tramites/nuevo/[procedureTypeCode]/page.tsx` (era `[modalidad]`)

### Eliminados

`TipologiaResolver.cs`, `TipologiaMatrizCatalog.cs`, `TipologiaJourney.cs`,
`app/tramites/nuevo/[modalidad]/page.tsx`, y sus tests.

Con `TipologiaMatrizCatalog` desapareció la invariante quemada `esperados = Traspaso ? 6 : 5`, que
era **el verdadero techo estructural**: impedía que existiera un recorrido que no fuera de 5 o 6 pasos.

### Superficie de API nueva o cambiada

| Endpoint | Cambio |
|---|---|
| `PUT /api/v1/superadmin/procedure-types/{id}/wizard-enabled` | **Nuevo.** Body `{"enabled": bool}`. 422 con lista de impedimentos si no está listo |
| `POST /api/v1/tramites/instances/from-consulta` | Acepta `procedureTypeCode`; deriva el identificador exigido de `entryMode` |
| `POST /api/v1/tramites/preflight-preview` | Ídem |
| `GET /api/v1/tramites/document-requirements/preview` | Acepta `procedureTypeCode` (mantiene `modalidad` por compatibilidad) |
| `GET /api/v1/tramites/instances/{id}/wizard` | Devuelve `typeName` y `capabilities` |
| Reportes OT (`/api/v1/admin/ot-metrics/*`) | `modalidad` → `family` (valores en MAYÚSCULAS) + `procedureTypeId` nuevo |
| `GET /api/v1/admin/rejection-reasons` | `modalidad` → `family` |

> Ninguno de estos endpoints está en `contracts/openapi/core-api.v1.yaml`, así que **no hay drift
> que corregir** — pero tampoco documentación que actualizar.

---

## 6. Cómo levantar y probar

```bash
dotnet run --project services/core-api/src/Flit.Api
```

```bash
cd frontend && npm run dev
```

API en `:4003`, frontend en `:3000` con proxy de `/api/v1` (`next.config`, `CORE_API_ORIGIN`).

**Ojo:** en la máquina del usuario hay un `dotnet watch` gestionando la API y otro el Gateway
(`:4002`). Si el puerto 4003 se vuelve a ocupar solo, es watch relanzando. Para reiniciarlo, matar el
proceso hijo `Flit.Api` y tocar un archivo fuente dispara la recompilación.

### Habilitar tipos para probar

```bash
curl -X PUT http://localhost:4003/api/v1/superadmin/procedure-types/{id}/wizard-enabled \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"enabled":true}'
```

O directamente:

```sql
UPDATE tramites.procedure_types SET wizard_enabled = true WHERE code = 'BLINDAJE';
```

### Criterios de aceptación no negociables

1. **Regresión de los dos flujos vivos:** `MATRICULA_NUEVA` (5 pasos, VIN-first) y
   `TRASPASO_STANDARD` (6 pasos, placa-first) deben comportarse igual que antes.
2. **Ruta OTROS:** pide placa + propietario, se titula con el nombre del tipo, captura un solo actor,
   no muestra datos comerciales, no ofrece elegir organismo (lo impone el RUNT).

---

## 7. Qué falta

### 7.1 Validación de negocio del catálogo — **bloqueante para producción**

Los 87 requisitos documentales y los 5 recorridos del DDL 82 son **base técnica que nadie de negocio
ha revisado**. Los 15 tipos que hoy están encendidos en la base de desarrollo van a *funcionar*, pero
lo que piden puede no ser lo que el trámite realmente exige.

Tres documentos usan el código `otro` porque el catálogo no tiene uno propio:
- certificado de blindaje
- conversión a gas
- denuncia por pérdida de placa

### 7.2 Quipux: 17 tipos sin códigos

Sus códigos de trámite en la secretaría **no están documentados en el repo ni son derivables**.
Radicar con un código inventado deja el trámite mal presentado, que es más caro que no presentarlo.
Quedan no elegibles hasta que negocio los aporte. `quipux_other` sigue sin uso.

**Aparte:** las tres banderas `transit_offices.quipux_registration/_transfer/_other` **no se leen en
el camino de radicación** — `EncolarEnvioQuipuxHandler` nunca las consulta. Encender ese gate
bloquearía radicaciones que hoy funcionan (las tres columnas tienen `DEFAULT false`). Es decisión de
negocio, documentada en `QuipuxTipoTramiteMap.Familia`.

### 7.3 Configurador de superadmin (frontend)

**No existe UI.** El backend lo soporta entero —upsert de pasos con `sectionType`, validador que
impide publicar mal parametrizado, endpoint de la barrera— pero hoy parametrizar un tipo nuevo es
SQL. Era el S6 del plan por el lado admin; lo entregado del S6 fue el lado del gestor.

### 7.4 SPs de ICT que ramifican por número de transacción

`05-ICT-sp-business.sql` y `06-ICT-sp-external.sql` usan `transaction_type IN (1,2)` / `IN (3,4)`.
**Se dejaron a propósito:** ahí el número *es* el vocabulario — validan el payload v1 contra el
contrato v1 del cliente externo, no el comportamiento del tipo v2. Cambiarlo alteraría lo que ICT
acepta de sus clientes.

### 7.5 Deuda menor

- **48 fallos de test del frontend preexistentes de `develop`**, sin investigar. Verificado dos veces
  que el número no cambió con este trabajo. Archivos: `tramite-wizard` (12), `NotificacionesBankPanel`
  (13), `OTConfigTablePanel` (8), y 11 más con 1-2 cada uno.
- **Enum `TramiteModalidadEntrada` residual**: ya solo lo usan ~10 archivos de test como fixture.
  Hay una tarea en cola para migrarlos a `ProcedureFamilyCodes` y borrarlo.
- **Cuatro componentes de paso** (`DeclaracionesTramite`, `DocumentChecklist`, `BiometricStep`,
  `FirmaFurStep`) siguen recibiendo `modalidad`, pero vía dos adaptadores que traducen su pregunta
  real (`modalidadPorEntrada` / `modalidadPorPartes` en `wizardCapabilities.ts`). Un trámite de OTROS
  ya cae del lado correcto en los cuatro; migrarlos a capacidades directas no cambiaría comportamiento.
- **Sin recorrido end-to-end** verificado con la app levantada.

---

## 8. Trampas encontradas (leer antes de tocar)

Cosas que costaron tiempo y que volverán a morder:

1. **`tramites.procedure_steps` NO tiene `deleted_at`** — su borrado lógico es `is_active`.
   `procedure_sections` no tiene ninguno de los dos. El DDL 85 murió en el arranque por esto, y la
   validación previa no lo detectó porque se hizo contra una tabla de imitación que sí traía la
   columna. **Una imitación que no calca el esquema real valida el script contra una base que no
   existe.** Hay un test estático que lo fija ahora.

2. **`WizardState.modalidad` transporta la FAMILIA**, no una modalidad. El nombre del campo es
   heredado y el backend escribe ahí `procedure_types.family` desde HU-02. Cualquier comparación
   contra `'traspaso'` en minúsculas **nunca acierta**. Usar `esFamiliaTraspaso()` de
   `wizardCapabilities.ts`, que acepta ambas escrituras.

3. **`FamilyCode` es una propiedad calculada**: EF no la traduce. Dentro de proyecciones LINQ hay que
   escribir `(p.ProcedureType != null ? p.ProcedureType.Family : "")`.

4. **`Property` gana a `Ignore` en la configuración de EF.** Renombrar un `builder.Property(...)` en
   vez de borrarlo deja el mapeo vivo y rompe con "No backing field could be found".

5. **Seeds que usan `ctx.X.Any()` no ven el ChangeTracker** → conflicto de clave duplicada. Usar
   `.Local.Any(...) && .Any(...)`.

6. **`TRUNCATE ... CASCADE` sobre `procedure_instances` habría vaciado `admin.plate_range_details`**
   y otras tres tablas que referencian expedientes con `ON DELETE SET NULL`. Por eso el DDL 80 usa
   `DELETE`.

7. **`Database:AutoMigrate` está en `true` por defecto.** Arrancar la API aplica lo pendiente sin
   preguntar. Para inspeccionar sin migrar: `Database__AutoMigrate=false dotnet run ...`.

8. **Los `appsettings.json` de `Flit.Api` y de core-ict son configuración local del usuario**
   (credenciales de su base). Deben quedar **fuera de todo commit**. Ya se coló dos veces y hubo que
   rehacer commits.

---

## 9. Verificación al cierre

| Suite | Resultado |
|---|---|
| core-api | **5515 tests, 0 fallos** |
| core-ict | **119 tests, 0 fallos** |
| frontend `tsc --noEmit` | limpio |
| frontend `vitest` | 2350 pasando, 48 fallando (preexistentes de `develop`) |

Todos los DDL se validaron ejecutándolos en clusters PostgreSQL 18 aislados y temporales, dos pasadas
cada uno más sus `Down` — con la salvedad de la trampa nº 1, que enseñó que la imitación tiene que
calcar el esquema real.

---

## 10. Siguiente paso sugerido

Por orden de dependencia:

1. **Sentar con negocio la matriz documental y los recorridos del DDL 82.** Es lo único que separa
   «21 tipos que funcionan» de «21 tipos correctos», y bloquea producción.
2. **Recorrido end-to-end** de un trámite de cada familia con la app levantada: crear, recorrer,
   generar FUR y consolidado, verificar que la portada dice el `name` del catálogo, rechazar con una
   causal de la familia correcta.
3. **Configurador de superadmin** (§7.3), que es lo que convierte «habilitar un tipo» en algo que el
   negocio pueda hacer sin SQL.
