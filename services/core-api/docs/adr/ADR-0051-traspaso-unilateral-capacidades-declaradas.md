# ADR-0051 — TRASPASO_UNILATERAL: separar sus diferencias con TRASPASO_STANDARD en capacidades declaradas del `gate_profile`

- **Estado**: Propuesto · 2026-08-26
- **Módulo**: Trámites — Traspaso (TR), leasing, conformación dinámica (`Flit.Tramites.Domain`,
  `Flit.Tramites.Application`, `Flit.Infrastructure/Documents`, `Flit.Infrastructure/Persistence`,
  `frontend/components/operacion`)
- **Feature/Bug**: `TRASPASO_UNILATERAL` se comporta de forma incompatible entre el FUR real y su
  vista previa, y entre los distintos gates del ciclo de vida, porque cada archivo decide "¿es
  traspaso?" con un criterio distinto
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, frontend, tramites, parametrizacion, traspaso, leasing

## Contexto

`TRASPASO_UNILATERAL` pertenece a la familia `TRASPASO` (leasing: el locatario formaliza el traspaso
a su nombre) pero difiere de `TRASPASO_STANDARD` en seis dimensiones a la vez — quién se captura por
formulario, quién firma, quién valida identidad, si se autogenera compraventa, si hay bloque de
avalúo. Hoy **ninguna de esas seis preguntas se responde desde el tipo**: cada archivo la responde por
su cuenta, con **cuatro criterios distintos e incompatibles entre sí**, verificados contra el código:

| Criterio | Dónde | Resultado para `TRASPASO_UNILATERAL` |
|---|---|---|
| Igualdad exacta con `TRASPASO_STANDARD` (`TramiteTipologiaCatalog.CodigoTraspasoStandard`) | `FurCommand.cs:156,214,243,264,292,401,643-645,809-811,904,1598`; `GenerarImprontaAttachmentCommand.cs:63-101`; `PreflightCommand.cs:1056-1059` | **`false`** — se comporta como matrícula: solo comprador, sin compraventa, sin avalúo |
| `instance.Family == ProcedureFamily.Traspaso` | `BiometricaCommand.cs:366-368`; `TramiteFirmaAplicador.cs:46-56`; `FinalizeDraftProcedureInstanceCommand.cs:34-64`; `IdentityValidationCompletedConsumer.cs:63-68`; `ListProcedureInstancesQuery.cs:334-347,357,381`; `TramiteCambioEstadoEmailProjector.cs:27-44` | **`true`** — se comporta como `TRASPASO_STANDARD` completo: exige comprador **y** vendedor |
| Heurística de texto `Contains("TRASPASO")`, con respaldo en `FurDocumentData.RequiereVendedor` | `FurFieldMapper.cs:17,429-432` (más el hueco de `FurFieldMapper.cs:91-97`, ver más abajo) | **`true`** por el `Contains`, y de forma **inconsistente** por parte: pinta datos de comprador y vendedor, pero el sello de firma del comprador queda **siempre** condicionado a `"comprador"` sin mirar si el tipo lo exige |
| `if` hardcodeado `IsUnilateral(code)` | `FurPreviewSample.cs:141,183-184` | **`true`** — el simulador SÍ sabe que este tipo omite la firma del comprador, pero es el **único** archivo que lo sabe |

El resultado es exactamente lo que ADR-0050 quiso evitar: el mismo trámite se conforma de tres formas
distintas según qué archivo lo mire. El síntoma más visible es que **la vista previa del FUR
(`FurPreviewSample`) y el FUR real (`FurCommand` + `FurFieldMapper`) se contradicen para el mismo
tipo** — el simulador omite la firma del comprador; el generador real, dependiendo del punto, o exige
comprador y vendedor (vía `BiometriaGateOk`/`BiometricaCommand`) o solo comprador (vía `esTraspaso`
de `FurCommand`), pero nunca "solo vendedor", que es lo que el negocio validó.

### Hallazgo adicional: el `gate_profile` ya sembrado de `TRASPASO_UNILATERAL` tampoco coincide con el comportamiento validado

`services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/82-parametrizacion-catalogo-completo.sql:44`
siembra hoy:

```json
{"entryMode":"PLATE","requiresBuyer":true,"requiresCommercialValue":true,"commercialValueSource":"FASECOLDA","requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true,"validateOtOperability":true,"simitMode":"INTERNAL"}
```

con el comentario explícito "**Unilateral: no comparece el vendedor; de ahí que no exija parte
saliente**". Ese seed es una **base técnica sin validar** (el propio archivo lo declara en su
cabecera: "esto es una base técnica, no un diseño funcional validado") y quedó **al revés** de lo que
este ADR fija: hoy `requiresSeller` está ausente y `biometricActors` trae `["BUYER"]` (valida
identidad el comprador, no el vendedor); `requiresCommercialValue`/`commercialValueSource` declaran
avalúo Fasecolda que el negocio dijo que NO aplica a este tipo. Este ADR corrige ese seed como parte
de su alcance — ver §Tabla de llaves.

### Hallazgo adicional: sincronizar al vendedor desde el RUNT no tiene, hoy, de dónde traer nombre/dirección/ciudad/teléfono

Se verificó el DTO real de la consulta de vehículo por placa
(`Flit.Tramites.Application/UseCases/Consultations/KyverumRuntVehicleResponse.cs`): el proveedor
Kyverum **solo** expone `tipoDocPropietario` (tipo de documento). Ni Kyverum ni el mapeador
equivalente de Verifik hidratan `owner_document_number`, `owner_full_name`, `owner_address`,
`owner_city` ni `owner_phone` — esos dos últimos, junto con el nombre y la dirección, **no existen
hoy en ningún `HydratedField` de la cadena de consulta**. Lo que sí existe:

- `owner_document_type` / `owner_document_number` — los teclea el gestor en el paso 1 y se persisten
  tal cual (`CreateFromConsultaCommand.cs:261-263`), no vienen del RUNT.
- El resto de datos del vendedor (nombre, dirección, ciudad, teléfono, correo) hoy los captura el
  gestor a mano en `ActorsForm`, con el documento pre-sembrado desde esos dos campos
  (`TramiteWizard.tsx`, comentario "El documento se siembra desde el propietario que devolvió el
  RUNT").

Por tanto, "sincronizar el actor vendedor desde el RUNT" para `TRASPASO_UNILATERAL` **no es leer un
dato que ya existe**: es una pieza nueva que reutiliza el documento ya teclead o en el paso 1 para
disparar un lookup de persona **best-effort** — el mismo patrón que ya usa
`RuntPersonLookupHandler` (proveedor `kyverum_runt_conductor`) para autopoblar el comprador en
matrícula, hoy usado solo bajo demanda desde un formulario que aquí no existe — y persistir lo que
resuelva directamente como fila de `instance.Actors`, no como `field_values`. Esto se documenta como
tal en la Decisión 5, con su riesgo abierto explícito (nombre/dirección pueden no resolver).

## Decisión

**El `gate_profile` del tipo declara las seis dimensiones que hoy están bifurcadas por código o por
familia.** Se añaden cuatro llaves nuevas (`sellerCapturedViaForm`, `signatureActors`,
`generatesSaleDocument`, `hasAppraisalBlock`) y se corrige el consumo de una llave que **ya existe**
pero que dos archivos ignoran (`biometricActors`, en `BiometricaCommand.cs` y `FurCommand.cs`). Cada
archivo de la tabla del §Contexto deja de preguntar "¿es traspaso?" por código, familia o texto, y
pasa a preguntarle al perfil "¿esta capacidad aplica?". El seed de `TRASPASO_UNILATERAL` se corrige en
el mismo cambio para declarar el comportamiento que el negocio validó (§Tabla de llaves).

La reconciliación entre "parte vendedora existe en el FUR" (`requiresSeller`, sin cambios) y "esa
parte se captura por formulario" (`sellerCapturedViaForm`, nueva) es la pieza central: hoy una sola
llave gobernaba ambas preguntas y por eso no podían responderse distinto para el mismo tipo.

## Alternativas consideradas

### Opción 1: Centralizar la bifurcación en un único helper, sin tocar el `gate_profile`

Crear un método único (p. ej. `ProcedureClassification.EsUnilateral(instance)`) y hacer que los ~14
puntos de la tabla lo llamen en vez de recalcular cada uno su propio criterio.

**Pros:**
- Cambio pequeño y localizado; una PR, sin migración de datos.
- Corrige de inmediato la contradicción entre FUR real y vista previa.

**Contras:**
- No escala: el próximo tipo con una combinación distinta de estas mismas seis dimensiones (y ya hay
  `TRASPASO_TRANSFERENCIA_DE_DOMINIO` sembrado con su propio perfil incompleto) vuelve a necesitar un
  `if` nuevo en los mismos ~14 archivos.
- Contradice directamente ADR-0050 (Aceptado en su decisión, aunque el documento siga en Propuesto):
  "el tipo de trámite es la única fuente de verdad de la conformación", no un catálogo de `if`
  centralizados por código.
- El propio código ya demuestra el costo de este camino: `TramiteTipologiaCatalog`,
  `ProcedureFamily.Traspaso` y `Contains("TRASPASO")` son tres versiones de exactamente este helper,
  escritas en momentos distintos, y ninguna coincide con las otras dos.

**Esfuerzo:** S · **Riesgos:** resuelve el síntoma (la contradicción) y dentro de un ciclo vuelve a
necesitar otro `if` centralizado para el siguiente tipo.

### Opción 2: Sacar `TRASPASO_UNILATERAL` de la familia `TRASPASO`

Reclasificarlo como su propia familia o dentro de `OTROS`, ya que "no comparece el vendedor" (la
premisa — incorrecta — del seed actual) lo acerca más a un trámite de un solo actor.

**Pros:**
- Evita la ambigüedad "es traspaso pero no como los otros traspasos".
- La familia `OTROS` ya tiene el patrón de un solo actor persistido como `comprador`.

**Contras:**
- Es la premisa **equivocada**: el negocio validó que SÍ hay parte vendedora — solo que no se captura
  por formulario. Reclasificar por familia resolvería el síntoma equivocado.
- Rompe la sección `vehicle_owner_*` del FUR, que es genuinamente de traspaso (transferencia de
  propiedad), no de `OTROS` (novedad sobre un vehículo del mismo titular).
- Regresión directa sobre trabajo reciente: el PR #304 (`0b41f456`, mergeado esta semana) corrigió
  los reportes ICT para que agrupen por familia — mover `TRASPASO_UNILATERAL` de familia rompe esa
  agrupación otra vez, para un caso que además ya está en producción de reportes.
- Exige migración de catálogo y de todo lo que ya distingue por `family = TRASPASO` (7 de los 14
  puntos de la tabla), un blast radius mayor que el problema que resuelve.

**Esfuerzo:** L · **Riesgos:** alto — cambia el eje de clasificación (family) para resolver algo que
es una cuestión de capacidad, exactamente la confusión que ADR-0050 ya cerró para otros casos.

### Opción 3: Capacidades declaradas en el `gate_profile`, extendiendo el patrón de ADR-0050 (elegida)

Añadir `sellerCapturedViaForm`, `signatureActors`, `generatesSaleDocument`, `hasAppraisalBlock`; y
corregir dos archivos (`BiometricaCommand.cs`, `FurCommand.cs`) que ya podrían leer `biometricActors`
del perfil y no lo hacen.

**Pros:**
- Un tipo nuevo con otra combinación de estas seis dimensiones (y las hay: `PRENDA_INSCRIPCION`,
  `CAMBIO_LOCATARIO` y ahora `TRASPASO_TRANSFERENCIA_DE_DOMINIO` son candidatos reales) se resuelve
  con datos de catálogo, no con código.
- Reutiliza tres patrones que YA existen y funcionan en `ProcedureTypeGateProfile`: el par
  declaración+resolver de `AllowsComplementaryTransformations`/`ComplementaryTransformationsAllowed`
  (para `generatesSaleDocument`/`hasAppraisalBlock`), la traducción de vocabulario de catálogo
  `RuntConsultaExigida.ActorTypeDeEntidad` (para `signatureActors`), y el helper
  `ActorsCommand.RolesQueValidanIdentidad` que YA lee `biometricActors` correctamente — solo faltaba
  que `BiometricaCommand.cs` y `FurCommand.cs` lo consultaran en vez de recalcular por familia/código.
- Mantiene `TRASPASO_UNILATERAL` en la familia `TRASPASO` (no rompe ICT, FUR, ni lo ya sembrado).
- Corrige de paso un bug latente sobre `TRASPASO_TRANSFERENCIA_DE_DOMINIO`, que hoy pierde firma y
  compraventa de vendedor por el mismo motivo (igualdad exacta de código) — ver §Consecuencias.

**Contras:**
- Alcance más grande que la Opción 1: toca ~14 archivos backend, 2 de frontend, y el seed del catálogo.
- Introduce una pieza técnica nueva sin precedente directo (sincronizar el actor vendedor desde el
  RUNT) cuyo dato de origen (nombre/dirección/teléfono) hoy no existe en la respuesta del proveedor
  — riesgo que se documenta abierto, no resuelto, en la Decisión 5.

**Esfuerzo:** M · **Riesgos:** medio, concentrado en la Decisión 5 (sincronización best-effort) y
acotado por la reutilización de patrones ya probados en el resto de la decisión.

## Tradeoff aceptado

Se elige la Opción 3 porque es la única que no vuelve a necesitar un `if` por código en el próximo
tipo que combine estas seis dimensiones de otra forma — que es, literalmente, lo que ya pasó tres
veces (`TramiteTipologiaCatalog`, `Family == Traspaso`, `Contains("TRASPASO")`) para el mismo par de
tipos. La Opción 1 es más barata pero dentro de un ciclo dos vuelve a haber tres criterios distintos;
la Opción 2 ataca el eje equivocado (familia en vez de capacidad) y arriesga trabajo reciente de ICT.

Se acepta a cambio un alcance de ~16 archivos y una pieza nueva sin dato de origen garantizado
(sincronización del vendedor), cuyo riesgo se acota explícitamente como best-effort y con reveal de
formulario cuando falta el dato — no se disfraza de "resuelto".

## Relación con ADRs previos

- **ADR-0050** (`ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md`, Propuesto) — establece que
  `procedure_types.gate_profile` es la única fuente de verdad de la conformación. Este ADR **aplica
  esa decisión** a seis dimensiones que hoy siguen bifurcadas por código o por familia pese a que el
  motor dinámico y `ProcedureTypeGateProfile` ya existen y corren en producción; no la reabre.
- **ADR-0035** (`ADR-0035-compraventa-autogenerada-siempre-y-sin-membrete.md`, Aceptado) — fijó que la
  compraventa del sistema se autogenera **SIEMPRE en traspaso**. Este ADR **matiza** esa afirmación:
  "siempre en traspaso" pasa a leerse "siempre que el tipo declare `generatesSaleDocument`" (default
  `true` para todo tipo de la familia `TRASPASO` con parte vendedora, que es exactamente lo que
  `TRASPASO_STANDARD` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO` siguen teniendo hoy sin cambio de
  comportamiento). `TRASPASO_UNILATERAL` es la primera excepción declarada porque no hay compraventa
  entre dos partes: el locatario ya tenía el vehículo por contrato de leasing.
- **ADR-0031** (`ADR-0031-compraventa-autogenerada-firmada-identidad-fur.md`, Aceptado) — la condición
  de autogeneración que fijó (`!TieneCompraventaDelUsuario`) ya fue superseded por ADR-0035; este ADR
  no la vuelve a tocar. Sí hereda su decisión de pintar firmas desde `SellosIdentidad`/`FirmaImagenes`
  por rol — ahora esos roles vienen de `signatureActors`, no de `esTraspaso`.
- **ADR-0029** (`ADR-0029-avaluo-comercial-multiproveedor.md`, Propuesto) — introdujo
  `IAvaluoProvider`/`GetSuggestedCommercialValueHandler` para el paso comercial del wizard. Este ADR
  **matiza** el punto de invocación documental: `FurCommand.cs:401` llama `BuildAvaluoAsync` con
  `esTraspaso` como guardia; pasa a usar `hasAppraisalBlock`, default `true` para
  `TRASPASO_STANDARD`/`TRASPASO_TRANSFERENCIA_DE_DOMINIO` (sin cambio de comportamiento) y `false`
  explícito para `TRASPASO_UNILATERAL`. No toca el handler de agregación paralela ni sus proveedores.
- **ADR-0039** (`ADR-0039-precedencia-unica-decision-envio-identidad.md`, Aceptado) — fija que
  `IdentitySendDecisionForTramite` es la única autoridad sobre "¿corresponde enviar correo de
  validación?". La Decisión 6 de este ADR **engancha** ahí para decidir cuándo revelar el formulario
  oculto del propietario: el disparador es el mismo hueco de dato ("hay que enviar validación y falta
  el correo") que ADR-0039 ya resuelve para la ruta de envío; este ADR no crea una segunda regla de
  envío, solo expone al frontend la señal de que falta un dato para que la decisión de ADR-0039 pueda
  ejecutarse.

## Decisiones técnicas

### 1 — `sellerCapturedViaForm`: separar "hay vendedor" de "el vendedor llena un formulario"

`RequiresSeller` sigue significando exactamente lo mismo que hoy: hay parte vendedora en el FUR
(`FurDocumentData.RequiereVendedor`, ya consumido por `FurFieldMapper.IsTraspaso` en
`FurFieldMapper.cs:429-432`). Se añade `sellerCapturedViaForm` (bool) para responder una pregunta
distinta: ¿esa parte se captura tecleando datos en el wizard, o llega de otra fuente?

- **Backend**: `WizardStateQuery.BuildSectionConfig` (`WizardStateQuery.cs:718-724`, caso
  `ProcedureSectionTypes.ActorForm`) añade `["sellerCapturedViaForm"] = profile.SellerCapturedViaForm`
  al `JsonObject` de configuración de la sección, junto a `requiresSeller`.
- **Frontend**: `wizardCapabilities.ts` añade el campo a `CapacidadesEfectivas` (p. ej.
  `vendedorCapturaPorFormulario`) leyendo `capabilities.sellerCapturedViaForm ?? true` (mismo criterio
  de "ausente ⇒ comportamiento previo" que ya usan `allowsComplementaryTransformations`/
  `allowsComplementaryPrenda`). **`rolesDeActores()` (`wizardCapabilities.ts:237-247`) deja de leer
  `caps.pideVendedor` y pasa a leer `caps.vendedorCapturaPorFormulario`** para decidir si añade
  `'vendedor'` a los roles que pinta `ActorsForm` en `TramiteWizard.tsx` (caso `'actores'`,
  `TramiteWizard.tsx:4584-4614`). `caps.pideVendedor` (`requiresSeller`) sigue gobernando todo lo
  demás que ya gobierna hoy (título, `modalidadPorPartes`, `validaIdentidadDelVendedor`) sin cambio.

### 2 — `signatureActors: string[]`: sustituye los cuatro `esTraspaso ? [comprador,vendedor] : [comprador]` de `FurCommand.cs`

Mismo vocabulario de catálogo que `biometricActors` (`OWNER`/`BUYER`/`LESSEE`), traducido con el MISMO
helper que ya existe para eso — `RuntConsultaExigida.ActorTypeDeEntidad` (`BUYER`→`comprador`,
`OWNER`→`vendedor`, `LESSEE`→`locatario`) — sin escribir una segunda tabla de traducción.

`FurCommand.cs` reemplaza sus cuatro apariciones del ternario:

- `FurCommand.cs:214` (roles para `sellosIdentidad`)
- `FurCommand.cs:264` (roles para el guard de exclusividad baúl/sello, HU #11061)
- `FurCommand.cs:904` (`ResolveVaultSignaturesAsync`)
- `FurCommand.cs:1598` (`ResolverNombresDelDirectorioAsync`)

por `profile.ResolveSignatureActors()` (roles internos, ya traducidos), UNA sola vez al inicio del
método (junto a `esTraspaso` en la línea 156), no cuatro veces.

**Ausente ⇒** `RequiresSeller ? [OWNER, BUYER] : [BUYER]` — replica exactamente el comportamiento
actual para `MATRICULA_NUEVA`, `TRASPASO_STANDARD` y toda `OTROS` (que solo tiene `comprador`). Para
`TRASPASO_TRANSFERENCIA_DE_DOMINIO` (que ya declara `requiresSeller:true` pero cuyo código nunca es
igual a `TRASPASO_STANDARD`) el default **corrige** un bug latente: hoy pierde la firma/sello del
vendedor en estos cuatro puntos por el mismo motivo que `TRASPASO_UNILATERAL`; con el default nuevo
pasa a `[OWNER, BUYER]`, coherente con su `biometricActors` ya sembrado. Se documenta como corrección
de bug, no como cambio de alcance de este ADR — ver §Consecuencias.

**Hueco a cerrar en `FurFieldMapper.cs`** (Backend Agent, mismo cambio): el bloque de firma del
comprador (`FurFieldMapper.cs:91-97`) llama `IdentidadOrSello(data, "comprador", ["comprador"])`
**incondicionalmente**, sin mirar si `comprador` está en las partes firmantes. Para que
`TRASPASO_UNILATERAL` (donde `signatureActors = ["OWNER"]`, comprador NO firma) no imprima un sello o
un "NO FIRMADO" en un espacio de firma que el tipo no exige, `FurDocumentData` necesita transportar
`SignatureActors` (roles ya resueltos) hasta el mapper, y `FurFieldMapper.Map()` debe condicionar
tanto el bloque del propietario (`vehicle_owner_signature`, hoy ya lo hace vía `esTraspaso`) como el
del comprador (`vehicle_buyer_signature`, hoy NO lo hace) a la pertenencia del rol a esa lista.

### 3 — `biometricActors`: se corrige el consumo, no la llave

`biometricActors` ya existe en `ProcedureTypeGateProfile.cs:32`, ya viaja al frontend
(`WizardStateQuery.cs:158,203`), ya lo consumen `DynamicGateEvaluator`, `WizardStateQuery` y **ya
existe un helper correcto que lo traduce a roles internos**: `ActorsCommand.RolesQueValidanIdentidad`
(`ActorsCommand.cs:666-680`), que resuelve exactamente el mismo par (catálogo → rol interno) que este
ADR necesita en los otros dos consumidores que hoy lo ignoran:

- `BiometricaCommand.cs:366-368` — `BiometriaGateOk`/el cálculo de `partes` recalcula
  `esTraspaso = instance.Family == ProcedureFamily.Traspaso` en vez de leer `biometricActors`. Pasa a
  usar el mismo criterio que `ActorsCommand.RolesQueValidanIdentidad` (extraído a un lugar compartido
  si el Backend Agent lo considera necesario para no duplicarlo una tercera vez).
- `FurCommand.cs:643-645` (`BiometriaGateOk`) — recibe `esTraspaso` (bool) como parámetro; pasa a
  recibir el conjunto de roles resuelto de `biometricActors` y comprobar que TODOS estén en
  `identidadAprobadaPartes`, en vez del ternario `comprador && vendedor` vs. solo `comprador`.

No hay llave nueva ni cambio de default: `TRASPASO_STANDARD` sigue en `["OWNER","BUYER"]`,
`TRASPASO_UNILATERAL` se corrige en el seed a `["OWNER"]` (§Tabla de llaves) — hoy trae `["BUYER"]`,
que es el default equivocado que valida identidad al comprador cuando el negocio pidió lo contrario.

### 4 — `generatesSaleDocument` y `hasAppraisalBlock`: capacidades para compraventa y avalúo

Mismo idioma que `AllowsComplementaryTransformations`/`AllowsComplementaryPrenda`
(`ProcedureTypeGateProfile.cs:53-61`): `bool?` — `null` (ausente) no es `false`, es "lo que diga la
familia", resuelto con un método, nunca leyendo la propiedad cruda:

```csharp
public bool? GeneratesSaleDocument { get; init; }
public bool? HasAppraisalBlock { get; init; }

public bool GeneratesSaleDocumentAllowed(string? familyCode) =>
    GeneratesSaleDocument ?? (RequiresSeller && FamilyIsTraspaso(familyCode));

public bool HasAppraisalBlockAllowed(string? familyCode) =>
    HasAppraisalBlock ?? (RequiresSeller && FamilyIsTraspaso(familyCode));
```

- `FurCommand.cs:292` (`if (esTraspaso) generated.Add(generator.GenerateCompraventa(data))`) pasa a
  `if (profile.GeneratesSaleDocumentAllowed(family)) ...`.
- `FurCommand.cs:401` (`AvaluoInfo? avaluo = esTraspaso ? await BuildAvaluoAsync(...) : null`) pasa a
  `profile.HasAppraisalBlockAllowed(family) ? await BuildAvaluoAsync(...) : null`. La RTM
  (`FurCommand.cs:410-413`, `aplicaRtm = esTraspaso && ...`) usa el mismo flag: sin bloque de avalúo,
  tampoco aplica RTM (el negocio no distinguió esto en la tabla de la especificación, así que se
  conserva acoplado a `hasAppraisalBlock` hasta que haya un requerimiento que los separe).
- `TramiteFirmaAplicador.cs:46-56` — el `esTraspaso = instance.Family == ProcedureFamily.Traspaso`
  que decide si "firmar" significa solicitar firma de compraventa (`firmaHandler`) o regenerar FUR
  (`furHandler`) pasa a `profile.GeneratesSaleDocumentAllowed(family)`. Para `TRASPASO_UNILATERAL`
  (`generatesSaleDocument:false`) el encadenado automático de firma cae a `furHandler`, que es lo
  correcto: no hay compraventa que firmar, solo el FUR con el sello del vendedor.

**`requiresCommercialValue`/`commercialValueSource`** (llaves existentes, sin cambio de esquema) se
corrigen en el seed de `TRASPASO_UNILATERAL` a `false`/ausente — gobiernan el PASO comercial del
wizard, pregunta distinta de `hasAppraisalBlock` (el bloque de avalúo que se **imprime** en el FUR y
en el certificado SOAT/RTM, resuelto siempre contra Fasecolda vía VIN, independientemente de lo que el
gestor haya tecleado en el paso comercial).

### 5 — Sincronización del actor `vendedor` desde el RUNT (pieza nueva)

Punto único: `PreflightCommand` — el mismo handler que ya hidrata `field_values` desde el proveedor de
vehículo (`UpsertHydratedFields`, `PreflightCommand.cs:918-937`) y que ya resuelve el organismo de
tránsito desde el RUNT en el mismo recorrido (`AutoBindTransitOfficeFromRuntAsync`,
`PreflightCommand.cs:1050-1083`). Se añade un paso análogo, `SyncSellerActorFromRuntAsync`, invocado
en el mismo punto que `AutoBindTransitOfficeFromRuntAsync` (después de `RunVehiculoAsync`/
`UpsertHydratedFields`, `PreflightCommand.cs:226-227`), con la guardia:

```csharp
if (profile.RequiresSeller && !profile.SellerCapturedViaForm
    && !instance.Actors.Any(a => a.ActorType == "vendedor"))
```

**Fuente del dato** — igual que hoy: `owner_document_type`/`owner_document_number` ya están en
`field_values` desde el paso 1 (tecleados por el gestor, `CreateFromConsultaCommand.cs:261-263`).
Con ese documento, `SyncSellerActorFromRuntAsync` reutiliza el MISMO patrón de lookup best-effort que
`RuntPersonLookupHandler` ya usa para autopoblar al comprador en matrícula (proveedor
`kyverum_runt_conductor`), pero:
- consulta el documento del **vendedor**, no del comprador;
- **persiste** el resultado como fila `ProcedureInstanceActor { ActorType = "vendedor", ... }` en
  `instance.Actors` (vía `repo.Add`, mismo idiom `PK store-generated` que `UpsertSingleField`,
  `PreflightCommand.cs:958-962`), en vez de devolverlo para que un formulario lo confirme —
  `RuntPersonLookupHandler` documenta explícitamente "NO persiste"; esta pieza SÍ persiste, porque no
  hay formulario que lo haga por ella.
- best-effort real: si el lookup no resuelve nombre/dirección/ciudad/teléfono (puede no resolver:
  el propio proveedor no garantiza cobertura total), la fila se crea igual con lo que sí resolvió
  (documento) y el resto en blanco — nunca bloquea el preflight (mismo "degradación, nunca error" que
  el resto del handler).

**Riesgo abierto, no resuelto por este ADR**: `FinalizeDraftGate.ActoresCompletos`
(`FinalizeDraftProcedureInstanceCommand.cs:50-64`) exige `FullName` no vacío para dar por completa una
parte. Si el lookup no resuelve nombre, un `TRASPASO_UNILATERAL` sincronizado queda con el mismo
bloqueo `actores_incompletos` que hoy bloquea el 100% de los borradores — solo que ahora sin
formulario con el que el gestor pueda corregirlo manualmente, salvo que se revele (Decisión 6). Este
ADR dimensiona el problema y establece el punto de escritura; **el criterio de completitud aplicable a
una parte SINCRONIZADA (¿basta el documento? ¿se exige nombre con degradación a "NO REGISTRA" en el
FUR?) queda para que el Backend Agent lo resuelva con el Líder Técnico** antes de mergear, porque toca
un gate de ciclo de vida y este ADR no tiene mandato para relajarlo unilateralmente (regla FLIT #6).

### 6 — Revelado del formulario oculto del propietario

El formulario de vendedor permanece oculto (`sellerCapturedViaForm:false`) salvo dos excepciones,
ambas con el MISMO disparador: **hace falta enviar validación de identidad al vendedor y falta el dato
para hacerlo**.

1. Persona jurídica sin representante legal utilizable en `IRepresentanteLegalDirectory`
   (`RepresentanteLegalDirectory.BuscarNombreRepresentanteAsync`, puerto ya usado por
   `FurCommand.cs:1590-1610`) — sin RL no hay a quién dirigir la validación.
2. Persona natural sin correo conocido — la consulta RUNT por placa no devuelve correo (ni lo
   devolvería aunque se ampliara: no es un dato público del RUNT), y `IniciarBiometriaHandler`
   (`BiometricaCommand.cs:152-156`) exige `input.Email` no vacío para poder crear la validación.

Este ADR **no** crea una regla de envío paralela: la decisión de si corresponde enviar sigue siendo,
exclusivamente, `IdentitySendDecisionForTramite` (ADR-0039). Lo que este ADR fija es **dónde se calcula
el flag de revelado** (una pregunta distinta: "¿falta el dato para poder ejecutar esa decisión?") y
cómo llega al frontend:

- **Backend**: `WizardStateQuery` calcula, para la parte `vendedor` cuando
  `RequiresBiometrics && biometricActors` incluye `OWNER`, si el actor sincronizado es jurídico sin RL
  resoluble o natural sin correo, y expone el resultado en el `sectionConfig` del paso `actor_form`
  (`WizardStateQuery.cs:718-724`) como `["revealSellerForm"] = bool`. Es una señal **por instancia**,
  no una llave de `gate_profile` — depende de datos del trámite, no del tipo.
- **Frontend**: `TramiteWizard.tsx`, caso `'actores'` (`TramiteWizard.tsx:4584-4614`), añade
  `'vendedor'` a `roles` cuando `sectionConfig.revealSellerForm === true`, aunque
  `vendedorCapturaPorFormulario` sea `false` — el revelado es una excepción puntual sobre la capacidad
  declarada, no un cambio de la capacidad misma.

## Compatibilidad hacia atrás

| Llave | Ausente ⇒ | Por qué no afecta a lo existente |
|---|---|---|
| `sellerCapturedViaForm` | `true` | Todo tipo que hoy captura al vendedor por formulario (`TRASPASO_STANDARD`, `TRASPASO_TRANSFERENCIA_DE_DOMINIO`) sigue haciéndolo — nadie más declara `requiresSeller:true` hoy en el catálogo. `MATRICULA_*` y `OTROS` tienen `requiresSeller` ausente/`false`: la llave nueva nunca se evalúa para ellos. |
| `signatureActors` | `RequiresSeller ? [OWNER,BUYER] : [BUYER]` | `MATRICULA_NUEVA`, `MATRICULA_LEASING`, toda `OTROS`: `RequiresSeller` ausente/`false` ⇒ `[BUYER]`, igual que el `esTraspaso ? … : [comprador]` de hoy. `TRASPASO_STANDARD`: `RequiresSeller:true` ⇒ `[OWNER,BUYER]`, igual que hoy. `TRASPASO_TRANSFERENCIA_DE_DOMINIO`: cambia de `[BUYER]` (comportamiento actual, con bug) a `[OWNER,BUYER]` — corrección deliberada, documentada en Decisión 2 y en Consecuencias, no una regresión silenciosa. |
| `generatesSaleDocument` / `hasAppraisalBlock` | `RequiresSeller && family==TRASPASO` | `TRASPASO_STANDARD` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO` (ambos con `requiresSeller:true` en el catálogo) siguen generando compraventa y bloque de avalúo sin cambio. `OTROS` nunca tiene `requiresSeller:true`, así que nunca activa estas capacidades — coherente con que `OTROS` no transfiere propiedad. |
| `biometricActors` | *(sin cambio de esquema)* | El cambio es de **consumo**, no de default. `TRASPASO_STANDARD` sigue en `["OWNER","BUYER"]`; el único valor que cambia es el de `TRASPASO_UNILATERAL`, de `["BUYER"]` (dato equivocado, sin validar por negocio) a `["OWNER"]` (validado) — parte del seed que este ADR corrige explícitamente. |

`TRASPASO_UNILATERAL` es el único tipo cuyo `gate_profile` cambia de VALOR (no solo gana llaves nuevas
en `null`/ausente): pasa de la base técnica sin validar de `82-parametrizacion-catalogo-completo.sql`
al perfil de la §Tabla de llaves. Como el tipo tiene `wizard_enabled = false` (no operable en
creación, ADR-0050 §Consecuencias) y no hay expedientes reales de este tipo en ningún ambiente, no hay
snapshot congelado (`procedure_type_snapshots`) que proteger.

## Tabla de llaves nuevas del `gate_profile`

| Llave | Tipo | Default (ausente) | Valor en `TRASPASO_UNILATERAL` | Quién la consume |
|---|---|---|---|---|
| `sellerCapturedViaForm` | `bool` | `true` | `false` | Backend: `ProcedureTypeGateProfile.cs` (propiedad nueva); `WizardStateQuery.cs:718-724` (`sectionConfig.actor_form`). Frontend: `wizardCapabilities.ts` (`CapacidadesEfectivas`, `rolesDeActores`); `TramiteWizard.tsx:4584-4614` (indirecto, vía `rolesDeActores`). |
| `signatureActors` | `string[]` (`OWNER`\|`BUYER`\|`LESSEE`) | `RequiresSeller ? ["OWNER","BUYER"] : ["BUYER"]` | `["OWNER"]` | Backend: `FurCommand.cs:214,264,904,1598` (reemplaza el ternario `esTraspaso`); `FurFieldMapper.cs:91-97` (nuevo — hoy no lo mira); `TramiteFirmaAplicador.cs` NO la consume (usa `generatesSaleDocument`, ver abajo). Traducción de vocabulario vía `RuntConsultaExigida.ActorTypeDeEntidad`. |
| `generatesSaleDocument` | `bool?` | `RequiresSeller && family==TRASPASO` | `false` | Backend: `FurCommand.cs:292` (`GenerateCompraventa`); `TramiteFirmaAplicador.cs:46-56` (qué handler dispara el encadenado de firma). |
| `hasAppraisalBlock` | `bool?` | `RequiresSeller && family==TRASPASO` | `false` | Backend: `FurCommand.cs:401,410-413` (`BuildAvaluoAsync` + aplicación de RTM). |
| `biometricActors` *(existente, corrige valor y consumo)* | `string[]` | *(sin cambio)* | `["OWNER"]` (corrige `["BUYER"]` del seed actual) | Backend: `BiometricaCommand.cs:366-368` y `FurCommand.cs:643-645` (`BiometriaGateOk`) — hoy la ignoran y deben pasar a leerla, mismo criterio que `ActorsCommand.RolesQueValidanIdentidad` (`ActorsCommand.cs:666-680`), que ya la lee bien. |
| `requiresCommercialValue` / `commercialValueSource` *(existentes, corrige valor)* | `bool` / `string?` | *(sin cambio)* | `false` / ausente | Backend: paso comercial del wizard (`WizardStateQuery`), sin cambio de código — solo corrige el dato sembrado. |

**Sin llave de `gate_profile` nueva** (por ser señales por instancia, no por tipo):
- El punto de escritura del actor `vendedor` sincronizado — `PreflightCommand.SyncSellerActorFromRuntAsync`,
  disparado por `RequiresSeller && !SellerCapturedViaForm` (Decisión 5).
- `revealSellerForm` — calculado por `WizardStateQuery` a partir de `IRepresentanteLegalDirectory` y
  del correo del actor sincronizado; expuesto en `sectionConfig.actor_form` (Decisión 6).

**Seed corregido de referencia** (borrador para `database-agent`, no un DDL final — el checklist de
`checklist-validacion-schema.md` decide la forma exacta de la migración):

```json
{
  "entryMode": "PLATE",
  "requiresSeller": true,
  "sellerCapturedViaForm": false,
  "requiresBuyer": true,
  "signatureActors": ["OWNER"],
  "generatesSaleDocument": false,
  "hasAppraisalBlock": false,
  "requiresBiometrics": true,
  "biometricActors": ["OWNER"],
  "requiresSignature": true,
  "validateOtOperability": true,
  "simitMode": "INTERNAL"
}
```

Se retiran del valor sembrado hoy: `requiresCommercialValue`, `commercialValueSource` (avalúo no
aplica a este tipo).

## Sequence diagram — preflight con sincronización del vendedor + wizard revelando o no el formulario

```mermaid
sequenceDiagram
    participant FE as Wizard (frontend)
    participant API as CreateFromConsultaCommand
    participant PF as PreflightCommand
    participant RUNT as Proveedor vehículo (Kyverum/Verifik)
    participant LOOKUP as RuntPersonLookup-like (best-effort)
    participant DB as instance.Actors / field_values
    participant WSQ as WizardStateQuery

    FE->>API: avanzar paso 1 (placa + owner_document_type/number tecleados)
    API->>DB: PATCH field_values (plate, owner_document_type, owner_document_number)
    API->>PF: HandleAsync (preflight autoritativo)
    PF->>RUNT: consulta vehículo por placa
    RUNT-->>PF: HydratedFields (vehículo; owner_document_type)
    PF->>DB: UpsertHydratedFields (field_values del vehículo)
    PF->>PF: profile = ProcedureTypeGateProfile.FromJson(...)
    alt profile.RequiresSeller && !profile.SellerCapturedViaForm && sin fila "vendedor"
        PF->>LOOKUP: consultar documento del vendedor (owner_document_type/number)
        LOOKUP-->>PF: nombre/dirección/ciudad/teléfono (best-effort, puede venir parcial)
        PF->>DB: crear ProcedureInstanceActor{ActorType="vendedor", ...}
    end
    FE->>WSQ: GET wizard state
    WSQ->>WSQ: revealSellerForm = actor "vendedor" es (PJ sin RL) o (PN sin correo) AND biometricActors incluye OWNER
    WSQ-->>FE: capabilities{sellerCapturedViaForm:false, biometricActors:["OWNER"], sectionConfig.actor_form.revealSellerForm}
    alt revealSellerForm == true
        FE->>FE: pintar formulario de vendedor (capturar RL o correo)
    else
        FE->>FE: paso "actores" solo pide comprador/locatario
    end
```

## Archivos a crear/modificar

### `services/core-api` (backend)

- `src/Flit.Tramites.Domain/Tramites/Services/ProcedureTypeGateProfile.cs` — añadir
  `SellerCapturedViaForm` (bool, default `true`), `SignatureActors` (`string[]`, default `[]`) +
  `ResolveSignatureActors()`, `GeneratesSaleDocument`/`HasAppraisalBlock` (`bool?`) +
  `GeneratesSaleDocumentAllowed(familyCode)`/`HasAppraisalBlockAllowed(familyCode)`. Actualizar el
  comentario XML con el esquema (espeja el comentario DDL de `gate_profile`).
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/FurCommand.cs` — sustituir las 4
  apariciones del ternario `esTraspaso ? [comprador,vendedor] : [comprador]` (líneas 214, 264, 904,
  1598) por `profile.ResolveSignatureActors()`; `BiometriaGateOk` (643-645) lee `biometricActors`;
  `GenerateCompraventa` (292) y `BuildAvaluoAsync`/RTM (401,410-413) leen
  `GeneratesSaleDocumentAllowed`/`HasAppraisalBlockAllowed`; `AssembleData` recibe y propaga
  `SignatureActors` a `FurDocumentData`.
- `src/Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` — `FurDocumentData` gana
  `SignatureActors: IReadOnlyList<string>` (roles internos ya resueltos), junto a `RequiereVendedor`.
- `src/Flit.Infrastructure/Documents/Fur/FurFieldMapper.cs` — condicionar el bloque
  `vehicle_buyer_signature` (líneas 91-97) a que `"comprador"` esté en `data.SignatureActors`, no
  incondicional; revisar `IsTraspaso`/`ResolvePropietario` (17-19, 429-432) para que sigan alineados
  con `RequiereVendedor` (sin cambio de comportamiento fuera de la corrección del bloque comprador).
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/BiometricaCommand.cs` — `BiometriaGateOk`
  y el cálculo de `partes` (366-368) leen `biometricActors` en vez de `instance.Family`.
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/ActorsCommand.cs` — extraer/exponer
  `RolesQueValidanIdentidad` (666-680) si `BiometricaCommand.cs` va a reutilizarlo directamente.
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/GenerarImprontaAttachmentCommand.cs` —
  `esTraspaso` (63) pasa a `profile.RequiresSeller`; `rolPropietario` (70) pasa a `RequiresSeller ?
  "vendedor" : "comprador"` (sin cambio de resultado para los tipos ya sembrados).
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/PreflightCommand.cs` —
  `AutoBindTransitOfficeFromRuntAsync` (1050-1083): sustituir `esTraspasoStandard || Family==Otros`
  (1056-1059) por `!profile.OperatorChoosesTransitOffice()` (capacidad **ya existente**, sin llave
  nueva). Añadir `SyncSellerActorFromRuntAsync` (nuevo, Decisión 5), invocado junto a
  `AutoBindTransitOfficeFromRuntAsync` (~línea 226-227).
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/FinalizeDraftProcedureInstanceCommand.cs`
  — `ActoresCompletos` (50-64) lee `RequiresSeller` en vez de `Family==Traspaso`; resolver con Líder
  Técnico el criterio de completitud para la parte sincronizada (Decisión 5, riesgo abierto) antes de
  implementar.
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/TramiteFirmaAplicador.cs` — `esTraspaso`
  (46-56) pasa a `profile.GeneratesSaleDocumentAllowed(family)`.
- `src/Flit.Tramites.Application/Identity/IdentityValidationCompletedConsumer.cs` — `esTraspaso`
  (63-68) pasa a leer `biometricActors`/`RequiresSeller` según corresponda al disparo que hace.
- `src/Flit.Tramites.Application/UseCases/ProcedureInstances/ListProcedureInstancesQuery.cs` —
  `PartesTraspaso`/`PartesMatricula` (334-347) y `DeriveSignaturePending` (357,381) leen
  `biometricActors`/`signatureActors` del tipo en vez de la familia — requiere que el listado
  proyecte `gate_profile` o los campos ya resueltos.
- `src/Flit.Infrastructure/Notifications/Tramites/TramiteCambioEstadoEmailProjector.cs` — `esTraspaso`
  (27-44) pasa a `RequiresSeller` (sigue determinando si se incluye `VendedorNombre` en el correo).
- `src/Flit.Tramites.Application/Documents/FurPreviewSample.cs` — `IsUnilateral` (183-184) deja de ser
  un `if` hardcodeado del simulador: `omitirFirmaComprador` (141) pasa a derivarse de
  `!signatureActors.Contains("comprador")` sobre el perfil real del tipo (o simulado, si el parámetro
  `flags` lo permite), para que el simulador y el generador real compartan la misma fuente.
- `src/Flit.Infrastructure/Persistence/Sql/Ddl/` — migración nueva (numeración siguiente a `90-…`) que
  corrige `gate_profile` de `TRASPASO_UNILATERAL` (ver seed de referencia arriba). **Responsabilidad
  del `database-agent`** conforme a `checklist-validacion-schema.md`.
- `services/core-api/docs/schema/ddl/05-F08-conformation-profile.sql` — actualizar el comentario de
  esquema de `gate_profile` con las llaves nuevas (espejo del DDL real).

### `frontend`

- `components/operacion/wizardCapabilities.ts` — `CapacidadesEfectivas` gana el campo de
  `sellerCapturedViaForm` (nombre en español a definir por Frontend Agent, p. ej.
  `vendedorCapturaPorFormulario`); `capacidadesEfectivas()` lo lee de `capabilities.sellerCapturedViaForm
  ?? true`; `desdeModalidad()` (respaldo) lo fija en `true` (nunca hay tipos con captura oculta en el
  respaldo de dos modalidades heredadas); `rolesDeActores()` (237-247) usa el campo nuevo en vez de
  `pideVendedor` para decidir si añade `'vendedor'`.
- `lib/api/types/procedure-runtime.ts` — `WizardCapabilities` gana `sellerCapturedViaForm?: boolean`;
  el `sectionConfig` de `actor_form` (tipo asociado, si existe uno tipado) gana `revealSellerForm?:
  boolean`.
- `components/operacion/TramiteWizard.tsx` — caso `'actores'` (4584-4614): leer
  `revealSellerForm` desde el `sectionConfig` del paso y añadir `'vendedor'` a `roles` cuando sea
  `true`, aunque `caps.vendedorCapturaPorFormulario` sea `false`.
- `components/operacion/__tests__/wizardCapabilities.test.ts` — casos nuevos: `TRASPASO_UNILATERAL`-like
  (`requiresSeller:true, sellerCapturedViaForm:false`) → `pideVendedor:true`,
  `vendedorCapturaPorFormulario:false`, `rolesDeActores` sin `'vendedor'`.

### `contracts/openapi/core-api.v1.yaml`

- Esquema de respuesta del wizard state (`WizardCapabilities` o equivalente): añadir
  `sellerCapturedViaForm` (boolean, nullable/opcional) y, en el `sectionConfig` de la sección
  `actor_form`, `revealSellerForm` (boolean, opcional). Sin cambio de código HTTP ni de rutas.

## Notas para agentes

- **Database Agent**: migración de corrección del seed de `TRASPASO_UNILATERAL` en
  `gate_profile` (ver JSON de referencia arriba), idempotente (mismo idiom `UPDATE ... WHERE code =
  'TRASPASO_UNILATERAL'` que `38-F08-seeds-tipos-configurados.sql`). Actualizar el comentario de
  esquema de `gate_profile` (migración `35-F08-conformation-profile.sql` y su espejo en
  `docs/schema/ddl/`) con las 4 llaves nuevas. No hay `procedure_type_snapshots` que proteger (tipo no
  operable, sin expedientes reales). Confirmar contra `checklist-validacion-schema.md` §A si
  `ProcedureInstanceActor` necesita alguna columna nueva para distinguir "capturado por formulario" de
  "sincronizado desde RUNT" (auditoría/trazabilidad de origen del dato) — este ADR no lo exige, pero
  puede ser necesario para la Decisión 5.
- **Backend Agent**: seguir literalmente la tabla de llaves y la lista de archivos. Extraer/reutilizar
  `ActorsCommand.RolesQueValidanIdentidad` en vez de escribir una cuarta traducción de
  `biometricActors`. **Antes de implementar `FinalizeDraftGate` para la parte sincronizada, escalar al
  Líder Técnico el criterio de completitud** (Decisión 5, riesgo abierto) — no relajar el gate por
  cuenta propia. `SyncSellerActorFromRuntAsync` sigue el patrón "degradación, nunca error" de
  `PreflightCommand`: un lookup fallido no debe romper el preflight.
- **Frontend Agent**: `rolesDeActores()` es el único punto que decide qué roles pinta `ActorsForm`;
  no dupliques la lógica de revelado en `TramiteWizard.tsx` fuera del `sectionConfig.actor_form` que
  ya expone el backend. No recalcular `revealSellerForm` en el cliente.
- **QA Agent**: TC de paridad FUR real vs. `FurPreviewSample` para `TRASPASO_UNILATERAL` (mismo
  conjunto de firmas, mismos actores, misma ausencia de compraventa/avalúo). TC de
  `TRASPASO_TRANSFERENCIA_DE_DOMINIO` antes/después (documentar el cambio de comportamiento de
  `signatureActors` como corrección esperada, no como regresión). TC de sincronización: lookup
  exitoso vs. lookup parcial (sin nombre) vs. lookup fallido — verificar que ninguno rompe el
  preflight. TC de revelado: PJ sin RL, PN sin correo, y el caso normal (formulario oculto).
- **Security Agent**: la sincronización automática del vendedor persiste datos personales de un
  tercero (el propietario/vendedor) sin que haya mediado una captura consentida por formulario en ESE
  trámite — confirmar contra Habeas Data / Ley 1581 si el consentimiento ya obtenido en el contrato de
  leasing (fuera del sistema) cubre esta persistencia, o si hace falta una nota de origen del dato
  (`Source="runt_sync"` en el actor, análogo a `Source="consultation"` de `field_values`) para
  trazabilidad. Esto no está resuelto por este ADR — es una pregunta para el Líder Técnico antes de
  implementar la Decisión 5.
- **Infra Agent**: sin cambios de despliegue; la migración del seed corre en el flujo normal de
  `Database:AutoMigrate` (no es destructiva, es un `UPDATE` de un `jsonb`).

## Referencias externas

- `services/core-api/docs/adr/ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md`
- `services/core-api/docs/adr/ADR-0035-compraventa-autogenerada-siempre-y-sin-membrete.md`
- `services/core-api/docs/adr/ADR-0031-compraventa-autogenerada-firmada-identidad-fur.md`
- `services/core-api/docs/adr/ADR-0029-avaluo-comercial-multiproveedor.md`
- `services/core-api/docs/adr/ADR-0039-precedencia-unica-decision-envio-identidad.md`
- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/82-parametrizacion-catalogo-completo.sql`
- `services/core-api/src/Flit.Infrastructure/Persistence/Sql/Ddl/38-F08-seeds-tipos-configurados.sql`
- `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/KyverumRuntVehicleResponse.cs`
  (hallazgo verificado: sin campos de nombre/dirección/teléfono del propietario)
