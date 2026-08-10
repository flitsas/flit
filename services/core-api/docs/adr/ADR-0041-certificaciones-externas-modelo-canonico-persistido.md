# ADR-0041: Las certificaciones externas (SOAT, RTM, RUES) se persisten como modelo canónico propio, independiente del proveedor

**Fecha**: 2026-08-07
**Status**: Aceptado
**Fecha de aceptación**: 2026-08-08
**Status previo**: Propuesto (2026-08-07)
**Deciders**: Líder Técnico FLIT (aceptado), Product Owner (origen del requisito y las 9 decisiones)
**Tags**: arquitectura, backend, base-de-datos, modulo-tramites, documental, consultas-externas, habeas-data

**Supersedes parcialmente**: [ADR-0037] — snapshot RUES congelado por trámite. Se conserva **íntegra la
decisión de negocio** (el certificado se emite desde lo consultado al registrar y no se reconsulta al
regenerar) y se **sustituye el mecanismo**: el snapshot deja de vivir en el `field_value`
`rues_snapshots_json` y pasa a la tabla `tramites.company_registrations`. Es exactamente la "Opción 4"
que ADR-0037 dejó sobre la mesa y descartó *por ahora* — las razones para descartarla ya no se sostienen
(ver Contexto).

**Relacionado**: [ADR-0020] consultas multiproveedor · [ADR-0030] caché de consultas con TTL (se conserva
para su propósito original; sigue sin ser fuente documental).

## Contexto

Las tablas certificadoras de SOAT, RTM y RUES del expediente consolidado salen incompletas. La Feature
#11131 (7 HUs, PR #208 mergeado) se propuso literalmente "persistir lo consultado y dejar de
reconsultar", y no bastó. Las cifras medidas hoy sobre `flit_local` lo confirman: `rtm_numero`,
`rtm_expedicion`, `rtm_entidad`, `rtm_vigencia` y `soat_expedicion` tienen **cero** filas; `soat_poliza`
solo aparece cuando el usuario cargó el PDF y el OCR funcionó.

Tres causas estructurales explican por qué un arreglo campo a campo no podía funcionar:

1. **El modelo del proveedor primario se dedujo de fixtures, no del proveedor.**
   `KyverumRuntVehicleResponse.cs:167-211` declara por escrito que Kyverum "no trae ni póliza, ni fecha
   de expedición, ni de vigencia". Tres consultas reales (`docs/consulta-runt-nzs920-procesamiento.md`)
   demuestran que sí las trae, siempre: `numSoat`, `fechaExpediSoat`, `fechaInicioPoliza`, `numeCerti`,
   `fechaExpedicionRvt`, `nombreCda`, `tipoRevision` y `fechaRegistro`. Como **no se guarda el payload
   crudo** de RUNT ni de RUES, no había forma de descubrirlo ni de desmentirlo: el modelo se convirtió
   en una profecía autocumplida, y las celdas quedaron dependiendo del OCR de un PDF.

2. **El vocabulario de destino es texto libre sin tipo.** `HydratedField(FieldKey, ValueText, ValueJson)`
   sobre `(instance, field_key)`: cada mapper inventa sus llaves, sus formatos de fecha
   (ISO con offset, `dd/MM/yyyy`, `AAAA-MM-DD`, lo que devuelva el OCR) y su vocabulario de estado
   (`VIGENTE`, `SI`, `vigente`, `APROBADA`). No hay value objects, ni normalización al escribir, ni
   forma de representar el histórico de pólizas y revisiones que el RUNT ya entrega (hasta 5 de cada
   uno). Añadir más llaves agrava el problema en vez de acotarlo.

3. **El almacén elegido no admite reparación.** `field_values` es inmutable fuera de borrador
   (`tramites.trg_field_value_immutable`). Cuando la HU #11132 corrigió seis mapeos, los trámites vivos
   quedaron sin arreglo posible: ni backfill ni reproceso, solo reconsultar, que se cobra por llamada.
   Ese mismo bloqueo obligó al certificado RUES a sostener una consulta **en vivo** durante la
   generación del expediente (`FurCommand.cs:926`), la única llamada saliente que queda al generar un
   PDF.

Se suma un cuarto hecho de operación real: en `frontend/components/operacion/ActorsForm.tsx:936`, cuando
el NIT ya está en el directorio de representantes legales del tenant, el flujo hace `return` y **no
consulta el RUES**. Como el snapshot solo se escribe en esa consulta y solo mientras el trámite está en
borrador, esas compañías —las recurrentes, precisamente— quedan sin datos RUES persistidos. Hoy el
certificado se salva porque el generador repone la consulta **en vivo, cobrada, en cada regeneración**
del expediente; si el proveedor falla, no se emite nada.

Requisitos explícitos del PO: (1) persistir SOAT, RTM y RUES en base de datos para generar el PDF sin
volver a consultar; (2) los proveedores son varios (kyverum, verifik, intempo, y los que vengan) y la
información debe quedar **normalizada sin importar la fuente**; (3) alta cohesión y bajo acoplamiento.

## Decisión

Las certificaciones externas se modelan como **agregados de dominio propios, independientes del
proveedor**, y se persisten en **tablas propias del esquema `tramites`**, junto con el **payload crudo
sanitizado** de la consulta que las originó.

1. **Modelo canónico** en `Flit.Tramites.Domain/Certifications`: `SoatCertification`,
   `RtmCertification`, `MerchantRegistration` y `VehicleRegistrationFacts`, construidos sobre value
   objects que conservan **siempre** el valor canónico *y* el crudo del proveedor (`CertifiedDate`,
   `CertifiedStatus`, `CertifiedNumber`, `CertifiedName`). Vocabulario de vigencia cerrado
   (`vigente | vencido | no_aplica | unknown`), con los mismos literales que `SoatGate` para que la
   proyección al gate del OT sea la identidad.

2. **La normalización ocurre al persistir, una sola vez**: fechas a `date` con offset de Colombia
   (no `AdjustToUniversal`, que puede correr el día calendario), estados al vocabulario cerrado,
   números **siempre como texto** (`numSoat` real de 16 dígitos), nombres con `Trim` y colapso de
   espacios. Al pintar solo queda formato de presentación. Lo no interpretable **no se inventa ni se
   vacía**: se guarda el crudo, se marca `Unparsed`, se registra la incidencia y el documento imprime
   el crudo.

3. **Cuatro tablas nuevas**: `vehicle_soat_policies`, `vehicle_rtm_inspections`,
   `company_registrations` y `external_query_payloads`, ancladas a `(tenant_id, procedure_instance_id)`.
   Se guarda el **histórico completo** de pólizas y revisiones, con `is_current` marcando la elegida.
   Al vivir en tablas propias **no tocan la inmutabilidad de `field_values`** — mismo precedente que
   `tramites.procedure_instance_prenda`— y el congelamiento pasa a ser **explícito** (`frozen_at`) en
   vez de heredado, lo que permite completar y reparar cuando el negocio lo autorice.

4. **Procedencia por dato y precedencia por celda.** Cada fila declara `source_kind`, `provider_key`,
   `observed_at`, `raw_payload_id` y `mapper_version`. La precedencia se generaliza a
   `consultation (300) > user (200) > ocr (100) > system (50)`, desempatada por `observed_at`, con la
   excepción de que una corrección manual **posterior** a la última consulta se conserva. El certificado
   deja de afirmar globalmente "se consultó al RUNT 2.0 el día X" y **declara la fuente en el pie de
   cada tabla**.

5. **Costura de normalización**: cada mapper de proveedor produce el bundle canónico usando
   normalizadores compartidos, y lo transporta por dos propiedades **aditivas** de `ConsultationResult`
   (`Certifications`, `RawPayload`), igual que se hizo con `FromCache`/`QueriedAt` en la HU #10878.
   La fusión entre fuentes ocurre **en la persistencia**, no en el chain resolver: éste no llama al
   segundo proveedor cuando el primero responde bien, y forzarlo costaría una llamada facturada por
   trámite. Un quinto proveedor se añade implementando su mapper y registrándose en la cadena, sin
   tocar los otros cuatro ni el generador de PDF.

6. **Consumo**: `FurCommand` deja de leer `Get(fv,"…")` para estas 32 celdas y consulta
   `ICertificationReader`, que resuelve tabla → fallback legacy (`rues_snapshots_json`, llaves `rues_*`,
   `field_values` de SOAT/RTM) y **nunca** consulta a un proveedor. Se conserva la escritura de las
   llaves que tienen consumidores fuera del certificado —`soat_estado` sobre todo, que es gate del OT—
   como **proyección derivada** con un escritor único.

7. **Generar el expediente cuesta cero llamadas externas, sin excepción.** El PO resolvió (2026-08-07)
   que la consulta en vivo del RUES se retira **sin condiciones**, y que el corte del wizard se conserva
   tal cual: cuando el NIT viene precargado del directorio de representantes legales, **no se consulta y
   no se emite certificado RUES para ese actor**. Es una renuncia consciente —esos expedientes hoy sí
   llevan el anexo— a cambio de eliminar la última llamada saliente de la generación documental. En
   coherencia, se retiran los fallbacks `"Sin razón social"` y `"DESCONOCIDO"`
   (`VerifikRuesConsultationProvider.cs:163-164`), que de lo contrario provocarían la emisión de un
   certificado con una sola casilla poblada.

8. **El payload crudo se conserva de forma indefinida** (decisión del PO, 2026-08-07). Se sanitiza antes
   de escribir y se marca `@pii:high`. Queda pendiente de revisión del `security-agent` la finalidad
   declarada frente a la Ley 1581, con la salida intermedia de acotar el plazo solo para el payload del
   RUES —el único que contiene nombres y documentos de personas naturales— dejando indefinido el de
   vehículo.

## Alternativas consideradas

### Opción 1 — Tablas normalizadas propias + payload crudo (ELEGIDA)

**Pros:** alta cohesión, bajo acoplamiento; admite el histórico; el congelamiento es explícito y por
tanto reparable; habilita reproceso sin volver a pagar la consulta; el gate `soat_estado` y la
denormalización `vin`/`plate` quedan intactos; hay precedente en el repo (`procedure_instance_prenda`).
**Contras:** exige DDL, migración, repositorio y backfill; durante la transición conviven dos fuentes.
**Esfuerzo:** M · **Riesgos:** divergencia entre tabla y proyección `field_values` (se acota con un
escritor único y el guardián de cobertura).

### Opción 2 — Documento congelado en `field_values`, extendiendo ADR-0037 al vehículo

**Descartada.** Es la más barata (S, cero DDL) y hereda el congelado gratis, pero **hereda también lo
que causó el fracaso anterior**: al vivir en `field_values`, el dato no se puede completar ni reparar
fuera de borrador, y un trámite cuyo snapshot no se escribió a tiempo no tiene segunda oportunidad —
que es exactamente lo que ocurre hoy con las compañías del directorio de representantes legales.
Además no admite consulta relacional ni analítica, y un JSON en una columna de texto sin esquema no es
estructuralmente distinto de lo que ya hay.

### Opción 3 — Seguir en `field_values` con llaves adicionales

**Descartada.** Es literalmente lo que hizo la Feature #11131 y no bastó: sigue sin tipos, sin
normalización al escribir, sin histórico (una llave = un valor) y sin capacidad de reparación. Las cifras
posteriores al despliegue lo confirman: cinco de las llaves que la Feature añadió tienen cero filas.

### Opción 4 — Reusar `external_query_cache` como fuente documental

**Descartada**, por la misma razón que la descartó ADR-0037: su TTL haría que el certificado dejara de
ser reproducible, y la misma regeneración daría documentos distintos según cuándo se ejecute. La caché
conserva su propósito original.

## Consecuencias

**Positivas**
- Generar el expediente cuesta **cero** consultas externas, también para el RUES.
- Las 12 celdas de SOAT/RTM se llenan desde la fuente oficial con el proveedor primario, sin depender
  del OCR de un PDF cargado a mano.
- Corregir un mapeo pasa a ser reprocesable desde el payload crudo, sin volver a pagar la consulta.
- Añadir un proveedor no toca a los existentes ni al generador de PDF.
- La regla de antigüedad de la RTM (HU #11136) deja de estar inerte: `vehicle_registration_date` se
  puebla desde `vehiculo.fechaRegistro`, que el primario ya enviaba.
- El certificado deja de atribuir al RUNT datos que pueden venir de un PDF escaneado.

**Negativas / riesgos**
- Dos fuentes conviven durante la transición (tabla + `field_values` legacy). Se acota con orden de
  lectura explícito y con un guardián de cobertura que ejecuta el camino completo.
- **Los trámites ya cursados no se reparan.** El PO decidió no reconsultar (ni manual ni masivamente):
  el fix aplica solo hacia adelante. El backfill traslada lo que ya existe; no rellena lo que nunca se
  guardó.
- **Las compañías registradas en el directorio de representantes legales pierden su certificado RUES**
  en el expediente. Es la contrapartida directa de eliminar la consulta en vivo. Conviene medir el
  volumen tras el despliegue; si resulta alto, la decisión es reversible con una consulta única al
  registrar el actor.
- El payload crudo del RUES contiene PII (nombres y documentos de representantes legales dentro del
  texto de facultades) y **se conserva sin plazo**. Obliga a sanitización, marcado `@pii:high` y
  finalidad declarada — requisitos de Ley 1581 que hoy no aplicaban porque el dato no se guardaba.
  Es el punto más expuesto del diseño y requiere visto bueno del `security-agent`.
- Cuatro tablas más en `tramites`, con su coste de mantenimiento.

## Qué NO decide este ADR

- **No** reactiva la lectura de la caché de reúso en el asistente (sigue apagada por HU #10955).
- **No** cambia el gate `soat_estado` ni su excepción en el trigger de inmutabilidad.
- **No** decide si los representantes legales estructurados se pintan en el certificado (se guardan;
  el layout es del PO).
- **No** corrige el desalineamiento del mock de RUES ni las etiquetas discutibles del certificado
  (`Razón Cancelación`, `Ubicación`), que van por su propia vía.
- **No** activa `FORCE ROW LEVEL SECURITY`: las tablas nuevas llevan RLS por consistencia con las 49
  existentes, sabiendo que hoy la política no se evalúa.

## Referencias

- `docs/plan-fix-definitivo-tablas-certificadoras.md` — plan de implementación de este ADR
- `docs/tablas-certificadoras-consolidado-soat-rtm-rues.md` — qué espera cada celda y de dónde sale
- `docs/consulta-runt-nzs920-procesamiento.md` — tres consultas reales al proveedor primario
- `docs/consulta-rues-nits-procesamiento.md` — capturas reales del RUES y sus hallazgos
- `docs/plan-tecnico-tablas-certificadoras.md` — Feature #11131 (intento anterior)
- [ADR-0037] snapshot RUES congelado · [ADR-0030] caché con TTL · [ADR-0020] multiproveedor
- Precedente de tabla propia para eludir la inmutabilidad: `Ddl/24-HU10585-prenda.sql:5`
