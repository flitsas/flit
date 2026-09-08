# ADR-0055: Captura de los dos hechos de prenda (levantamiento + inscripción) en una misma instancia de trámite

**Fecha**: 2026-09-07
**Status**: Aceptado (2026-09-07, Líder Técnico — Willyn Londoño)
**Deciders**: Líder Técnico FLIT, Product Owner (módulo Trámites/Otros Trámites)
**Tags**: arquitectura, backend, frontend, modulo-tramites, modulo-prenda

## Contexto

El WIZARD, dentro de "Otros Trámites", ofrece hoy `PRENDA_INSCRIPCION` (Inscribir prenda) y
`LEVANTAMIENTO_PRENDA` (Levantar prenda) como tipos de UNA sola acción sobre el gravamen. El
negocio pide que, al elegir cualquiera de los dos, el gestor pueda ejecutar TAMBIÉN la acción
complementaria en la MISMA radicación (p. ej.: levantar la prenda de un crédito pagado e inscribir
de una vez la prenda de un crédito nuevo, o viceversa). No es una casilla genérica de "elegir
ambas" en el selector de tipo de trámite: es una capacidad que debe vivir dentro de los flujos de
captura de `PRENDA_INSCRIPCION` y `LEVANTAMIENTO_PRENDA`.

Esto choca con tres decisiones ya tomadas y vigentes en el código:

1. **`decisionFija` (ADR-0050, comentario en `TramiteWizard.tsx` ~L4634-L4645 y
   `PrendaForm.tsx` ~L290-L299)**: cuando un tipo prendario ofrece una sola decisión posible, el
   selector de acción desaparece y `PrendaForm` afirma la decisión en vez de preguntarla. Esto es
   exactamente lo que el nuevo requisito revierte para estos dos tipos: si ahora hay que poder
   ejecutar dos acciones, ya no hay "una sola decisión posible".
2. **Contrato `PUT /instances/{id}/prenda`** (`ProcedureInstanceEndpoints.cs` ~L384-L410,
   `RegistrarPrendaInput`/`RegistrarPrendaHandler` en `PrendaCommand.cs`): persiste **una** cadena
   `Decision` por request. `RegistrarPrendaHandler` versiona por reemplazo — si ya hay una fila
   `vigente`, la marca `reemplazada` y crea una nueva `vigente` — es decir, el modelo asume que solo
   existe **un hecho vigente a la vez**, no que puedan coexistir dos.
3. **Modelo de datos**: `ProcedureInstancePrenda` (`Entities/ProcedureInstancePrenda.cs`) tiene un
   único campo `Decision`, y la tabla `tramites.procedure_instance_prenda`
   (`Ddl/24-HU10585-prenda.sql`) impone `uq_procedure_instance_prenda_vigente` — índice único
   parcial por `procedure_instance_id` WHERE `estado = 'vigente'` — que garantiza **a lo sumo una
   fila vigente por instancia**, sin importar el tipo de acción.

El propio equipo ya identificó este problema y lo dejó pendiente a propósito, con evidencia
explícita en tres sitios:

- `ProcedureTypeLayers.EsPrendaDeAccionUnica` (dominio, ~L58-L65): *"`LEVANTAR_INSCRIBIR_PRENDA` y
  `CAMBIO_ACREEDOR` ejecutan DOS acciones en el mismo trámite (...) y el expediente guarda una sola
  decisión de prenda: cómo se capturan esas dos caras está sin resolver. Ambos están inactivos en
  el catálogo."*
- `wizardCapabilities.decisionesDelTipoDePrenda` (frontend, ~L280-L290): para
  `LEVANTAR_INSCRIBIR_PRENDA`/`CAMBIO_ACREEDOR` devuelve `['levantar', 'registrar']` (dos opciones
  en el selector), pero el comentario admite: *"El asistente captura una decisión por expediente,
  así que el gestor elige cuál declara; el FUR marca la casilla base de todos modos."* — es decir,
  hoy solo se puede declarar UNA de las dos, aunque el tipo exista.
- `Ddl/90-prenda-en-requisitos.sql`: excluye deliberadamente a `LEVANTAR_INSCRIBIR_PRENDA` y
  `CAMBIO_ACREEDOR` de la migración que movió la captura de prenda a Requisitos, *"porque ejecutan
  dos acciones sobre el gravamen y su captura todavía no está resuelta"*.

El catálogo ya tiene sembrado (inactivo, `wizard_enabled = false`) un tipo
`LEVANTAR_INSCRIBIR_PRENDA` (migraciones `20260729120000_CatalogoTiposTramiteCanonico.cs` y
`20260822100000_ParametrizacionCatalogoCompleto.cs`), pensado originalmente como el vehículo para
resolver este mismo problema.

El FUR ya modela la respuesta completa: `PrendaDecision.ToFurMarking` traduce a un
`FurPrendaMarking` con valores `Constitucion | Levantamiento | Ninguna | Ambos`
(`PrendaDecision.cs` ~L88-L102), y `FurFieldMapper.MarkAlertas` (~L363-L370) ya sabe pintar ambas
casillas (11 y 12 / numeral 20) cuando el marking es `Ambos`. Pero **nada produce hoy
`FurPrendaMarking.Ambos`**: `ToFurMarking` recibe una sola `string decision` y por construcción
solo puede devolver uno de los otros tres valores. Hay una desconexión real entre lo que el FUR
puede representar (`Ambos`) y lo que el flujo de captura permite hacer (una decisión).

Fuera de alcance de este ADR: la consulta a RUNT (Kyverum/Verifik/Intempo) y el automapeo de
acreedor/gravamen a Requisitos — funcionan hoy y no cambian.

## Decisión

**Decisión aceptada**: Opción B — extender el agregado de prenda para soportar dos hechos
(levantamiento + inscripción) dentro de la misma instancia, embebidos en los flujos existentes de
`PRENDA_INSCRIPCION` y `LEVANTAMIENTO_PRENDA`, en vez de reactivar `LEVANTAR_INSCRIBIR_PRENDA` como
tercer tipo de trámite. Ver justificación en "Tradeoff aceptado".

**Aceptación**: el Líder Técnico (Willyn Londoño) aceptó la Opción B el 2026-09-07. Backend Agent,
Frontend Agent y Database Agent quedan habilitados para implementar siguiendo las notas operativas
de este ADR.

## Alternativas consideradas

### Opción A — Reactivar `LEVANTAR_INSCRIBIR_PRENDA` como tercer tipo de trámite explícito

Activar (`wizard_enabled = true`) el tipo ya sembrado en el catálogo, con su propio flujo de
captura: dos decisiones explícitas desde el inicio (una para levantar, otra para inscribir), en vez
de "embeber" la acción complementaria dentro de `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA`.

**Pros:**
- Reutiliza directamente el tipo ya modelado en `ProcedureTypeLayers.EsTipoPrendaBase` y en el
  catálogo (`ParametrizacionCatalogoCompleto`) — cero trabajo de esquema de catálogo nuevo.
- El tipo de trámite declara honestamente su naturaleza dual desde la elección inicial: el gestor
  no descubre a mitad de flujo que puede hacer algo más, lo elige explícitamente al radicar.
- No toca el contrato ni el comportamiento de `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA`, que
  siguen siendo de una sola acción — menor riesgo de regresión sobre flujos que ya operan en
  producción.
- Encaja con el patrón ya usado por `CAMBIO_ACREEDOR` (mismo problema, mismo tipo de solución
  pendiente) — resolver ambos con el mismo mecanismo evita dos soluciones distintas al mismo
  problema.

**Contras:**
- **No es lo que pide el negocio**: el requisito es explícito en que la capacidad debe vivir
  "dentro de cada uno de los dos flujos existentes", no como una tercera entrada en el selector de
  tipo de trámite. Habría que negociar el AC con el PO o rechazar la lectura literal del pedido.
  El PO ya redactó el AC bajo la premisa de "elegir Inscribir o Levantar y desde ahí ofrecer la
  complementaria" — introducir un tercer tipo cambia la experiencia que el usuario final espera
  (un gestor que ya sabe que va a "Levantar Prenda" tendría que saber de antemano, antes de
  radicar, que también va a inscribir otra).
- El agregado de prenda (`ProcedureInstancePrenda`) sigue teniendo un solo campo `Decision`: el
  problema de fondo (una fila vigente vs. dos hechos) no se resuelve, solo se traslada a un tipo de
  trámite nuevo con el mismo defecto de origen — habría que resolver el modelo de datos de todas
  formas para que este tercer tipo capture ambos hechos.
- Un tipo de trámite nuevo activo implica: nuevo `gate_profile`, nuevos pasos/secciones en
  `procedure_steps`, nuevas reglas de `ProcedureTypeGateProfile.AdmiteDimensionDePrenda`, ajustar
  `wizardCapabilities.ts` (`esTipoDePrenda`, `decisionesDelTipoDePrenda` ya lo referencian mal —
  devuelven un selector de una sola opción para un tipo que debería exigir las dos), documentos FUR
  y mandato — superficie de cambio comparable o mayor que la Opción B, sin resolver el problema de
  fondo del agregado.
- Fragmenta la experiencia: un gestor que ya está en `LEVANTAMIENTO_PRENDA` y descubre que necesita
  inscribir otra prenda tendría que abandonar el trámite en curso y empezar de nuevo con el tipo
  correcto — contradice el pedido de "en la misma radicación".

**Esfuerzo:** M (activar catálogo es barato; resolver el agregado de datos para que capture dos
hechos sigue siendo necesario y es el grueso del esfuerzo).
**Riesgos:** Divergencia entre lo que el AC pide (embebido) y lo entregado (tipo nuevo) —
alto riesgo de rechazo en aceptación de negocio. Requiere decisión de UX no trivial (¿en qué
momento el gestor sabe que necesita el tipo dual?).

### Opción B — Extender el agregado de prenda para soportar dos hechos en la misma instancia (recomendada)

Revertir `decisionFija` para `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` cuando el gestor active la
acción complementaria, y extender el agregado `ProcedureInstancePrenda` para admitir dos filas
vigentes simultáneas por instancia — una por "acción" (`levantar` y `registrar`/`solicitar`) — en
vez de una sola fila vigente global. El contrato `PUT /instances/{id}/prenda` acepta una lista de
1 o 2 acciones en el mismo request (o dos PUTs idempotentes, uno por acción — ver nota de diseño
API abajo).

**Pros:**
- Es la lectura literal y más fiel del requisito: la acción complementaria vive dentro del flujo
  existente de `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA`, sin introducir un tercer tipo de
  trámite ni cambiar lo que el gestor elige al radicar.
- Resuelve el problema de fondo (no lo traslada): el agregado pasa de "una decisión" a "hasta dos
  hechos por instancia, con roles distintos (`levantar` vs `registrar`)", que es exactamente la
  brecha que documentan `ProcedureTypeLayers`, `decisionesDelTipoDePrenda` y
  `90-prenda-en-requisitos.sql`.
- Cierra la desconexión ya detectada entre `FurPrendaMarking.Ambos` (que el FUR sabe representar) y
  `PrendaDecision.ToFurMarking` (que nunca lo produce): con dos hechos vigentes, `ToFurMarking`
  puede evolucionar a una función que reciba el conjunto de decisiones vigentes y sí devuelva
  `Ambos` cuando corresponda.
- Deja el camino allanado para que `LEVANTAR_INSCRIBIR_PRENDA`/`CAMBIO_ACREEDOR` (si en el futuro
  se activan) reutilicen el mismo agregado de dos hechos, en vez de inventar un segundo mecanismo.
- El acreedor y el certificado de cada hecho quedan asociados a su propia fila (`AcreedorNombre`,
  `AcreedorDocumento`, `LevantamientoEntidad`, `DocTipo` del documento requerido), evitando
  colisiones cuando el acreedor del crédito que se levanta es distinto del acreedor del crédito que
  se inscribe — un caso que el negocio describe explícitamente en el ejemplo (crédito pagado vs.
  crédito nuevo, casi siempre con acreedores distintos).

**Contras:**
- Requiere migración de esquema (raw SQL, patrón `Ddl/NN-*.sql` + `[Migration]` C# que la envuelve,
  igual que `24-HU10585-prenda.sql`): reemplazar el índice único parcial
  `uq_procedure_instance_prenda_vigente(procedure_instance_id) WHERE estado='vigente'` — que hoy
  garantiza una sola fila vigente por instancia — por una restricción que permita hasta dos
  vigentes por instancia pero nunca dos con la MISMA "acción" (`levantar`/`registrar`+`solicitar`).
  Esto es dominio del `database-agent`, pero el diseño conceptual (una columna nueva `accion` o
  reutilizar `decision` mapeada a acción) es responsabilidad de este ADR.
- `RegistrarPrendaHandler` deja de ser "reemplaza la vigente" y pasa a "reemplaza la vigente DE ESA
  ACCIÓN" — el versionado (R17, HU #10599) debe re-scopearse por acción, no globalmente. Riesgo de
  regresión sobre matrícula/traspaso, que siguen usando el modelo de una sola decisión vigente y NO
  deben verse afectados por este cambio (matrícula/traspaso no admiten "ambos": ahí `decisiones`
  sigue siendo una elección excluyente entre `solicitar|registrar|levantar|omitir|sin_prenda`).
- El contrato `PUT /instances/{id}/prenda` cambia de forma observable para el frontend (de "una
  decisión" a "una decisión, con la posibilidad de declarar la complementaria") — exige coordinar
  versión de contrato con Frontend Agent y, si aplica, documentarlo en
  `contracts/openapi/core-api.v1.yaml` (hoy el endpoint de prenda NO está documentado en el
  contrato OpenAPI — gap preexistente que debe cerrarse en la misma HU, independiente de la opción
  elegida).
- `PrendaForm.tsx` y `TramiteWizard.tsx` requieren rediseño de UI: dejar de ocultar el selector con
  `decisionFija` para estos dos tipos y añadir la superficie de "declarar también la acción
  complementaria" (checkbox u opción secundaria) con su propio subformulario de acreedor/documento.
- `PrendaDecision.ToFurMarking` (y su consumidor en `FurFieldMapper`) deben migrar de "traducir una
  decisión" a "traducir un conjunto de hechos vigentes" — cambio de firma que toca tests existentes
  (`PrendaDecisionFurMarkingTests.cs`, `FurPrendaMarkingTests.cs`).

**Esfuerzo:** M-L (dominio + infraestructura + 2 frontends: PrendaForm y TramiteWizard; migración
de datos con `database-agent`).
**Riesgos:** Romper el invariante "una vigente por instancia" en matrícula/traspaso si el cambio de
esquema no queda correctamente acotado a `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` (regla de
negocio, no solo de esquema — requiere gate explícito en el handler, no solo en el índice). Requiere
tests de regresión exhaustivos sobre R4/R10/R17 (HU #10585/#10595/#10599) y HU #10881 (gate de
`omitir`).

### Opción C — Prenda complementaria como "hecho" independiente fuera del agregado versionado (evento append-only)

En vez de extender `ProcedureInstancePrenda` para tener hasta dos filas "vigentes", separar
completamente el modelo: la tabla actual sigue registrando LA decisión primaria del tipo (la que
ya decide `decisionFija` hoy, sin tocarla), y se crea una tabla nueva, p. ej.
`procedure_instance_prenda_complementaria`, append-only (sin versionado "vigente/reemplazada"), que
registra el hecho adicional cuando el gestor lo activa.

**Pros:**
- Cero riesgo sobre el modelo actual de matrícula/traspaso y sobre el versionado R17: la tabla
  `procedure_instance_prenda` y `RegistrarPrendaHandler` no cambian en absoluto.
- Migración de esquema más simple: tabla nueva con su propio índice único
  `(procedure_instance_id)` (a lo sumo un complementario por instancia, sin necesidad de un
  concepto de "acción" cruzado con la tabla principal).
- Rollback trivial: si el negocio da marcha atrás, se elimina la tabla nueva sin tocar la tabla
  base ni las migraciones ya aplicadas de prenda.

**Contras:**
- Dos tablas para un mismo concepto de dominio (un gravamen) es una duplicación de modelo que
  complica cualquier lectura agregada ("¿qué decisiones de prenda tiene esta instancia?") — hay que
  hacer `UNION` o exponer dos endpoints/DTOs en vez de uno solo, y el FUR (`FurDocumentData`) tiene
  que ensamblar `Ambos` combinando dos fuentes en vez de leer un único agregado.
  cuando el negocio pida lo mismo para `CAMBIO_ACREEDOR` u otro tipo dual, tocaría decidir otra vez
  si usa esta tabla complementaria o inventa una tercera — no generaliza tan bien como Opción B.
- No resuelve la deuda documentada en `ProcedureTypeLayers`/`decisionesDelTipoDePrenda`: dejaría el
  comentario "cómo se capturan esas dos caras está sin resolver" parcialmente vigente para el caso
  general (`LEVANTAR_INSCRIBIR_PRENDA`), aunque sí resuelve el caso puntual pedido aquí.
- El versionado post-registro (R17) del hecho complementario queda sin resolver explícitamente:
  ¿se puede modificar? ¿se re-versiona igual que la tabla principal? Requeriría duplicar la lógica
  de `RegistrarPrendaHandler` en un segundo handler, con el riesgo de que diverjan con el tiempo.

**Esfuerzo:** M (tabla nueva + handler nuevo + UI nueva; evita tocar el handler existente pero no
evita tocar `PrendaForm.tsx`/`TramiteWizard.tsx` ni `FurFieldMapper`).
**Riesgos:** Deuda técnica de "dos tablas para un concepto" que probablemente haya que consolidar
más adelante cuando se resuelva `LEVANTAR_INSCRIBIR_PRENDA`/`CAMBIO_ACREEDOR` — dos migraciones en
vez de una a mediano plazo.

## Tradeoff aceptado

Se recomienda la **Opción B** porque es la única de las tres que (a) cumple literalmente el
requisito de negocio — la acción complementaria vive dentro de `PRENDA_INSCRIPCION`/
`LEVANTAMIENTO_PRENDA`, no en un tercer tipo — y (b) resuelve la deuda de fondo ya documentada por
el propio equipo en tres lugares distintos del código (`ProcedureTypeLayers`,
`decisionesDelTipoDePrenda`, `90-prenda-en-requisitos.sql`), en vez de trasladarla o duplicarla.
Se acepta el costo de tocar el versionado de `RegistrarPrendaHandler` y el índice único parcial
porque es exactamente el punto donde vive la limitación actual ("a lo sumo una vigente por
instancia", sin distinguir acción): no hay forma de resolver el requisito sin tocar ese invariante.

Se descarta la Opción A porque introduce un tipo de trámite nuevo sin resolver el problema de
fondo del agregado (seguiría necesitando la Opción B o C para que ese tercer tipo capture
realmente dos hechos) y porque contradice la instrucción explícita del negocio de que la capacidad
sea "embebida", no una tercera entrada en el selector.

Se descarta la Opción C porque introduce una segunda tabla para el mismo concepto de dominio
(gravamen), lo que complica las lecturas agregadas (FUR, panel de trámite) y no generaliza al caso
`CAMBIO_ACREEDOR`/`LEVANTAR_INSCRIBIR_PRENDA` sin una migración adicional futura — sería resolver
el problema dos veces.

**Punto de escalamiento explícito al Líder Técnico**: si el negocio confirma que prefiere que la
capacidad dual sea un tipo de trámite separado y explícito desde la radicación (Opción A) —p. ej.
por razones de reporting/BI donde "trámite dual" necesita ser un valor distinguible de
`ProcedureTypeId` y no un detalle interno del agregado de prenda—, esa es una decisión de producto
que este ADR no puede tomar por sí solo y debe resolverse antes de construir.

## Consecuencias

### Lo que se gana
- El requisito de negocio queda satisfecho sin abandonar los dos tipos de trámite que el usuario ya
  conoce y usa.
- El agregado de prenda queda modelado para "hasta N hechos por instancia, uno por acción", lo que
  también destraba `CAMBIO_ACREEDOR`/`LEVANTAR_INSCRIBIR_PRENDA` a futuro con el mismo mecanismo.
- Se cierra la desconexión entre `FurPrendaMarking.Ambos` (ya existente, sin productor) y el flujo
  de captura real.

### Lo que se pierde
- El invariante simple "una fila vigente por instancia" deja de ser válido sin matices: pasa a "una
  vigente por instancia y por acción, y nunca más de dos acciones vigentes en total" — más
  complejidad conceptual y de tests de regresión.
- `decisionFija` dejará de aplicar para `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` en el caso dual,
  reintroduciendo parcialmente el selector que ADR-0050 había retirado — debe convivir con cuidado
  con la razón original de ADR-0050 (no preguntar lo que ya se sabe).

### Cambios operacionales
- Nueva migración DDL cruda (`database-agent`) sobre `tramites.procedure_instance_prenda`.
- Cambio de contrato en `PUT /instances/{id}/prenda` — requiere coordinación de versión con
  Frontend Agent y actualización de `contracts/openapi/core-api.v1.yaml` (que hoy no documenta este
  endpoint; oportunidad de cerrar ese gap en la misma HU).
- Tests de regresión obligatorios sobre R4/R10/R17 y HU #10881 antes de aceptar el cambio.

## Impacto por capa (detalle para las notas operativas)

### Dominio (`Flit.Tramites.Domain`)
- `ProcedureInstancePrenda`: agregar campo que distinga la "acción"/rol del hecho dentro de la
  instancia (p. ej. `Accion` con valores `principal | complementaria`, o reutilizar semántica de
  `Decision` agrupando `solicitar|registrar` bajo "constitución" y `levantar` bajo "levantamiento" y
  validando que no puedan coexistir DOS filas vigentes de la MISMA familia).
- `PrendaDecision`: mantiene el conjunto cerrado de decisiones; se añade una función que, dado el
  CONJUNTO de decisiones vigentes de una instancia (no una sola), determine si aplica
  `FurPrendaMarking.Ambos`.
- `ProcedureTypeLayers.EsPrendaDeAccionUnica`: para `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` deja
  de ser estrictamente "acción única" cuando el gestor activa la complementaria — requiere una
  nueva pregunta explícita, p. ej. `PermiteAccionComplementaria(code)`, en vez de sobrecargar
  `EsPrendaDeAccionUnica` con una excepción.
- Regla de negocio explícita y testeada: matrícula y traspaso NO admiten dos hechos vigentes
  (siguen siendo de una sola decisión, `solicitar|registrar|levantar|omitir|sin_prenda`
  mutuamente excluyentes) — el "dual" es exclusivo de `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA`.

### Aplicación (`Flit.Tramites.Application`)
- `RegistrarPrendaInput`/`RegistrarPrendaHandler` (`PrendaCommand.cs`): evolucionar para aceptar
  1 o 2 hechos por request, o admitir dos llamadas idempotentes al mismo endpoint (una por acción) —
  decisión de diseño de API a cerrar con Backend Agent; se recomienda explorar primero "dos PUTs,
  uno por acción" antes de un payload de lista, porque conserva compatibilidad de contrato con los
  consumidores actuales de un solo `Decision`.
- `GetPrendaVigenteHandler`: pasa de devolver `PrendaDto?` (una decisión) a devolver una colección
  de 0-2 `PrendaDto` vigentes por instancia — cambio de contrato de lectura, rompe compatibilidad
  binaria con el consumidor actual del frontend (mitigar con endpoint nuevo o versión de DTO).
- Versionado (reemplazo de "vigente" a "reemplazada"): re-scopear por acción, no por instancia.

### Infraestructura (`Flit.Infrastructure`)
- Nueva migración SQL cruda (patrón `Ddl/NN-*.sql` + wrapper `[Migration]`, igual que
  `24-HU10585-prenda.sql`/`90-prenda-en-requisitos.sql`/`91-prenda-entidad-levantamiento.sql`):
  reemplazar `uq_procedure_instance_prenda_vigente(procedure_instance_id) WHERE estado='vigente'`
  por un índice que permita hasta dos vigentes por instancia pero nunca dos de la misma familia de
  acción (p. ej. índice único parcial sobre `(procedure_instance_id, accion_familia)` donde
  `accion_familia` derive de `decision`).
- `ProcedureInstancePrendaConfiguration.cs`: la entidad sigue `ExcludeFromMigrations()` (DDL
  gestionado a mano) — el `database-agent` debe seguir ese mismo patrón, no introducir una migración
  EF Core generada automáticamente para esta tabla.
- Actualizar `FlitDbContextModelSnapshot.cs` si el `database-agent` decide que la nueva columna sí
  debe reflejarse en el modelo EF (columnas sí se mapean aunque el DDL sea manual).

### Frontend (`frontend/`)
- `PrendaForm.tsx`: para `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA`, mantener `decisionFija` como
  comportamiento por defecto (afirmación, sin selector) pero añadir una superficie explícita
  ("¿También necesitas levantar/inscribir otra prenda en esta radicación?") que, al activarse,
  despliega el subformulario de la acción complementaria (acreedor, documento, y
  `levantamientoEntidad` si aplica).
- `TramiteWizard.tsx` (~L4634-L4680): el bloque que hoy monta un único `PrendaForm` con
  `decisionesDelTipo` fijo debe poder montar el subformulario complementario condicionalmente, sin
  reintroducir el selector genérico de "elegir ambas" en el tipo de trámite (eso vive dentro del
  paso, no en la elección de tipo).
- `wizardCapabilities.ts`: `decisionesDelTipoDePrenda` deja de aplicar tal cual a
  `LEVANTAR_INSCRIBIR_PRENDA`/`CAMBIO_ACREEDOR` (fuera de alcance, siguen inactivos); para
  `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` se necesita una función nueva que indique si el tipo
  admite acción complementaria y cuál es.
- `tramites-client.ts`: adaptar `getPrenda`/`putPrenda` (o los nombres reales del cliente) al nuevo
  contrato (lista de 0-2 hechos vigentes).

## ADRs relacionados

- ADR-0050 (`ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md`) — origen de
  `decisionFija`/`EsPrendaDeAccionUnica`/`AdmiteDimensionDePrenda`: este ADR-0055 NO lo contradice
  en la familia MATRICULAS/TRASPASO (que sigue con una sola decisión excluyente), pero SÍ acota una
  excepción explícita para `PRENDA_INSCRIPCION`/`LEVANTAMIENTO_PRENDA` en la familia OTROS. Si se
  acepta, debe registrarse como excepción documentada en `ProcedureTypeLayers`, no como reversión
  silenciosa de ADR-0050.
- Feature #10585 / HU #10594-#10599 / HU #10881 — cimiento del dominio de prenda (R4/R10/R17,
  gate de `omitir`) cuyo versionado por reemplazo este ADR debe re-scopear sin romper.
- HU #11257 (Feature #11254) — introdujo `FurPrendaMarking`/`ToFurMarking`; este ADR es quien por
  fin da un productor a `FurPrendaMarking.Ambos`.

## Notas para agentes

- **Database Agent**: diseñar el índice único parcial que reemplaza a
  `uq_procedure_instance_prenda_vigente` para permitir hasta dos vigentes por instancia sin
  duplicar acción, siguiendo el patrón de migración SQL cruda ya usado en
  `Ddl/24-HU10585-prenda.sql`/`90-prenda-en-requisitos.sql` (no generar migración EF automática
  para esta tabla — sigue `ExcludeFromMigrations()`). Validar contra
  `checklist-validacion-schema.md` §A (especialmente A12 nomenclatura de índices y A17
  reversibilidad).
- **Backend Agent**: no implementar hasta que el ADR pase a `Aceptado`. Al implementar: re-scopear
  el versionado de `RegistrarPrendaHandler` por acción, no por instancia; mantener el gate
  `TramiteEstadoErrores.EstadoFinal` y el gate de `omitir` (HU #10881) intactos; NO tocar el
  comportamiento de matrícula/traspaso (siguen siendo de una sola decisión).
- **Frontend Agent**: no implementar hasta `Aceptado`. Al implementar: preservar `decisionFija`
  como comportamiento por defecto de `PrendaForm` y añadir la superficie de acción complementaria
  como una extensión, no como reemplazo del patrón de ADR-0050.
- **QA Agent**: casos de prueba deben cubrir explícitamente que matrícula y traspaso NO admiten dos
  hechos vigentes (regresión de ADR-0050/R4/R10/R17), y que el FUR marca correctamente `Ambos`
  cuando corresponde (numeral 20, casillas 11/12).
- **Security Agent**: revisar que el nuevo shape del contrato `PUT /instances/{id}/prenda` no
  exponga PII de acreedor de un hecho al leer el otro (dos acreedores distintos en la misma
  instancia); confirmar que `@pii:medium` en `acreedor_documento` se mantiene en cualquier columna
  nueva equivalente.
- **Infra Agent**: sin impacto de despliegue distinto al de cualquier migración de esquema
  estándar (mismo pipeline que HU #10585).

## Referencias externas

- `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` — fuente normativa de las casillas del numeral 3 y
  20 del FUR que este ADR debe respetar al producir `FurPrendaMarking.Ambos`.
