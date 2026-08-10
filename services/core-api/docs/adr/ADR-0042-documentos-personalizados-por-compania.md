# ADR-0042: El documento personalizado de la compañía sustituye al del sistema conservando su tipo documental

**Fecha**: 2026-08-10
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente), Product Owner (origen del requisito y las 12 restricciones funcionales)
**Tags**: arquitectura, backend, frontend, documental, modulo-companias, modulo-tramites, multi-tenant, habeas-data
**Feature**: #11309 — `[ADMIN] - Documentos personalizados por compañía: sustitución del mandato y de la solicitud de trámite virtual, con historial reversible`

**Supersede parcialmente**: [ADR-0036-mandatarios-multiples-y-mandato-por-ot] (**Aceptado**) — ver §«Supersede
parcial» más abajo. Se conserva íntegro el modelo de mandatarios múltiples y la **aplicabilidad** del mandato por
OT como condición de emisión; quedan **inertes**, y solo para las compañías que personalicen el mandato, los
mecanismos que producen el *contenido* del documento y su firmante.

---

## Contexto

Una compañía B2B (tenant) que opera FLIT como back-office de su propio canal —canal configurado
`notification_channel = tenant_api`, «API Renting cliente»— necesita que el expediente lleve **su** mandato y
**su** solicitud de trámite virtual, no los que FLIT genera. Son documentos que su área legal ya tiene aprobados y
que el organismo de tránsito le acepta con ese texto; el PDF que hoy genera FLIT ([ADR-0036-mandatarios-multiples-y-mandato-por-ot])
es un contrato construido a partir de la configuración del OT.

Restricciones vigentes en el repositorio que condicionan la solución:

- El expediente se arma en un único punto (`GenerarFurHandler`, `FurCommand.cs`): una lista `generated` de
  `GeneratedDocument` que después se persiste reemplazando por `Tipo` **solo** lo que tenga `Source="system"`.
- El pie de página de cada parte del consolidado sale de `DocumentLabels.Display(Tipo)` y la marca de agua de
  estado del compositor ([ADR-0030-marca-documental-compartida-y-merger-compositor]): el `Tipo` del adjunto **es**
  la llave de la identidad visual.
- Registrar un tipo documental nuevo cuesta **cinco** puntos de registro (semilla `document_types`, matriz
  documental por tipología, catálogo de ordenables por OT, los dos resolutores de orden por modalidad, y las
  reglas condicionales/etiquetas) y ataría el orden del expediente a
  [ADR-0038-orden-del-consolidado-lo-define-el-ot], que sigue en `Propuesto`.
- El aislamiento real entre tenants es el `WHERE tenant_id` **manual**: las políticas RLS existen pero no se
  evalúan (sin `FORCE ROW LEVEL SECURITY` y la aplicación es owner de las tablas).
- El PO cerró de antemano: reemplazo **estático** (sin inyección de datos ni de firmas), nivel **compañía**
  (el tenant B2B), **sin anclaje ni congelado por trámite**, historial versionado y reversible, y auditoría por
  `audit_log` / `IAdminAuditWriter` (el diff de settings compara escalares y un archivo no cabe ahí).

## Decisión

El PDF que la compañía carga **sustituye** al documento generado **conservando el mismo `Tipo`**
(`mandato` / `tramite_virtual`), se resuelve **en cada generación** contra la configuración vigente del tenant
—sin snapshot por trámite—, se persiste como adjunto con un origen propio (`Source="company"`) y una referencia a
la versión exacta usada, y pasa por el compositor documental igual que cualquier otra parte del expediente. La
sustitución se aplica en **un único punto** del pipeline, después de que la lista `generated` esté completa, y la
precedencia entre orígenes del mismo `Tipo` deja de depender de la fecha de carga para pasar a ser una regla
**declarada**.

El interruptor de la funcionalidad **es** la existencia de una versión activa por `(tenant, tipo de documento)`:
no hay un booleano paralelo que pueda contradecirla. El canal se lee siempre de
`admin.tenant_operational_policies.notification_channel` y **no se copia** a la tabla nueva.

## Alternativas consideradas

### Opción 1: Sustitución estática conservando el `Tipo`, resuelta en el pipeline, versionada (elegida)

**Pros:**
- Hereda **gratis** el pie de página, la marca de agua de estado, la matriz documental, el checklist y el orden
  del expediente: todo eso se llavea por `Tipo`, y el `Tipo` no cambia.
- Cero puntos de registro nuevos en el catálogo documental y **cero dependencia** de un ADR aún `Propuesto`.
- Un solo punto de intervención en el pipeline ⇒ el cambio de comportamiento es auditable en un diff pequeño.
- Con `FLIT_SMTP` la funcionalidad es literalmente invisible: sin versión activa el resolutor no responde y el
  pipeline es byte a byte el de hoy.
- El historial versionado y reversible se modela con el patrón ya probado de `admin.company_deeds`
  ([ADR-0033-representantes-legales-y-escrituras-por-compania]): puerto de storage acotado, PDF en S3, metadatos
  en BD, presigned inline para ver.

**Cons:**
- Dos filas del mismo `Tipo` pueden coexistir (`company` y una carga del gestor), así que obliga a **declarar** la
  precedencia que hoy resuelve una carrera de fechas — y eso cambia el desempate para todos los tipos, no solo
  para estos dos.
- El documento del sistema se genera y se descarta en la iteración en que hay sustitución (coste de CPU, sin
  llamadas salientes nuevas).
- Deja inertes mecanismos ya aceptados de [ADR-0036-mandatarios-multiples-y-mandato-por-ot] sin poder detectarlo
  desde la interfaz de configuración del OT.

**Esfuerzo:** M
**Riesgos:** que la precedencia declarada altere el resultado del consolidado en trámites existentes con
compraventa cargada por el usuario; mitigable con pruebas de composición sobre los dos caminos de consolidado.

### Opción 2: Tipo documental nuevo (`mandato_personalizado`, `tramite_virtual_personalizado`)

Registrar el personalizado como un tipo propio del catálogo y dejar que el orden del expediente decida.

**Pros:**
- Precedencia trivial: son documentos distintos, no compiten por el mismo `Tipo`.
- El documento del sistema y el de la compañía pueden coexistir en el expediente si el negocio lo quisiera.
- La traza es evidente desde el propio nombre del adjunto.

**Cons:**
- Cinco puntos de registro (semilla de tipos, matriz documental por tipología, ordenables por OT, resolutores de
  orden por modalidad, reglas condicionales y etiquetas) y una **dependencia de un ADR `Propuesto`** para que el
  orden sea configurable.
- El OT tendría que reordenar su prelación en cada organismo para que el personalizado no cayera al final como
  «Anexo».
- Si el del sistema no se retira, el organismo recibe **dos** mandatos; si se retira, se acaba necesitando la
  misma regla de sustitución que la Opción 1 pero con dos tipos que mantener sincronizados.

**Esfuerzo:** L
**Riesgos:** expedientes con documentos duplicados o mal ordenados en producción durante la transición.

### Opción 3: Plantilla configurable con inyección de datos y firmas

Tratar el PDF de la compañía como plantilla de overlay (patrón del FUR multiplantilla) con un mapa de coordenadas
por compañía, para seguir estampando datos del trámite, sellos de identidad y firmas del baúl.

**Pros:**
- El documento seguiría siendo un documento del sistema: datos frescos, firmas reales, sello del baúl
  y ningún mecanismo de [ADR-0036-mandatarios-multiples-y-mandato-por-ot] quedaría inerte.
- Elimina de raíz los riesgos R-3 y R-4.

**Cons:**
- Exige un mapa de coordenadas **por compañía y por versión del PDF**; el precedente del FUR multiplantilla
  demostró que eso es una cola infinita de recalibraciones.
- Cada vez que la compañía cambie una línea de su documento, el mapa se rompe en silencio.
- Contradice de frente la restricción del PO (reemplazo estático, sin inyección).

**Esfuerzo:** L (y recurrente)
**Riesgos:** documentos con datos estampados fuera de sitio, que es peor que un documento sin datos.

## Tradeoff aceptado

Se elige la Opción 1 porque **el `Tipo` es la llave de toda la maquinaria documental de FLIT** (pie, marca de agua,
matriz, checklist, orden por OT): conservarlo convierte en herencia gratuita lo que la Opción 2 obliga a registrar
cinco veces y a atar a una decisión que todavía no está aceptada. El precio —declarar una precedencia que hoy es una
carrera de fechas— hay que pagarlo de todas formas: la carrera ya existe entre la compraventa del sistema y la del
usuario ([ADR-0035-compraventa-autogenerada-siempre-y-sin-membrete]) y este Feature solo la haría más visible. La
Opción 3 se descarta no por esfuerzo sino porque el PO pidió explícitamente un reemplazo estático: inyectar datos en
un PDF que la compañía puede cambiar sin avisar es una fuente permanente de documentos mal estampados.

Se descarta también, por decisión expresa del PO, **anclar la versión al trámite**: no hay snapshot ni congelado, y
un trámite aprobado que se regenere tomará el documento vigente en ese momento (riesgo R-1, aceptado).

## Supersede parcial de [ADR-0036-mandatarios-multiples-y-mandato-por-ot]

Cuando una compañía tiene una versión activa de `mandato`, quedan **inertes** para los trámites de ese tenant:

1. **Aplicabilidad por OT × tipo de persona.** `IMandateRequirementPolicy` y
   `admin.transit_office_mandate_config` **siguen decidiendo si el mandato se emite** (restricción funcional 6: se
   sustituye solo donde el trámite habría generado el documento), pero **dejan de decidir qué se emite**. La
   configuración del OT pasa de definir contenido a ser únicamente un predicado de aplicabilidad.
2. **Firmante = usuario autenticado, con su 409 `mandatario_requerido`.** El gate de aprobación
   (`MandatoApprovalHandler`) resuelve hoy «el mandato aplica **sii** existe un adjunto de `Tipo='mandato'`» y de ahí
   deriva el 409 `mandatario_requerido` y el 409 `mandatario_identidad_requerida`. Sobre un PDF estático no hay
   firmante que estampar, así que ese gate queda inerte y **debe** excluir el origen `company`: sin esa exclusión, el
   aprobador quedaría bloqueado exigiendo un mandatario que el documento no usa. `MandateSignerSelector`,
   `instance.MandateSignerId` y la selección al aprobar dejan de tener efecto sobre este documento.
3. **Plantillas y familias de mandato por OT.** `MandatoTemplateResolver`, `MandatoFamiliaCodes`, el mandatario
   institucional (nombre y NIT), la ciudad de cámara, la sigla del mandatario y el objeto compuesto —incluidas las
   transformaciones declaradas que se incorporaron al objeto del contrato— no intervienen.
4. **Política de firma del mandatario.** `IMandatoFirmaPolicy` y los tres modos (`SinBloque` por convenio
   compañía↔organismo, `Manual` por firma física/diferida, `Estampada`) quedan inertes: los bloques de firma son
   los que traiga el PDF de la compañía. De aquí sale el riesgo R-3.

**Se conserva íntegro** de [ADR-0036-mandatarios-multiples-y-mandato-por-ot]: la multiplicidad de mandatarios por
compañía y su directorio (siguen siendo la fuente de verdad para las compañías que no personalizan, y para el resto
del expediente), el enlace mandatario↔`user_id`, la generación de la solicitud de trámite virtual como documento
siempre exigido, y la aplicabilidad del mandato como condición de emisión. Este ADR **no** deroga ninguna tabla ni
ningún índice de aquel.

## Riesgos aceptados

| Id | Riesgo | Por qué se acepta |
|----|--------|-------------------|
| **R-1** | Un trámite **aprobado** que se regenere toma el documento **vigente**, no el que entró al registro: el expediente puede cambiar después de aprobado. | Decisión expresa del PO (restricción 4: sin anclaje ni congelado). El anclaje exigiría un snapshot por trámite, que es exactamente el mecanismo que [ADR-0037] tuvo que sustituir. |
| **R-2** | El PDF es estático: **nadie coteja** que la placa, el NIT o el organismo que aparecen en él correspondan al trámite. Un documento con datos de otro trámite pasa sin objeción. | Es la definición de «reemplazo estático». La responsabilidad del contenido queda del lado de la compañía, que es quien lo firma. |
| **R-3** | El mandato personalizado puede **no llevar bloques de firma**: ni mandante, ni mandatario, ni sello del baúl, ni sello de validación de identidad. | El organismo acepta el documento de la compañía tal cual; si necesitara firma, la compañía la incorpora a su propio PDF. |
| **R-4** | La solicitud de trámite virtual personalizada sale **sin el sello del baúl** (vigencia + hash), así que el organismo no puede verificar la firma leyendo el documento. | Misma razón que R-3; el sello es una garantía que FLIT aporta a su propio documento, no una exigencia del organismo. |
| **R-5** | El **consolidado maestro cacheado** sigue sirviendo la versión anterior hasta la siguiente regeneración: cargar o reactivar una versión no invalida nada. | Restricción 7: no hay invalidación masiva. Invalidar todos los consolidados de un tenant al cargar un PDF contradice [ADR-0038-orden-del-consolidado-lo-define-el-ot] y convierte una operación de configuración en un trabajo de lote. |

## Consecuencias

### Lo que se gana

- Una compañía B2B puede llevar su propio mandato y su propia solicitud de trámite virtual al expediente sin que
  FLIT toque una plantilla ni despliegue código.
- Historial versionado y reversible: ninguna carga borra la anterior, cualquier versión se puede reactivar y se
  puede volver al documento del sistema sin perder nada.
- La precedencia entre orígenes del mismo tipo documental pasa de ser una carrera de `uploaded_at` a ser una regla
  declarada y verificable — beneficio que alcanza también a la compraventa del sistema frente a la del usuario.
- Aparece una guarda compartida de «solo se retira lo que el sistema generó», que es la pieza que el
  **Bug #11310** necesita para no duplicarse en las cuatro ramas de limpieza.

### Lo que se pierde

- Sobre el documento personalizado FLIT deja de aportar contenido: ni datos del trámite, ni firmas, ni sellos.
  Cuatro mecanismos ya aceptados quedan inertes (§Supersede parcial).
- El expediente de una compañía que personaliza deja de ser reproducible a partir del estado del trámite: hay que
  saber además qué versión estaba activa en el instante de la generación (por eso el adjunto guarda la referencia a
  la versión y el trámite registra el hecho).
- El pipeline genera y descarta un PDF en cada iteración con sustitución.

### Cambios operacionales

- Tabla nueva `admin.company_personalized_documents` (versiones, con índice único parcial de una sola activa por
  `(tenant, tipo)`), columna nueva en `tramites.procedure_instance_attachments` para la referencia de versión, y un
  valor nuevo en `source` (`company`) que **no** requiere tocar restricciones: la columna es `varchar(20)` sin
  `CHECK`.
- La migración se ordena **después** de `20260808110000_BackfillCertificaciones`.
- Auditoría: triggers estándar (`trg_audit_log`, `trg_row_version`) sobre la tabla nueva, más `IAdminAuditWriter`
  para el verbo de la operación desde el API. **No** se usa el diff de configuración del tenant.
- Evento nuevo por trámite cuando se emite un personalizado, para que el hecho quede trazado donde se pueda
  responder ante un rechazo del organismo.

## ADRs relacionados

- [ADR-0036-mandatarios-multiples-y-mandato-por-ot] — **superseded parcialmente** por este ADR (§Supersede parcial).
- [ADR-0030-marca-documental-compartida-y-merger-compositor] — el personalizado entra al compositor como cualquier
  otra parte: pie con el nombre del documento y marca de agua de estado con su excepción en
  `aprobado`/`entregado`/`preparado`.
- [ADR-0033-representantes-legales-y-escrituras-por-compania] — precedente del artefacto PDF custodiado **a nivel
  compañía** con puerto de storage acotado; se replica su patrón.
- [ADR-0029-preview-presigned-get-inline] — vista del PDF cargado con presigned GET inline y TTL corto.
- [ADR-0038-orden-del-consolidado-lo-define-el-ot] — razón por la que **no** hay invalidación masiva de
  consolidados al cargar o reactivar una versión.
- [ADR-0035-compraventa-autogenerada-siempre-y-sin-membrete] — precedente de coexistencia de dos orígenes del mismo
  tipo documental; su intención es lo que la precedencia declarada por fin garantiza.
- [ADR-0032-regeneracion-consolidado-tras-rechazo] — cascada de invalidación que este ADR **no** amplía.
- [ADR-0041-certificaciones-externas-modelo-canonico-persistido] — precedente de resolver el contenido documental
  contra base de datos, sin llamadas salientes durante la generación.
- [ADR-0037-snapshot-rues-congelado-por-tramite] — mecanismo de anclaje por trámite que aquí se descarta a propósito
  (R-1).

## Notas para agentes

- **Database Agent**: `admin.company_personalized_documents` con `tenant_id` (FK `identity.tenants`,
  `ON DELETE RESTRICT`), `document_type` con `CHECK` en `('mandato','tramite_virtual')`, versión incremental, estado
  del ciclo de vida, `storage_path` + `storage_sha256` + `page_count`, único parcial de **una sola activa** por
  `(tenant_id, document_type)` (patrón `uq_mandate_signer_companies_active`), único `(tenant_id, document_type, version)`,
  RLS `tenant_isolation` por paridad, triggers `trg_row_version` y `trg_audit_log`. Columna
  `source_personalized_document_id` en `tramites.procedure_instance_attachments` (`ON DELETE SET NULL`, espejo de
  `source_deed_id`). DDL idempotente; migración EF **posterior** a `20260808110000_BackfillCertificaciones`, con
  `Flit.Infrastructure` como startup.
- **Backend Agent**: puerto nuevo en Trámites siguiendo la forma de `IProcedureDeedResolver` (con implementación
  nula como default seguro, para que los tests que no lo ejerciten se comporten como hoy) y adaptador en
  Infrastructure que cruza Admin sin acoplar módulos. **Un solo punto** de sustitución en el pipeline, después de
  que la lista `generated` esté completa. Guarda compartida de «solo se retira lo generado por el sistema». Excluir
  `Source='company'` del gate de mandatario al aprobar. La precedencia declarada va en la deduplicación del
  consolidado y aplica a los **dos** caminos (wizard y maestro). Aislamiento del fallo: PDF ilegible ⇒ se cae al
  documento del sistema con advertencia registrada, nunca se rompe el expediente.
- **Frontend Agent**: sección de documentos personalizados en la configuración de la compañía, **visible solo si el
  canal del tenant es `TENANT_API`** (leída de la configuración, sin copia local); por tipo de documento: estado
  vigente, carga con subida directa a storage, vista previa inline, historial de versiones con reactivación y
  «volver al documento del sistema». Aplicar `flit-design-guardian`.
- **QA Agent**: con `FLIT_SMTP`, paridad byte a byte con el comportamiento actual. Sustitución solo donde el trámite
  habría generado el documento. Dos regeneraciones consecutivas dejan **una** fila del tipo y el mismo hash.
  Reactivación de versión antigua, vuelta al documento del sistema, y cambio de canal a `FLIT_SMTP` (se desactiva el
  reemplazo y **se conserva** el historial). PDF corrupto/cifrado ⇒ el consolidado sale con el documento del
  sistema. Aislamiento cross-tenant en las seis consultas nuevas.
- **Security Agent**: el PDF cargado puede contener datos personales (Ley 1581) — no loguear su contenido ni la
  presigned URL completa (lleva firma HMAC); comprobar ownership **antes** de firmar la URL de vista. El aislamiento
  efectivo es el `WHERE tenant_id` manual: exigir test que consulte con el tenant equivocado y espere cero filas.
  Validar el archivo en servidor (magic bytes, no cifrado, parseable, tamaño y páginas), nunca confiar en el
  `content-type` que declara el cliente ni en el SHA-256 que calcula.
- **Infra Agent**: sin dependencias nuevas (PdfSharpCore ya está para abrir y contar páginas). Retención: los
  binarios de versiones históricas no se borran; vigilar el crecimiento del bucket por tenant.

## Referencias externas

- Ley 1581 de 2012 — protección de datos personales (contenido del PDF cargado).
- Bug #11310 — las ramas de «generar-o-limpiar» borran por tipo sin filtrar origen (preexistente, radicado aparte).
