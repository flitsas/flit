# ADR-0037: El certificado RUES se emite desde un snapshot congelado al registrar el trámite

**Fecha**: 2026-07-30
**Status**: Propuesto
**Deciders**: Product Owner (origen del requisito), Líder Técnico FLIT (pendiente de aceptación)
**Tags**: arquitectura, backend, modulo-tramites, documental, consultas-externas, costos

**Supersedes parcialmente**: [ADR-0031] / HU #10955 — solo en lo relativo a la **emisión del certificado RUES**. La consulta de identidad del actor en el asistente **no** cambia con este ADR.

## Contexto

El Certificado RUES del expediente consolidado se arma con los datos de registro mercantil de cada actor persona jurídica. Hoy, cuando esos datos no están en `field_values` para *ese* NIT, `RuesActorDataResolver` los resuelve **consultando en vivo al proveedor**, y lo hace por diseño explícito: la clase documenta que *"deliberadamente NO lee la caché de reúso"* porque HU #10955 estableció que el estado en RUES de una persona jurídica se consulta siempre en vivo y *"un certificado no debe dar fe de un payload cacheado"*.

Tres hechos hacen insostenible ese diseño:

1. **Cada consulta al RUES se cobra.** El expediente se regenera muchas veces a lo largo del trámite (cambios de estado, aprobación del OT, subsanación, cualquier regeneración documental), y cada regeneración podía disparar una consulta por actor jurídico.
2. **La ruta "barata" casi nunca aplica.** Las llaves `rues_*` de `field_values` son **de instancia**: un único juego por trámite. En un traspaso entre dos personas jurídicas solo una de las dos puede estar representada, así que la otra caía **siempre** a la consulta en vivo.
3. **`field_values` no es escribible fuera de borrador.** Un trigger de base de datos lo impide. Cuando el trámite avanza no hay forma de completar el dato, así que la consulta en vivo no era una optimización opcional: era el único camino disponible en la mayor parte del ciclo de vida.

A esto se suma que, hasta HU #11132, la consulta real al RUES **no devolvía ningún dato** por una divergencia de contrato, de modo que el costo se pagaba sin obtener el beneficio.

## Decisión

El Certificado RUES se emite con **los datos consultados en el momento de registrar el trámite**, congelados y almacenados, y **no se vuelve a consultar al proveedor** para regenerar un documento.

- El snapshot se toma en `RuesPersonLookupHandler`, que es la costura por la que el asistente consulta el RUES al resolver el actor jurídico por NIT.
- Se guarda **por NIT**, en un único `field_value` (`rues_snapshots_json`) que indexa las compañías consultadas en ese trámite.
- Queda **congelado** de forma natural: al vivir en `field_values`, el trigger de borrador lo vuelve inmutable en cuanto el trámite avanza. Solo se reescribe si el operador vuelve a consultar ese NIT con el trámite aún en edición.
- La consulta en vivo se conserva **como respaldo registrado en bitácora**, para trámites anteriores a este cambio.

### Por qué por NIT y no por actor

El asistente consulta el RUES **mientras se diligencia** el formulario de actores, antes de que exista la fila del actor. En ese instante no hay a quién colgarle el dato; la única llave disponible es el NIT consultado. Al generar el expediente se cruza el NIT del actor contra el snapshot.

### Por qué un solo documento y no una llave por NIT

Un trámite puede tener dos personas jurídicas. Con llaves sueltas por NIT el número de llaves dependería de los datos; con un documento indexado, no. De paso corrige el defecto de multiplicidad descrito en el punto 2 del contexto.

## Alternativas consideradas

### Opción 1: snapshot en `field_values` indexado por NIT (elegida)

**Pros:** sin migración; hereda el congelado del trigger existente; soporta N compañías; el dato viaja con el trámite y es reproducible años después.
**Contras:** `field_values` es un saco genérico y esto es un documento estructurado dentro de un campo de texto.

### Opción 2: reusar `external_query_cache` (TTL de 24 h) como fuente del certificado

**Descartada.** Su TTL haría que el certificado dejara de ser reproducible al día siguiente: la misma regeneración daría un documento distinto según cuándo se ejecute. La caché conserva su propósito original —reusar la información dentro del asistente— y no es fuente documental.

### Opción 3: columna `metadata` del actor

**Descartada.** El actor no existe todavía en el instante de la consulta (ver arriba). Habría exigido una etapa de traspaso desde el formulario al guardar los actores, con más acoplamiento y un camino de fallo nuevo.

### Opción 4: tabla propia `procedure_instance_rues_snapshots`

Es la opción más limpia conceptualmente y sigue sobre la mesa si el volumen o las consultas analíticas lo justifican. Se descartó **por ahora** porque exige migración, repositorio y configuración para un dato que solo consume el generador del expediente, y porque la Opción 1 obtiene el congelado gratis.

## Consecuencias

**Positivas**
- Regenerar el expediente cuesta **cero** consultas al RUES.
- El certificado es reproducible: dice lo que se consultó al registrar, no lo que el RUES diga hoy.
- Desaparece la mezcla de compañías en trámites con dos personas jurídicas.

**Negativas / riesgos**
- El certificado puede quedar **desactualizado** respecto al RUES real. Es deliberado: un certificado da fe de una consulta fechada, y el snapshot guarda esa fecha.
- Si la consulta falla al registrar y el trámite avanza, **no hay segunda oportunidad** dentro de la ventana de edición. Por eso HU #11132 (corrección del contrato) es prerrequisito duro de HU #11133.
- Trámites anteriores a este cambio siguen dependiendo del respaldo en vivo hasta que se cierren.

## Qué NO decide este ADR

- **No** reactiva la lectura de la caché de 24 h en los pasos de vendedor y comprador del asistente. Ese reúso está hoy apagado por HU #10955 y su reactivación es una decisión pendiente del PO, con su propio alcance.
- **No** cambia la consulta de identidad de personas naturales.

## Referencias

- Plan técnico: `docs/plan-tecnico-tablas-certificadoras.md`
- Requisito fuente: `ajustes-tablas-certificadoras.txt`
- Feature #11131, HU #11132 (prerrequisito), HU #11133 (esta decisión)
- [ADR-0030] caché de consultas externas con TTL — se conserva íntegro para su propósito original
