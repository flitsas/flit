# Plan técnico — Representantes legales (vista unificada) y orden del consolidado por tipo de trámite

- **Origen:** `ot-y-rl.txt` (raíz del repo)
- **Fecha:** 2026-07-31
- **Estado:** Propuesto — decisiones D1–D7 **cerradas**; **creado en ADO** (2026-07-31)
- **ADO:** Feature **#11173** (bloque A) y Feature **#11174** (bloque B); 11 HUs **#11175–#11185**.
  Todas en `New` / `FLIT - EVOLUTION\Sprint 2`, tags `DOR`, sin activar.
- **Rama base:** `develop` @ `97a07590` (árbol de trabajo sincronizado, 0 ahead / 0 behind)

El requerimiento son dos bloques independientes que **no comparten código**: se pueden crear como
dos Features y ejecutarse en paralelo.

---

## Bloque A — Compañías / Representantes legales

### A.0 Estado actual verificado

La pestaña «Representantes legales» de la configuración de compañía es
`RepresentativesAndVaultTab.tsx` y hoy contiene **tres secciones hermanas**: el directorio de
representantes, «Escrituras por compañía» (HU #11063) y el Baúl de firmas.

Dentro del directorio hay **tres superficies distintas** para lo que el requerimiento quiere ver como
una sola:

| Superficie | Archivo | Qué hace hoy |
|---|---|---|
| Tabla | `LegalRepresentativesTab.tsx` | Listado paginado + acciones Ver / Editar / Eliminar / Validar identidad / Baúl |
| Alta y edición | `LegalRepresentativesFormPanel.tsx` | `OtSidePanel` con empresas en **cards planas**, datos de la persona y tipos de trámite |
| Detalle (solo lectura) | `LegalRepresentativeDetailModal.tsx` | `Modal` con empresas anidadas, historial de escrituras, estado de firma e identidad |

Backend relevante:

- `AdminLegalRepresentativesEndpoints.cs` — `GET ""`, `GET /{id}`, `POST`, `PUT`, `DELETE`, `GET /procedure-types`.
- `AdminLegalRepresentativeIdentityEndpoints.cs` — solo `POST /send` y `POST /resend`.
- `AdminSignatureVaultEndpoints.cs` — `GET ""` **sin filtros** (`ListSignatureVaultQuery` solo tiene `TenantId`).
- `LegalRepresentativeSignatureResolver.cs` — al guardar resuelve automáticamente: (1) firma del baúl
  activa y vigente **por documento de la persona**, (2) validación de identidad vigente por documento,
  (3) ninguna.

### A.1 Brechas requisito por requisito

| # | Requisito del líder | Estado | Brecha real |
|---|---|---|---|
| 1 | Unificar ver / crear / editar | 🔴 | Tres superficies (tabla + panel + modal); el modal duplica datos que el panel no muestra |
| 2 | Mantener la ventana de registro | 🟢 | El botón «Nuevo representante» y `OtSidePanel` se conservan tal cual |
| 3 | Ver, editar, asociar escritura y firma desde la misma opción | 🔴 | Escrituras solo desde el modal de detalle; firma no se asocia desde ninguna pantalla |
| 4 | Acordeón de compañías, principal marcada con ícono | 🔴 | Cards planas en el panel; lista plana en el modal; «primaria» solo se distingue por el texto «Empresa primaria» |
| 5 | Escrituras al registrar la empresa o dentro del acordeón | 🟡 | `DeedsFormPanel` existe y funciona, pero solo se lanza desde el modal de detalle (requiere `zClassName="z-[120]"` por el overlay) |
| 6 | Precargar TODO al editar | 🟡 | `fromItem()` precarga desde el **item del listado**: trae compañías con contacto (HU #11058) pero **no escrituras, ni firma, ni identidad**; eso solo lo devuelve `GET /{id}` |
| 7 | Asociar firma del baúl filtrando por documento, solo vigentes | 🔴 | El baúl no acepta filtros y `LegalRepresentativeWriteInput` **no admite `SignatureVaultId`**: la firma siempre la elige el resolver, el usuario no puede escoger |
| 8 | Asociar identidad automática al crear si ya está aprobada | 🟢 | Ya lo hace `LegalRepresentativeSignatureResolver` paso (2) |
| 9 | Enviar y reenviar correo de validación | 🟢 | `POST /identity/send` y `/resend`, ya expuestos en tabla y detalle |
| 10 | Botón «asociar validación de identidad» que actualice el registro | 🔴 | **No existe para representantes.** Sí existe para mandatarios: `POST .../identity/link` → `IAdminIdentityValidationService.LinkExistingAsync`, que además vincula al sujeto (`LegalRepresentative.LinkIdentity`) |

### A.2 Diseño propuesto

**Una sola superficie** (D1): `LegalRepresentativesFormPanel` pasa a ser el panel del representante con
tres modos — `view` / `create` / `edit` — sobre el `OtSidePanel` actual. «Ver» abre el panel en `view`
con un botón «Editar» que cambia de modo **sin cerrar**; `LegalRepresentativeDetailModal.tsx` se retira
junto con su test. El botón «Nuevo representante» se conserva (requisito 2). Al **crear**, el panel no
se cierra: pasa a `edit` para que se puedan asociar escrituras en el mismo flujo (D3).

**La pestaña queda con dos secciones**, no tres (D4): «Representantes legales» y «Baúl de firmas».
`CompanyDeedsSection.tsx` y su test se eliminan de `RepresentativesAndVaultTab.tsx`. Antes de borrarla,
resolver **D4-bis** (escrituras legadas sin representante).

**Precarga completa**: al abrir en `view`/`edit` el panel llama a `fetchLegalRepresentative(tenantId, id)`
—que ya devuelve `companies[].deeds[]`, `identityStatus`, `identityValidUntil`, `firmaBaulVigente`,
`signatureVaultId`— y precarga desde ahí con skeleton, en vez de desde el item de la tabla.

**Acordeón** (`RepresentativeCompaniesAccordion`, componente nuevo compartido por los tres modos):
cabecera con razón social + NIT + ícono de principal (`aria-label="Compañía principal"`) + contador de
escrituras vigentes; cuerpo con el contacto editable y el bloque de escrituras (historial con estado
`vigente/vencida/futura/inactiva` + «Ver PDF» + «Editar» + «Asociar escritura», reutilizando
`DeedsFormPanel`). En modo `create` el bloque de escrituras aparece deshabilitado con el motivo
(«disponible al guardar»), y se habilita en cuanto el panel pasa a `edit` (D3). La marca de principal
sale de un flag `isPrimary` **explícito** en `LegalRepresentativeCompanySummary` (D2): hoy habría que
inferirla comparando contra `RepresentedCompanyId`, que es la columna denormalizada **deprecada y
nullable** desde la HU #10932, así que apoyar la UI en ella es frágil.

**Firma del baúl seleccionable**:
- `GET .../signature-vault` acepta `documentType`, `documentNumber` y `soloVigentes`; el DTO
  `SignatureVaultResponse` ya trae `Estado`, `VigenciaDesde/Hasta` y `CodigoHash`, así que no cambia.
- `LegalRepresentativeWriteInput` acepta `SignatureVaultId?`. Si viene, el writer **valida** (misma
  tenencia, documento coincidente con el del representante, activa y vigente) y lo persiste saltándose
  el resolver; si no viene, se conserva el comportamiento actual (compatibilidad con el consumo del
  wizard y con las 8 HUs ya mergeadas del Feature #10899/#10929).

**Identidad**: nuevo `POST /api/v1/admin/companies/{tenantId}/legal-representatives/{id}/identity/link`,
copia literal del de mandatarios (`AdminMandateSignerIdentityEndpoints.LinkAsync`), delegando en
`LinkExistingAsync` y devolviendo `409 sin_identidad_vigente` cuando no hay nada que vincular. En el
panel, el bloque de identidad muestra estado + vigencia y ofrece «Enviar» / «Reenviar» / «Asociar
validación», y tras vincular refresca el registro (`identity_validation_ref` y el estado de firma).

### A.3 Trampas identificadas

- **Escrituras durante el alta**: `company_deeds` exige `company_id` (la compañía representada, que se
  crea por upsert en el POST del representante) y, desde la migración `44-HU-representante-escritura`,
  lleva `representative_id`. En un alta, ninguno de los dos existe todavía — resuelto por D3.
- **Escrituras legadas**: las creadas antes de esa migración tienen `representative_id = NULL` y **no
  aparecen** en el detalle. Al eliminar la sección hermana (D4) se quedan sin ninguna pantalla que las
  muestre → **D4-bis**, a resolver antes de la HU A5.
- **Z-index**: `DeedsFormPanel` se lanza desde un contenedor con overlay; ya existe la prop
  `zClassName` justamente por eso. Al mover el lanzamiento al `OtSidePanel` hay que revisar la pila.
- **Regresión #11058**: el formulario reenvía `companies[]` completo y el upsert normaliza los vacíos a
  `null`. Cualquier campo de contacto que el acordeón deje de precargar **borra el dato en BD**.
- **Reversión de la HU #11063**: aquella HU sacó las escrituras a una sección hermana; este
  requerimiento las devuelve al representante y la sección se elimina (D4). Hay que dejarlo escrito en
  el PR: es una decisión de PO que se revierte, no un descuido.

---

## Bloque B — OT: orden de los documentos del consolidado

### B.0 Estado actual verificado

La infraestructura **ya existe casi entera**, pero está desconectada:

- Tabla `admin.ot_document_precedence` con `(tenant_id, procedure_type_id, document_type_id, sort_order)`,
  RLS por tenant (`09-HU10152-ot-admin.sql:91-133`).
- Endpoints `GET`/`PATCH /document-precedence?procedureTypeId=` (`AdminOtEndpoints.cs:271-286`).
- Pantalla en `/admin/transit-offices/[id]/documents` → pestaña «Prelación», con
  `DocumentPrecedenceList.tsx`: **drag and drop HTML5 nativo + reordenamiento por teclado WCAG**, sin
  dependencias externas.
- `ResolvedDocumentMatrixResolver.cs` resuelve orden y obligatoriedad con precedencia OT > Default.

### B.1 Brechas requisito por requisito

| # | Requisito del líder | Estado | Brecha real |
|---|---|---|---|
| 1 | El OT selecciona tipo de trámite y ve los documentos que aplican | 🔴 | `ListByProcedureTypeAsync` devuelve **solo filas ya persistidas** en `ot_document_precedence`, y nada las siembra → la pantalla sale vacía. Y `ReorderBatchAsync` devuelve `null` (422) si `existing.Count == 0` ⇒ **la pantalla es inoperante hoy** |
| 2 | Arrastrar y soltar para cambiar la página | 🟢 | Ya implementado, con teclado incluido |
| 3 | El consolidado se genera con los documentos del trámite en ese orden | 🔴 | El consolidado del **wizard** (`ConsolidadoCommand.cs:185`) usa `ConsolidadoOrderingResolver`, que es una lista **hardcodeada** (`TraspasoConsolidadoOrdering.cs:13-43`). Solo el **maestro** consulta la matriz (`AdminOtEndpoints.cs:1230-1240`) |
| 4 | No cambia la obligatoriedad | 🟡 | La obligatoriedad vive en `procedure_document_requirements.is_mandatory` + `document_requirement_overrides` (HU #10198), tabla distinta de la prelación. Hay que **blindarlo con tests**, sobre todo si se tocan los documentos generados |
| — | Los documentos generados salen «de un listado» | 🔴 | **No.** Están hardcodeados. `document_types` sí tiene `fur`, `compraventa`, `mandato`, `tramite_virtual`, `impronta`; pero **no** `certificado_identidad`, `certificado_identidad_vendedor`, `certificado_rues`, `certificado_rnmc`, `escritura`, `escritura_comprador` ni `licencia_transito` |

Además, aunque se configure el orden, `GenericConsolidadoOrdering.SelectByResolvedMatrix:50` **antepone
una cabecera fija** (`fur`, `licencia_transito`, `certificado_identidad`, `certificado_identidad_vendedor`)
antes de la matriz: con el código de hoy el OT **no puede mover el FUR de la primera página**, que es
justamente lo que pide el requerimiento.

Y la matriz base sembrada para traspaso tiene 7 documentos —`compraventa`, `impronta`, `soat`, `rtm`,
`paz_salvo`, `cedulas`, `cert_tradicion` (`25-HU10522-traspaso-matrix-seed.sql`)—: sin los generados,
reordenar solo cubre la mitad del expediente.

### B.2 Diseño propuesto

1. **La lista de prelación se vuelve la unión** matriz base del trámite ∪ documentos generados
   aplicables ∪ overrides guardados, ordenada por el override si existe y por `default_sort_order` si
   no. El `PATCH` pasa a ser **upsert** (hoy exige que la fila ya exista).
2. **Documentos generados como catálogo de primera clase** (D5): `ot_document_precedence.document_type_id`
   es FK a `tramites.document_types`, así que los tipos que faltan se crean ahí con
   `is_system_generated = true` y **sin** fila en `procedure_document_requirements`. Tipos a dar de alta:
   `certificado_identidad`, `certificado_identidad_vendedor`, `certificado_rues`, `certificado_rnmc`,
   `escritura`, `escritura_comprador`, `licencia_transito`. Y a marcar como generados los que ya
   existen: `fur`, `mandato`, `tramite_virtual`, `compraventa`, `impronta`.

   **El flag NO debe leerse como «excluido del checklist».** `compraventa` e `impronta` son a la vez
   generados y documentos del checklist —`compraventa` es *obligatoria* en la matriz base de traspaso
   (`25-HU10522-traspaso-matrix-seed.sql:21`)—, así que definirlo como exclusión los borraría del
   checklist y rompería el requisito 4. La semántica correcta es aditiva:

   - **Checklist del gestor** = `procedure_document_requirements`, exactamente como hoy. Sin cambios.
   - **Lista ordenable del OT** = matriz base ∪ tipos con `is_system_generated`, deduplicada por
     `document_type_id`.

   Así `compraventa` aparece una sola vez, sigue siendo obligatoria y además es reordenable.
3. **El consolidado del wizard ordena por la matriz resuelta**: `ConsolidadoCommand` ya recibe
   `ChecklistMatrixCompleteness`, que se apoya en el puerto `IResolvedChecklistMatrixProvider`
   (`ResolvedChecklistMatrixProvider.cs` puentea Admin → Trámites sin acoplar contextos). Se reutiliza
   ese puerto —no hace falta uno nuevo— resolviendo con `instance.ProcedureTypeId` y
   `instance.TransitOfficeId`, y se cae a `ConsolidadoOrderingResolver` cuando el OT no configuró nada.
4. **Quitar la cabecera fija** de `SelectByResolvedMatrix` una vez los generados estén en la matriz.
5. **Mapeo código ↔ tipo de adjunto**: `document_types.code` y `ProcedureInstanceAttachment.Tipo`
   coinciden en el checklist pero no en los generados. Se fija una tabla de equivalencias explícita
   (una sola función, con test) en vez de confiar en que los strings coincidan.

### B.3 Trampas identificadas

- **Regla «ningún consolidado dentro de otro»**: `consolidado` y `consolidado_maestro` están excluidos
  en los tres ordenadores. Cualquier ordenador nuevo debe heredar la exclusión —olvidar
  `consolidado_maestro` fue exactamente el bug que duplicaba el expediente entero al aprobar el OT—,
  igual que el filtro de `biometric_*` y el de MIME fusionables.
- **Invalidación**: reordenar no regenera los consolidados ya emitidos (`consolidado_wizard_vigente` /
  `consolidado_maestro_vigente`). Ver **D6**.
- **Quipux**: `QuipuxApplicationPorts.cs:31` llama al maestro con `matrizPrecedencia: null` → ese envío
  sigue saliendo con el orden hardcodeado. Hay que decidir si se le pasa la matriz también.

---

## Decisiones cerradas (2026-07-31)

| # | Decisión | Resolución |
|---|---|---|
| D1 | Superficie única | **Side panel** (`OtSidePanel`), que ya es el de alta/edición: aloja mejor el acordeón y evita el conflicto de z-index del panel de escrituras |
| D2 | Marca de compañía principal | **Flag `isPrimary` explícito en el DTO**; no inferirla de `represented_company_id`, que está deprecada y es nullable desde la HU #10932 |
| D3 | Escrituras durante el alta | **Habilitar el bloque tras el primer guardado**: al crear, el panel no se cierra —pasa a modo `edit`— y ahí ya existen `representative_id` y `company_id` |
| D4 | Sección hermana «Escrituras por compañía» (HU #11063) | **Se elimina.** El único punto de gestión de escrituras pasa a ser el acordeón del representante. Ver la consecuencia en D4-bis |
| D5 | Modelado de los documentos generados | **`is_system_generated` en `tramites.document_types`**, sin fila en `procedure_document_requirements`: el resolutor de orden los incluye, el checklist del gestor los ignora |
| D6 | ¿Reordenar invalida consolidados vigentes? | **No.** Se aplica en la siguiente generación y la UI lo advierte; invalidar cruzaría N trámites de todos los clientes del OT |
| D7 | ¿El envío a Quipux usa la matriz? | **Sí.** Se le pasa la precedencia resuelta en vez del `null` actual (`QuipuxApplicationPorts.cs:31`) |

### D4-bis — consecuencia de eliminar la sección hermana (CERRADA 2026-07-31)

**Resuelta: se trabaja solo en local y las escrituras huérfanas se borraron.** En `flit_local` había
**5 de 10** con `representative_id IS NULL` (descripciones de prueba: «Escrituras 1», «Escrituras 1
juan», «Escrituras 0 juan», «escrituras 0 diego», «eqe»); se eliminaron junto con sus 5 vínculos en
`company_deed_companies` (cascada). Quedan 5 escrituras, todas con representante. **La HU A5 ya no
está bloqueada.**

**Hallazgo colateral (candidato a bug propio):** `procedure_instance_attachments.source_deed_id` **no
tiene FK** hacia `admin.company_deeds`, pese a haberse introducido como referencia en la migración
`43-HU10936`. Nada impide borrar una escritura que un expediente ya emitido está citando: tras el
borrado, un adjunto del trámite `TRM-2026-000011` (aprobado) quedó apuntando a una fila inexistente.
El PDF del expediente está intacto —el id solo servía de trazabilidad—, pero la integridad referencial
no está garantizada.

<details>
<summary>Contexto original de la decisión</summary>

Las escrituras creadas **antes** de la migración `44-HU-representante-escritura` tienen
`representative_id = NULL` y el detalle del representante las filtra por ese campo. Hoy siguen siendo
visibles porque `CompanyDeedsSection` lista por compañía; al retirarla **dejan de verse desde cualquier
pantalla**, aunque siguen en BD y el resolutor del trámite no las escoge (busca por representante).

No se pueden backfillar: solo estaban ligadas a la empresa, no hay forma de saber a qué representante
corresponden. Tres salidas, por orden de preferencia:

1. **Re-crearlas desde el representante** y limpiar las huérfanas (es la salida ya anotada cuando se
   introdujo la columna). Viable si el volumen en DEV/PDN es bajo — **hay que medirlo antes de borrar
   la sección**.
2. Mostrarlas en el acordeón como bloque aparte («heredadas de la empresa», solo lectura). Barato, pero
   contradice la decisión de que las escrituras son privadas por representante.
3. Dejarlas huérfanas sin más. Es lo que ocurre por defecto si no se hace nada.

**Acción previa a la HU A5** — contar las escrituras huérfanas en DEV y PDN (`psql` no está en el PATH
de esta máquina, así que queda pendiente de ejecutar):

```sql
SELECT count(*) FILTER (WHERE representative_id IS NULL) AS legadas,
       count(*)                                          AS total
FROM admin.company_deeds
WHERE is_active;
```

Con un volumen bajo, la salida 1 (re-crear y limpiar) es la buena.

</details>

---

## Descomposición propuesta

### Feature A — #11173 «Vista unificada de representantes legales» (23 SP)

| HU | ADO | Tipo | Alcance | SP | Depende de |
|---|---|---|---|---|---|
| A1 | #11175 | BE | Filtros `documentType` / `documentNumber` / `soloVigentes` en `GET signature-vault` + `SignatureVaultId` explícito y validado en create/update | 3 | — |
| A2 | #11176 | BE | `POST /legal-representatives/{id}/identity/link` reusando `LinkExistingAsync` + actualización del registro | 3 | — |
| A3 | #11177 | BE | `isPrimary` y orden estable en `companies[]` del listado y el detalle | 2 | — |
| A4 | #11178 | FE | Panel unificado `view`/`create`/`edit` con precarga completa desde `GET /{id}`; se retira `LegalRepresentativeDetailModal` | 5 | A3 |
| A5 | #11179 | FE | Acordeón de compañías con principal marcada + escrituras anidadas (historial, asociar, editar, ver PDF) y **retiro de la sección «Escrituras por compañía»** | 5 | A4 |
| A6 | #11180 | FE | Selector de firma del baúl y bloque de identidad (enviar / reenviar / asociar) dentro del panel | 5 | A1, A2, A4 |

Orden: A1 ‖ A2 ‖ A3 → A4 → A5 ‖ A6. (D4-bis ya está resuelta: las escrituras huérfanas de `flit_local`
se borraron el 2026-07-31.)

### Feature B — #11174 «Orden del consolidado configurable por tipo de trámite» (19 SP)

| HU | ADO | Tipo | Alcance | SP | Depende de |
|---|---|---|---|---|---|
| B1 | #11181 | BE/DB | `is_system_generated` en `document_types` + alta de los 7 tipos generados que faltan + migración; **el checklist no cambia** (test de no regresión) | 5 | — |
| B2 | #11182 | BE | Prelación = unión (base ∪ generados ∪ overrides) deduplicada; `PATCH` como upsert | 3 | B1 |
| B3 | #11183 | BE | Mapeo código ↔ tipo de adjunto + tests de regresión del orden actual de traspaso y matrícula | 3 | B1 |
| B4 | #11184 | BE | Consolidado del wizard ordena por la matriz resuelta; se retira la cabecera fija de `SelectByResolvedMatrix`; se pasa la precedencia también al envío Quipux (D7); fallback intacto | 5 | B2, B3 |
| B5 | #11185 | FE | Pantalla de prelación operativa: lista completa, estado vacío real, aviso de «aplica desde la próxima generación» | 3 | B2 |

Orden: B1 → B2 ‖ B3 → B4 ‖ B5.

**Total: 11 HUs, 42 SP.** Ninguna activada — la activación es gate humano.

---

## Verificación

- **Backend:** `dotnet test` de `Flit.Admin.Tests`, `Flit.Infrastructure.Tests`, `Flit.Tramites.Application.Tests`.
  Baseline actual conocido: Admin 776, backend agregado 3149 verdes / 1 omitido.
  Trampa conocida: **`Flit.Api` corriendo bloquea su `bin`** y los proyectos de test lo referencian ⇒ hay
  que pararla antes de prometer la regresión completa.
- **Frontend:** `pnpm typecheck` + `vitest`. Baseline con fallos preexistentes en `hu10494-status-badge`,
  `hu10492-dark-mode` y flakies de `LegalRepresentativesTab` por timeout de 5 s bajo carga: comparar
  siempre contra baseline antes de atribuir un fallo a estos cambios.
- **Visual:** los tests cubren decisiones, no layout. El orden del consolidado exige verificación con
  PDF real (`services/core-api/artifacts/render-documentos`).
- **ADR:** el bloque B toca el orden del expediente, decidido en su momento en las HUs #10455 / #10522 /
  #10706 y ajustado por #10926/#10936. Un ADR nuevo que declare «el orden del consolidado lo define el
  OT; las listas hardcodeadas son fallback» está justificado.
