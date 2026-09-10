# Plan técnico — Otros ajustes (RL, ruta de registro, organismo en el paso 1, mandatarios y formatos de mandato)

- **Origen:** `otros-ajustes.txt` (raíz del repo)
- **Fecha:** 2026-08-01
- **Estado:** Propuesto — decisiones D1–D8 **cerradas**; **creado en ADO** (2026-08-01)
- **ADO:** Features **#11187** (A), **#11188** (B), **#11189** (C), **#11190** (D), **#11191** (E) y 15 HUs
  **#11192–#11206**, todas en `New` / `FLIT - EVOLUTION\Sprint 2`, tags `adopcion-ia; DOR`, sin activar
- **Rama:** `feature/AB-11173-11191-rl-ot-mandatarios-y-mandatos` (la misma de los Features #11173/#11174)
- **Insumos del PO:** `C:\Users\USUARIO\Downloads\MandatosFile` — 10 plantillas `.md` (5 OT × persona natural/jurídica) + sus PNG de referencia

Cinco bloques. **A y B tocan lo que acabamos de construir** (panel unificado del representante), **C y D son independientes** entre sí y del resto, y **E es el único reversible por sí solo** — por eso va al final, como pidió el PO.

---

## Bloque A — Representantes legales

### A.1 La identidad vigente aparece como «sin validar»

**Causa raíz encontrada** (`DbLegalRepresentativeReader.LoadIdentityVigenciaAsync:441-457`): la vigencia de identidad
se resuelve **por sujeto**, no por persona:

```csharp
.Where(v => v.SubjectType == AdminIdentitySubjectTypes.LegalRepresentative
    && representativeIds.Contains(v.SubjectRef))
```

La firma del baúl, en cambio, se resuelve **por documento** (`FirmaKey(r)` → `(TenantId, DocumentType, DocumentNumber)`).
Esa asimetría es el bug: una validación aprobada y vigente de la misma persona que se creó bajo otro sujeto
—un mandatario, un actor de trámite, u **otra fila de representante de la misma persona en otra compañía**—
es invisible para el panel, que muestra «Identidad sin validar». El caso reportado (CC 1038409485) encaja.

`admin.admin_identity_validations` ya tiene `document_type` y `document_number` (DDL `40-HU10907`, con RLS por
tenant), así que resolver por persona no exige esquema nuevo.

**Diseño:** `LoadIdentityVigenciaAsync` pasa a resolver por `(tenant_id, document_type, document_number)`, igual
que el baúl, uniendo lo que ya trae por `subject_ref`. La vigencia se calcula con el mismo
`AdminIdentityVigencia.Resumir` (no se toca la regla de negocio, solo el universo de filas que entra).

**Trampa:** el botón «Asociar validación» (HU #11176) sigue siendo necesario — mostrar la identidad no es
lo mismo que **vincularla** al representante, que es lo que actualiza `identity_validation_ref` y la firma. El
panel debe distinguir «hay validación vigente de esta persona» de «está asociada a este representante».

### A.2 Capturar la firma del baúl sin salir del formulario

Hoy `SignatureVaultSelector` (HU #11180) solo **elige** entre firmas existentes; si la persona no tiene ninguna,
el usuario debe irse al Baúl de firmas, crearla y volver. Ya existe `SignatureCapture.tsx` en la misma carpeta y
el endpoint de alta del baúl (`SignatureVaultHandlerTests` cubre alta, hash y vigencia).

**Diseño:** cuando el selector no encuentre firmas para el documento del representante, ofrecer «Capturar firma»
en el mismo panel: imagen + vigencia + hash, tomando **de los datos del representante** el resto (tipo y número
de documento, nombres, NIT de la compañía). Al guardar, la firma recién creada queda preseleccionada y viaja en
`signatureVaultId` — la ruta explícita que ya abrió la HU #11175.

**Trampa (D7 cerrada):** el alta del baúl exige unicidad de firma activa por persona
(`Create_DuplicateActive_TranslatesTo422`). Si ya existe una activa pero **vencida**, se **revoca
automáticamente** y se crea la nueva, dejando el historial trazable. No se rompe la unicidad ni se obliga al
usuario a salir del formulario.

### A.3 Las vigencias se ven un día antes

**Causa raíz encontrada** (`frontend/lib/format/date.ts:24`): `formatFecha` hace `new Date(value)` y luego formatea
en `America/Bogota`. Los campos de vigencia son `DateOnly` en el backend (`DeedResponse.VigenciaDesde/Hasta`), así
que llegan como `"2026-07-01"`; JavaScript los parsea como **medianoche UTC** y, al renderizar en UTC−5, salen
como `2026/06/30`. El formulario de edición sí muestra la fecha real porque el `<input type="date">` recibe la
cadena tal cual.

**Diseño:** `formatFecha` detecta el formato `YYYY-MM-DD` (fecha de calendario, sin instante) y lo formatea sin
conversión de zona; los `timestamptz` siguen convirtiéndose a Bogotá como hoy.

**Alcance real — es global, no solo escrituras:** el mismo `formatFecha` pinta las vigencias del baúl de firmas,
del selector de firma y de la identidad. Todo campo `DateOnly` de la aplicación tiene hoy este corrimiento.

---

## Bloque B — Ruta de registro de trámites (identidad y firma del RL)

### B.1 Enviar la validación de identidad al representante cuando el actor es NIT

**Requisito:** garantizar que la validación se envíe al RL cuando vendedor o comprador es NIT y **no** está
registrado como representante en la configuración de la empresa con escrituras y firma vigente.

**Estado actual:** el resolutor `LegalRepresentativeSignatureResolver` cubre el camino feliz (persona ya
registrada). La ruta de registro no tiene una compuerta que, ante un NIT sin representante utilizable, dispare el
envío del correo de validación al RL declarado en el trámite.

**Diseño:** al resolver los actores del trámite, si el actor es NIT y no hay representante con
(escritura vigente + firma o identidad vigente), se dispara el envío usando `POST identity/send` — el mismo que ya
usan el panel del representante y los mandatarios — y el trámite queda marcado a la espera.

### B.2 Firma a posteriori (pendientes de firma)

Es el punto de mayor calado del documento y **no tiene ningún modelo hoy**.

**Requisito:** si el NIT tiene RL asociado con **identidad y firma del baúl ambas vencidas**, ofrecer marcar el
trámite como «se firmará a posteriori». Al activarlo, el método por defecto pasa a ser validación de identidad y,
cuando esa validación se complete, debe aplicarse **a todos los trámites de esa empresa y ese representante** que
tengan la marca, firmándolos y actualizando la firma activa del RL en la compañía. Las demás rutas no cambian.

**Diseño:**
1. Marca por trámite (`procedure_instances`, columna nueva) + la pareja `(company_tenant_id, representative_id)`
   que identifica el lote.
2. Un proceso de aplicación disparado por la **aprobación de la validación de identidad** de esa persona:
   recorre los trámites marcados, les aplica la firma y actualiza la firma vigente del representante.
3. La cascada de regeneración documental ya existe (`IExpedienteHotDocumentsRegenerator`,
   `InvalidarConsolidados`): al firmarse, los documentos del trámite deben regenerarse para que el sello salga.

**Alcance del lote (D1/D2 cerradas):** solo trámites en **borrador y en subsanación** — los que aún no han salido
hacia el organismo. Los radicados, aprobados y anulados quedan fuera: regenerar un expediente que el OT ya tiene
en su bandeja exigiría además decidir si se le reenvía, y eso no está pedido. Si la validación nunca llega, el
trámite simplemente permanece marcado y sin firmar; la marca es informativa, no habilita radicar sin firma.

**Trampas:** (a) hay que **revalidar el estado en el momento de aplicar**, no al marcar: entre la marca y la
aprobación de la identidad pueden pasar días y el trámite pudo radicarse; (b) es una operación masiva y diferida:
necesita traza por trámite, no un best-effort silencioso
(el `DbSignatureVaultReader` con transacción anidada del Feature #11004 es el precedente de lo que pasa cuando un
best-effort se traga el fallo).

### B.3 Los nombres del RL en los documentos salen del trámite

**Requisito:** los nombres y apellidos del representante legal que aparecen en **todos** los documentos son los
que se registren en el trámite (no los del directorio de la compañía).

**Diseño:** unificar el origen del nombre del mandante/representante en los generadores (mandato, compraventa,
solicitud, FUR) a los datos del actor del trámite, con el directorio como respaldo cuando el trámite no los traiga.

**Trampa:** hay varios generadores y cada uno arma su propio bloque de firma; el cambio debe hacerse en el
constructor de datos común, no generador por generador, o quedará inconsistente entre documentos.

---

## Bloque C — El organismo se decide en el paso 1

**Estado actual verificado:** el organismo se elige en el **último** paso (`FirmaFurStep.tsx:322-344`), con un modal
que se auto-abre; y en traspaso ese selector no se abre porque el OT sale de donde está matriculado el vehículo
(HU #10659). La instancia se crea con `transitOfficeId: null` (`tramites-client.ts`) y el OT se persiste recién en
la transición de radicación (`TramiteLifecycleService.cs:436`), tras validar grant OT↔empresa y operabilidad.

### C.1 Matrícula inicial — seleccionar la secretaría en el paso 1

Selección **obligatoria para habilitar la consulta por VIN**, solo en matrícula inicial; se retira del paso FUR.
El componente debe advertir que solo se listan OT **activos** y qué hacer si el OT no aparece.

Mensaje propuesto:

> **Solo se muestran los organismos de tránsito activos en FLIT.** Si el organismo donde vas a radicar no aparece
> en la lista, solicita al administrador que lo agregue y lo active.

### C.2 Traspaso — validar el organismo en el paso 1

La validación se adelanta al paso de consulta del vehículo:

- OT de matrícula **activo y habilitado** para la compañía gestora ⇒ continuar.
- En caso contrario ⇒ **no permitir continuar**, con este mensaje:

> **No puedes radicar en este organismo de tránsito.** El vehículo está matriculado en un organismo que no está
> activo en FLIT o no está habilitado para tu compañía. Solicita al administrador que lo active y lo habilite
> para tu compañía antes de continuar con el trámite.

**Trampa:** hoy el gate vive en la transición de radicación; adelantarlo al paso 1 **no debe eliminarlo de allí**
(un trámite puede quedar en borrador y cambiar la habilitación antes de radicar). Son dos comprobaciones, no una
mudanza.

**Borradores en curso (D8 cerrada): convivencia.** El requisito del paso 1 aplica a los trámites **nuevos**; los
borradores que ya eligieron OT en el paso FUR lo conservan y pueden seguir editándolo donde ya lo tenían, hasta
que se cierren. No hay migración ni bloqueo de trabajo en curso. Implica que el selector del FUR **no se retira
del código**, solo deja de ofrecerse en los trámites creados a partir del cambio.

---

## Bloque D — Mandatarios: del perfil del OT al configurador de compañías

**Estado actual verificado:** la UI vive en `/admin/transit-offices/[id]/mandatarios`
(`MandatariosSection.tsx`, `MandatarioFormPanel.tsx`). El modelo es `admin.mandate_signers`, llaveado por
`transit_office_id`, con puente `mandate_signer_companies (transit_office_id, company_tenant_id)`. El trámite ya
tiene `procedure_instances.mandate_signer_id`, pero **se resuelve al aprobar** (DDL `42-HU10916`), no al registrar.
ADR-0036 (Aceptado) ya derogó la exclusividad: hay N mandatarios activos por (OT, compañía).

### D.1 El alta la hace la empresa y elige sus OT

La compañía registra los datos del mandatario y marca, de la lista de **OT asignados a esa empresa**, dónde
aplica (se conserva la lógica de checkboxes, invertida: antes el OT elegía compañías).

**Modelo (D3 cerrada): puente `(mandate_signer_id, transit_office_id)`.** `mandate_signers.transit_office_id` es
hoy una sola columna, así que «un mandatario para N OT» significaría N filas duplicadas de la misma persona. Se
mueve la relación a una tabla puente: **una persona, una fila**.

**Lo que arrastra ese cambio** (hay que tocarlo todo en la misma HU o el modelo queda inconsistente):
- `mandate_signer_companies (transit_office_id, company_tenant_id)` — su llave incluye el OT, que deja de vivir
  en el mandatario.
- `signature_vault.mandate_signer_id` (DDL `32-HU10642`) — sigue apuntando a la persona, no cambia, pero su
  lectura por OT sí.
- `procedure_instances.mandate_signer_id` (DDL `42-HU10916`) — la FK se mantiene; lo que cambia es cómo se
  resuelve el candidato para un OT dado.
- La columna `transit_office_id` de `mandate_signers` queda **deprecada y nullable** antes de retirarse, igual
  que se hizo con `represented_company_id` en la HU #10932.

### D.2 Elegir el mandatario al registrar el trámite

Al seleccionar el OT, mostrar los mandatarios habilitados para ese OT, con **nombres, documento y vigencia de su
validación**; si hay uno solo, quedar preseleccionado. Cambiable mientras el trámite esté en **borrador** y haya
más de un mandatario.

**Trampa:** esto adelanta a la creación lo que hoy se resuelve al aprobar. La resolución automática debe quedar
como respaldo para los trámites que no traigan elección explícita, igual que hicimos con el orden del consolidado.

---

## Bloque E — Formatos de mandato por organismo

**Estado actual verificado:** `MandatoPdfGenerator` + `MandatoTemplateResolver` ya soportan tres variantes
(`generico`, `sabaneta`, `bello`) y `admin.transit_office_mandate_config` las configura por OT con un **CHECK
cerrado** (`ck_transit_office_mandate_config_template`), más el mandatario institucional y si exige mandato a
persona natural.

**Lo que aportan los `.md` del PO:** un metadato que el modelo actual no tiene, `familia_mandatario`:

| OT | Familia | Mandatario |
|---|---|---|
| Envigado, Funza, Medellín | `individuo` | Persona natural |
| Bello, Sabaneta | `organismo_transito` | Unión temporal (persona jurídica) |

Es decir, la variante no es «una por municipio» sino **dos familias** más los datos propios del OT (ciudad, razón
social y NIT de la unión temporal, sigla). Funza y Medellín no existen hoy como plantilla y son idénticos a
Envigado salvo la ciudad.

### E.1 Plantillas por OT sin multiplicar el código

**Diseño:** sustituir el CHECK cerrado por la pareja `familia_mandatario` + datos del OT, de modo que dar de alta
Funza y Medellín sea **configuración**, no código. Las diferencias de redacción entre familias siguen en el
generador (son texto legal, no plantillas de datos).

### E.2 Firma manual cuando el mandatario es una empresa

**Requisito:** si el OT tiene empresa relacionada como mandatario ⇒ **firma manual**, sin plasmar firmas de
validación de identidad; si no la tiene ⇒ firma con validación de identidad.

**Contradicción resuelta (D4 cerrada): manda la regla.** Los `.md` de Bello y Sabaneta —los dos OT con unión
temporal— incluyen el bloque «Firmado electrónicamente… Hash: {{hash_mandatario}}» para el mandatario, pero se
consideran **desactualizados en ese punto**: se usan como fuente del **texto legal**, no de la política de firma.

En consecuencia, cuando el OT tenga empresa relacionada como mandatario (hoy `institutional_mandatary_name` en
`transit_office_mandate_config`), el bloque de firma electrónica del MANDATARIO **no se pinta**: queda la línea de
firma manual sobre la cédula. El MANDANTE no cambia — sigue firmando con su validación de identidad.

**Trampa:** la variante `Sabaneta` ya se documentó como «solo firma el MANDANTE», pero la `Bello` como «ambas
partes firman». Con esta decisión, Bello pasa a comportarse como Sabaneta y hay que actualizar la documentación
del generador, que hoy afirma lo contrario.

### E.3 Transformaciones en el contrato

**Requisito del PO:** que el contrato tenga en cuenta las transformaciones del trámite (cambio de color, de
carrocería, de combustible).

**Hallazgo:** **ninguna de las 10 plantillas menciona transformaciones, prendas, color, carrocería ni combustible.**
Las plantillas expresan el objeto del contrato con una sola variable, `{{tramite}}` («TRASPASO DE PROPIEDAD»,
«MATRÍCULA INICIAL»). Así que el requisito **no es tocar las plantillas**: es componer `{{tramite}}` con las
transformaciones asociadas (el wizard ya las captura — `VehicleTransformationsCard.tsx`).

**Redacción (D5 cerrada): dentro del objeto del contrato**, sin cláusula nueva:

> …se hace cargo de la radicación y reclamación del trámite de **TRASPASO DE PROPIEDAD, CAMBIO DE COLOR Y CAMBIO
> DE CARROCERÍA** vehículo de placa: **ABC123**, por cuenta y riesgo del mandante.

Sin transformaciones, el texto queda exactamente como hoy. La composición es una función pura con test (nombre de
la modalidad + transformaciones en mayúscula, separadas por comas y la última con «Y»), y no toca ninguna de las
dos familias de plantilla.

---

## Decisiones cerradas (2026-08-01)

| # | Decisión | Resolución |
|---|---|---|
| D1 | Estados elegibles para la firma a posteriori | **Borrador y subsanación.** Los radicados, aprobados y anulados quedan fuera. El estado se revalida al aplicar, no al marcar |
| D2 | ¿Alcanza expedientes ya entregados al organismo? | **No.** Regenerar lo que el OT ya tiene en bandeja obligaría a decidir el reenvío, y no está pedido. Si la validación nunca llega, el trámite queda marcado y sin firmar |
| D3 | Mandatario para N OT | **Puente `(mandate_signer_id, transit_office_id)`**: una persona, una fila. `mandate_signers.transit_office_id` queda deprecada y nullable antes de retirarse (mismo patrón que la HU #10932) |
| D4 | Bello y Sabaneta: firma manual o electrónica | **Manda la regla.** Con empresa relacionada como mandatario no se plasma firma de validación de identidad; los `.md` son fuente del texto legal, no de la política de firma |
| D5 | Redacción de las transformaciones | **Dentro del objeto del contrato** (`{{tramite}}`), sin cláusula nueva. Sin transformaciones el texto queda igual que hoy |
| D6 | Sección de mandatarios en el perfil del OT | **Se elimina.** Único punto de gestión: el configurador de compañías. Hay que dejar escrito en el PR que revierte una decisión previa de PO |
| D7 | Firma nueva con una activa ya existente | **Revocar y crear, siempre.** Ampliado el 2026-08-01: la última firma capturada sustituye a la anterior **esté vencida o vigente**. La sustituida queda `revocada`, no se borra, así que lo ya firmado con ella sigue siendo trazable. Aplica también al alta desde el Baúl de firmas, que usa el mismo endpoint |
| D8 | Borradores que ya eligieron OT en el paso FUR | **Convivencia.** El requisito del paso 1 aplica a trámites nuevos; los borradores en curso conservan su OT y su selector hasta cerrarse |

---

## Descomposición propuesta

| Feature | HU | ADO | Tipo | Alcance | SP |
|---|---|---|---|---|---|
| A #11187 | A1 | **#11192** | BE | Vigencia de identidad del RL resuelta **por persona** (documento), no por sujeto | 3 |
| A #11187 | A2 | **#11193** | FULLSTACK | Capturar la firma del baúl dentro del panel del representante | 5 |
| A #11187 | A3 | **#11194** | FE | `formatFecha` respeta las fechas de calendario (`DateOnly`) — corrige el corrimiento global | 3 |
| B #11188 | B1 | **#11195** | FULLSTACK | Envío de validación al RL cuando el actor NIT no tiene representante utilizable | 5 |
| B #11188 | B2 | **#11196** | BE/DB | Marca de «firma a posteriori» y aplicación por lote al aprobarse la identidad | 8 |
| B #11188 | B3 | **#11197** | FE | Opción de firma a posteriori en la ruta de registro | 3 |
| B #11188 | B4 | **#11198** | BE | Nombres del RL en los documentos tomados del trámite | 3 |
| C #11189 | C1 | **#11199** | FULLSTACK | Matrícula: secretaría en el paso 1 como requisito de la consulta por VIN | 5 |
| C #11189 | C2 | **#11200** | FULLSTACK | Traspaso: validación del OT en el paso 1, con bloqueo y mensaje | 5 |
| D #11190 | D1 | **#11201** | BE | Mandatario multi-OT desde la compañía (modelo + API) | 5 |
| D #11190 | D2 | **#11202** | FE | Alta de mandatarios en el configurador de compañías con selección de OT | 3 |
| D #11190 | D3 | **#11203** | FULLSTACK | Selección del mandatario al registrar el trámite, editable en borrador | 5 |
| E #11191 | E1 | **#11204** | BE/DB | Familias de plantilla + datos del OT: Funza y Medellín como configuración | 5 |
| E #11191 | E2 | **#11205** | BE | Firma manual vs. validación de identidad según mandatario empresa | 3 |
| E #11191 | E3 | **#11206** | BE | Transformaciones en el objeto del contrato de mandato | 3 |

**Total: 15 HUs, 64 SP.** Orden sugerido: A → C ‖ D → B → E (A son defectos de lo ya construido; E al final por si
hay que revertirlo). Ninguna activada: la activación es gate humano.

---

## Entrega — PR único con excepción del Líder Técnico

La rama ya acumula **67 archivos y ~7.600 líneas** por los Features #11173/#11174, contra el límite FLIT de **800
líneas por PR**; con estos cinco bloques pasa de 15.000.

**Decisión (2026-08-01): un solo PR, con excepción explícita del Líder Técnico** a la regla #9. No se parte en
PRs apilados ni en ramas de entrega por Feature.

Lo que eso exige, para que la excepción sea defendible y el PR revisable:

- **La excepción hay que pedirla, no darla por hecha.** La regla #9 es innegociable salvo que un humano la
  levante; conviene tenerlo por escrito en el work item antes de abrir el PR.
- **Un commit por HU, sin excepciones** (así viene la rama): el revisor puede leer commit a commit en vez de
  enfrentarse al diff completo.
- **Descripción del PR con índice por Feature y por HU**, señalando qué commits tocan esquema y cuáles solo UI.
- **Las reversiones de decisiones de PO se declaran arriba** en la descripción: la sección «Escrituras por
  compañía» (HU #11063) y la de mandatarios en el perfil del OT (D6).
- **El orden de despliegue va en la descripción**: migrar antes de subir el código, o revienta toda consulta a
  `document_types`.

---

## Verificación

- **Backend:** `dotnet test` de `Flit.Admin.Tests`, `Flit.Infrastructure.Tests`, `Flit.Tramites.Application.Tests`.
  Baseline: 3317 verdes / 1 omitido. Los 8 fallos de `Flit.DataMigration.Tests` son preexistentes (CRLF en los golden).
- **Frontend:** `pnpm typecheck` + `vitest`. Baseline 1200 verdes con 5 fallos preexistentes en develop.
- **A3 exige verificación visual**: el corrimiento de fechas no lo detecta ningún test actual porque todos comparan
  la cadena que produce el formateador, no el día calendario esperado. Hay que añadir el test con `DateOnly`.
- **E exige render real** con `services/core-api/artifacts/render-documentos` y contraste contra los PNG del PO.
