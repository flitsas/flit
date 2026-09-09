# ADR-0056: Generación documental standalone por puertos acotados, sin tocar el pipeline de trámites

**Fecha**: 2026-09-08
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente), Product Owner (módulo Generación documental), Architecture Agent
**Tags**: arquitectura, backend, frontend, seguridad, modulo-generacion-documental, modulo-admin

> **Citar este ADR por slug**, no por número: el directorio `services/core-api/docs/adr/` tiene ocho
> números duplicados (0030, 0033, 0036, 0050, 0053 y otros). El slug
> `ADR-0056-generacion-documental-standalone` es la referencia estable.

## Contexto

El Feature #12201 pide un módulo administrativo (`/admin/generacion-documental`) que emita
**Certificado RUES** y **Documento de Transferencia de Dominio** **sin crear una
`ProcedureInstance`** (CF-02), con historial por tenant, descarga presignada auditada y carga masiva
XLSX de hasta 100 filas. El PO cerró: ruta y permisos propios (`generacion-documental.read` /
`.generate`), `document_type = transferencia_dominio_generada`, respuesta `{ id, status }` sin PDF
inline, snapshot inmutable, columnas `download_count` y `document_snapshot`, y captura «placa
primero» con prellenado encadenado (CF-25).

El plan técnico previo (`docs/plan-tecnico-generacion-masiva-docs.md`) partía de tres premisas que la
verificación contra el código **desmiente**:

1. Que había que **romper el acople** de `IAttachmentStorage.SaveAsync(Guid procedureInstanceId, …)`.
   En realidad ese parámetro es una **clave de agrupación opaca**: `SignatureVaultArtifactStorage`,
   `IdentitySignatureArtifactStorage` y `DeedDocumentStorage` le pasan `tenantId`, y
   `MandateTemplateStorage` le pasa `transitOfficeId`. Hay comentario explícito en el código
   (`SignatureVaultArtifactStorage.cs:18-20`).
2. Que había que **modificar `RuesCertificateData`** por llevar `ProcedureInstanceId` y
   `ReferenceNumber`. `RuesCertificatePdfGenerator` **no usa** el primero y usa el segundo **solo
   para el nombre del archivo** (`:71-72`).
3. Que el prellenado sin instancia era gratis o inexistente. Existe `POST /tramites/preflight-preview`
   (RUNT sin instancia), pero exige tipo de trámite, corre el gate de organismo y **bloquea con 409
   por duplicidad de trámite activo** — semántica inaceptable para emitir un documento.

Restricción dura adicional: `Flit.Admin.Application.csproj` **no referencia**
`Flit.Tramites.Application`. Ninguna pieza del pipeline de trámites es visible desde el módulo Admin.

## Decisión

**Construir el módulo sobre puertos acotados declarados en `Flit.Admin.Application` e implementados
en `Flit.Infrastructure`, sin modificar el pipeline de trámites** (salvo elevar a `public` la
visibilidad de `RuesActorJuridicalLookup`), y persistir en **una sola tabla**
`admin.standalone_documents` para documentos individuales y de lote, más una cabecera
`admin.standalone_document_batches` en el incremento I3 — **sin tabla `batch_items`**.

## Alternativas consideradas

### Opción 1: Puertos acotados en el módulo Admin *(elegida)*

`IStandaloneDocumentStorage`, `IStandaloneRuesCertificateRenderer`, `IStandaloneTransferGenerator`,
`IStandaloneRuesCompanyLookup`, `IStandaloneVehiclePrefill`, `IStandalonePersonPrefill`, todos
declarados en Admin.Application y adaptados en Infrastructure sobre las piezas existentes. Una tabla
de documentos; cabecera de lote en I3.

**Pros:**
- Cero cambios en código compartido con trámites: el riesgo de regresión del expediente se extingue.
- Reutiliza cuatro patrones vigentes (storage acotado del baúl/escrituras, seeder de permisos
  idempotente, DDL numerada + migración EF, tabs de improntas).
- Es la única forma de compilar dada la restricción de referencias de proyecto.
- Una fila por documento hace que historial, filtro por lote, ZIP «solo generados» y redescarga sean
  el mismo código en I1 y en I3.

**Cons:**
- Indirección extra (puerto + adaptador) por cada pieza reusada.
- Duplicación superficial de fachadas de consulta: convivirán la ruta con instancia y la standalone.
- La tabla única mezcla filas individuales y de lote; obliga a índices parciales disciplinados.

**Esfuerzo:** M (I1) · M (I2) · L (I3)
**Riesgos:** divergencia futura entre las dos rutas de consulta (mitigada al reusar el mismo
`IConsultationProviderChainResolver` y los mismos mappers).

### Opción 2: Desacople profundo del pipeline de trámites

Cambiar la firma de `IAttachmentStorage.SaveAsync` a un scope explícito, quitar
`ProcedureInstanceId` de `RuesCertificateData` y generalizar `RuntPersonLookupHandler` /
`RunPreflightPreviewHandler` a `Guid?`.

**Pros:**
- Un solo camino canónico; el próximo módulo standalone no paga fontanería.
- El storage deja de mentir sobre su primer parámetro.
- Evita la duplicación de fachadas antes de que exista.

**Cons:**
- `IAttachmentStorage` tiene ~30 consumidores (adjuntos, FUR, consolidado, biométrica, licencia de
  tránsito, improntas de firma): el refactor obliga a regresión completa del expediente **antes** de
  poder mergear el primer incremento.
- Rompe el límite de 800 líneas por PR solo con el refactor, sin entregar valor.
- **No ahorra los puertos**: Admin.Application seguiría sin ver esos tipos.

**Esfuerzo:** L–XL
**Riesgos:** regresión del pipeline documental del trámite (probabilidad media, impacto alto);
bloqueo de la cadena crítica.

### Opción 3: Job-first, todo es un lote desde el primer incremento

Modelar `standalone_document_jobs` + `standalone_documents` desde I1; la generación individual es un
job de una fila. El prellenado se resuelve solo en frontend encadenando endpoints existentes.

**Pros:**
- El incremento masivo sale casi gratis; una sola máquina de estados y un solo worker.
- Sin fachadas nuevas de backend.

**Cons:**
- **Contradice el contrato de estados cerrado por el PO** (CF-21): con jobs desde I1, `pending` y
  `processing` se vuelven observables en generación individual.
- Reutilizar `/preflight-preview` arrastra sus gates (409 por duplicidad, 422 por organismo), que son
  correctos en el wizard y equivocados aquí.
- Infraestructura de lote por adelantado para valor que se cobra en I3: sobre-diseño.

**Esfuerzo:** L en I1, S en I3
**Riesgos:** violación de un criterio funcional ya cerrado; acople a semántica ajena.

## Tradeoff aceptado

Se acepta **pagar indirección y una duplicación superficial de fachadas** a cambio de **no tocar el
pipeline de trámites**. La Opción 2 sería preferible si el acople fuera real, pero no lo es: el
primer parámetro de `SaveAsync` ya funciona como clave de agrupación con cuatro precedentes
productivos, y el generador RUES ignora el identificador del trámite. Refactorizar código compartido
por un problema que ya está resuelto significa asumir el riesgo de regresión del expediente a cambio
de un beneficio estético. Además, la restricción de referencias de proyecto obliga a los puertos en
cualquiera de las tres opciones: la Opción 2 los paga **igual**, más el refactor.

La Opción 3 se descarta porque rompe una decisión funcional cerrada del PO (contrato de estados de
dos capas) y porque el único punto de reutilización que ofrecía —el preview de vehículo— trae gates
de trámite que aquí producen falsos bloqueos.

Sobre el modelo de datos: se acepta **una tabla que mezcla filas individuales y de lote** a cambio de
no mantener dos máquinas de estados equivalentes. Una fila fallida del XLSX es un intento de
documento con `status = 'error'`, exactamente el mismo estado que ya existe para una generación
individual fallida; modelarla en una tabla aparte duplicaba `status`, `error_code` y la relación con
el documento, y obligaba a dos consultas para pintar un solo historial.

## Consecuencias

### Lo que se gana

- El módulo se entrega sin ningún cambio de comportamiento en trámites; el riesgo R7 del registro del
  Feature queda cerrado.
- El certificado RUES standalone **no puede** divergir del del expediente: es el mismo generador.
- El historial, el filtro por lote y el ZIP se implementan una sola vez.
- Los tres incrementos son independientes: I1 mergea sin esperar a I2 ni a I3.

### Lo que se pierde

- Dos rutas de consulta (con y sin instancia) que pueden divergir si alguien toca una sola.
- El primer parámetro de `IAttachmentStorage.SaveAsync` sigue llamándose `procedureInstanceId`
  aunque ya no signifique eso; la deuda semántica queda documentada, no resuelta.
- No hay componente de captura de persona compartido con el wizard: la UX de prellenado se replica,
  con el costo de mantener dos implementaciones del mismo patrón.

### Cambios operacionales

- **Estimación:** el Feature pasa de 8 HUs / 39 SP a **10 HUs / 49 SP**. Las dos HUs nuevas cubren el
  prellenado standalone (backend 5 SP, frontend 5 SP), que no estaba en el plan original.
- **Migración:** un `.sql` numerado (`105-…`, primer número libre) como fuente de verdad + migración
  EF que lo ejecuta con `EmbeddedDdl.LoadUp`. En I3, `106-…`.
- **Permisos:** módulo `generacion-documental` sembrado por un método **idempotente propio** en
  `DevelopmentAuthSeeder`; ampliar el array de `SeedBaseModulesAsync` no funciona (early-return en
  bases ya sembradas).
- **Frontend:** la entrada del dock debe colgar de los módulos accesibles del usuario, no de
  `currentUser?.isSuperAdmin` como el resto de las entradas admin de `Shell.tsx`; de lo contrario
  AdminCompany no vería el módulo.
- **Seguridad:** la RLS de este repo es **decorativa** (no hay `FORCE ROW LEVEL SECURITY` y la
  aplicación conecta como owner). El aislamiento real es el filtro de repositorio más el ownership
  check del endpoint, y los tests de aislamiento deben ser de autorización, no de RLS.

## ADRs relacionados

- `ADR-0037-snapshot-rues-congelado-por-tramite` — se replica el **patrón** de snapshot congelado
  (`{ queriedAt, fields }`) en una columna propia; no se extiende su alcance al trámite.
- `ADR-0029-preview-presigned-get-inline` — la redescarga usa presigned de vida corta; la URL nunca
  se loguea. Queda abierta la necesidad de `Content-Disposition: attachment`.
- `ADR-0033-representantes-legales-y-escrituras-por-compania` — origen del patrón de puerto acotado
  de storage que este ADR generaliza al módulo.
- `ADR-0051-traspaso-unilateral-capacidades-declaradas` — referencia técnica del escenario B; sus
  exenciones aplican al FUR, no se extienden al instrumento privado.
- `ADR-0042-documentos-personalizados-por-compania` — no aplica en este Feature; la personalización
  de plantillas por compañía queda fuera de alcance.
- `ADR-0023-catalogo-global-roles` — roles SuperAdmin / AdminCompany sobre los que se conceden los
  permisos nuevos.

## Notas para agentes

- **Backend Agent**: no tocar `IAttachmentStorage`, `RuesCertificateData` ni
  `RuesCertificatePdfGenerator`. El único cambio permitido en `Flit.Tramites.*` es elevar
  `RuesActorJuridicalLookup` a `public`. Autorización **por permiso**, nunca por
  `AdminAuthorization.SuperAdminPolicy` de grupo. Resolver la idempotencia **antes** de consultar al
  proveedor RUES.
- **Database Agent**: `.sql` numerado 105 (y 106 en I3) + migración EF envolvente. Índices parciales
  para idempotencia y lote. Trigger de inmutabilidad **`BEFORE UPDATE` únicamente**, con lista blanca
  que deje pasar `downloaded_at` y `download_count`. Prohibido `BYTEA`. Ninguna FK hacia `tramites.*`.
- **Frontend Agent**: entrada del dock por módulos accesibles, no por `isSuperAdmin`. Mapa de estados
  de cuatro internos a tres etiquetas en un único archivo. Paso de «Régimen aplicable» antes del
  selector de escenario. Replicar, no importar, el patrón de `ActorsForm.tsx`.
- **QA Agent**: tests de aislamiento como **autorización** (403/404), nunca apoyados en RLS.
  Verificar que dos descargas incrementan el contador y que el trigger sí bloquea la mutación del
  snapshot. Checklist §13 del anexo normativo en el DoD de los escenarios.
- **Security Agent**: `document_snapshot` es PII alta y no sale en listados ni en logs; presigned
  siempre después del ownership check y nunca logueada; el tope de 100 filas y la idempotencia son
  controles de costo y de abuso.
- **Infra Agent**: sin infraestructura nueva. El worker de I3 es un `BackgroundService` del propio
  `core-api`; solo hay que fijar concurrencia y timeout del reaper por ambiente.

## Referencias externas

- Resolución 20233040017145 de 2023 (Mintransporte), arts. 5.1.5, 5.1.6, 5.1.8, 5.3.2.1, 5.3.2.2 y
  5.3.2.3–5.3.2.13 (traspasos especiales, excluidos por CF-24).
- `docs/plantilla-transferencia-dominio.md` — dictamen `expert-doc-engine`, escenarios A/B/C,
  validaciones VB/VA y checklist de aprobación del PDF. Custodio exclusivo del contenido normativo.
- `.claude/state/diseno-feature-12201.md` — diseño técnico completo: diagramas de secuencia, DDL de
  referencia, contratos OpenAPI, archivos por incremento y notas operativas.
- Ley 1581 de 2012 (Habeas Data) — minimización de PII en listados, logs, XLSX persistido y metadata
  global.
