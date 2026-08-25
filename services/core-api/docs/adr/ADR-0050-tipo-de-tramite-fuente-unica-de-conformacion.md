# ADR-0050 — El tipo de trámite es la única fuente de verdad de la conformación del expediente

- **Estado**: Propuesto · 2026-08-21
- **Módulo**: Trámites — dominio, conformación y creación (`Flit.Tramites.Domain`, `Flit.Tramites.Application`, `Flit.Infrastructure/Persistence`, `frontend/components/operacion`)
- **Feature/Bug**: habilitación dinámica de tipos de trámite (base: `docs/diagnostico-habilitacion-dinamica-tipos-tramite.md`)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, frontend, datos, tramites, parametrizacion, feature-08

## Contexto

FLIT clasifica un expediente con **tres vocabularios paralelos que no coinciden**:

| Eje | Valores | Dónde vive |
|---|---|---|
| `procedure_types.family` | `MATRICULAS`, `TRASPASO`, `OTROS` | catálogo (21 tipos) |
| `procedure_instances.modalidad_entrada` | `matricula_inicial`, `traspaso` | instancia, `varchar(20)` sin CHECK |
| `procedure_instances.tipologia_codigo` | `matricula_inicial`, `traspaso_standard` | instancia |

`TipologiaResolver.FromFamily` traduce del primero al segundo colapsando `MATRICULAS`, `OTROS` y
cualquier familia desconocida en `matricula_inicial`. Un `BLINDAJE` o un `DUPLICADO_PLACA` nace con
FK correcto pero con el wizard, el checklist, el FUR, la portada, el mandato y las causales de
rechazo de una matrícula inicial. El colapso no es hipotético: `78-seed-tipos-tramite-leasing.sql`
ya publicó `MATRICULA_LEASING`, `TRASPASO_UNILATERAL` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO`, que hoy
se comportan en runtime como sus canónicos.

El techo estructural no es la columna: es `TipologiaMatrizCatalog`, que solo admite dos recorridos y
codifica la invariante `esperados = Traspaso ? 6 : 5`. Ampliar el vocabulario sin tocarlo no habilita
ningún tipo nuevo.

**FEATURE-08 ya construyó el motor que resuelve esto** y quedó a medias: `DynamicGateEvaluator`
(8 de 9 `section_type`), `gate_profile` tipado, `procedure_type_snapshots` —que **ya escribe en
producción hoy** en cada creación— y 30 tests están mergeados detrás del flag
`F08_DynamicProcedures`. El flag no se puede encender: `MATRICULA_NUEVA` y `TRASPASO_STANDARD` solo
tienen una sección `generic_form` heredada de seeds DEV, así que encenderlo degradaría el wizard a un
paso siempre completo. El frontend (`SectionRendererRegistry`) nunca se mergeó ni se conectó al
wizard. El ADR de convergencia que el propio DDL exige desde julio nunca se escribió — este documento
lo salda.

## Decisión

**`tramites.procedure_types` es la única fuente de verdad de la conformación de un expediente.**
La instancia deja de tener clasificación propia: `modalidad_entrada` y `tipologia_codigo` se
eliminan. Lo que hoy derivan otros ejes pasa a derivarse del tipo:

- **Clasificación** → `procedure_types.family`, con `CHECK` de tres valores.
- **Tipología** → `procedure_types.code`. Se elimina `TramiteTipologiaCatalog`.
- **Recorrido del wizard** → `procedure_steps` / `procedure_sections.section_type`, evaluado por
  `DynamicGateEvaluator`. Se elimina `TipologiaMatrizCatalog` con su invariante de 5/6 pasos.
- **Reglas de conformación** → `gate_profile` (`entryMode`, `requiresSeller/Buyer`,
  `requiresBiometrics`, …), en lugar de los ternarios binarios por modalidad.
- **Congelado por expediente** → `procedure_type_snapshots`, que ya captura
  `code/name/family/gateProfile/stepSectionTypes` al crear. Un cambio de catálogo no reclasifica
  expedientes existentes.
- **Barrera de operación** → nueva columna `procedure_types.wizard_enabled boolean NOT NULL DEFAULT
  false`, separada de `publication_status`: *visible en administración* deja de significar *operable
  en creación*.

El motor dinámico de FEATURE-08 pasa a ser el **único** camino: se elimina la rama estática de
`WizardStateQuery` y con ella el flag `F08_DynamicProcedures`.

## Alternativas consideradas

### Opción 1: Ampliar `modalidad_entrada` a tres valores

Añadir `otros` al enum y a la columna, manteniendo el motor estático y añadiendo journeys a
`TipologiaMatrizCatalog`.

**Pros:**
- Cambio incremental, sin migración de datos ni reescritura de los tres triggers de BD.
- No rompe contratos de API ni los clientes que filtran por `matricula_inicial`.
- Entrega valor rápido en filtros, listados y causales.

**Cons:**
- Deja vivos los tres vocabularios y la traducción entre ellos.
- No habilita ningún tipo nuevo: `TipologiaMatrizCatalog` sigue admitiendo dos recorridos.
- Obliga a mantener dos motores en el repo (estático + F08 dormido) indefinidamente.
- Cada tipo nuevo sigue siendo un deploy, que es justo lo que el requerimiento prohíbe.

**Esfuerzo:** M · **Riesgos:** resuelve la clasificación y deja intacto el problema real.

### Opción 2: Convergencia total en `procedure_types` sobre FEATURE-08 *(elegida)*

**Pros:**
- Un solo vocabulario y un solo motor; habilitar un tipo pasa a ser configuración.
- Aprovecha ~85% del backend de F08 ya escrito y testeado, y una pieza que ya corre en producción.
- Las 16 ramas por código canónico de `FurNumeral3Marks`, hoy inalcanzables tras el fallback,
  empiezan a funcionar sin escribirlas.
- Coherente con ADR-0021 (una sola fuente de verdad) y con ADR-0037 (congelar por trámite).

**Cons:**
- Alcance grande: dominio, esquema, frontend, ICT, Quipux y analítica.
- Exige el reset del esquema `tramites` para no arrastrar el modelo viejo.
- El frontend no tiene código previo utilizable: la integración registry ↔ wizard no existe.

**Esfuerzo:** L · **Riesgos:** regresión en los dos flujos vivos si el motor dinámico no alcanza
paridad antes de retirar el estático.

### Opción 3: Motor nuevo parametrizado desde cero

Retirar el andamiaje de F08 y rediseñar la conformación dinámica con lo aprendido.

**Pros:**
- Libertad de diseño sin heredar decisiones de julio.
- Permite corregir de raíz lo que hoy son parches (truncado a `sectionTypes[0]`, `prenda_decision`
  vacío).

**Cons:**
- Descarta 30 tests y una pieza en producción sin defecto conocido.
- Reconstruye un vocabulario de secciones ya acordado con el DDL y el contrato OpenAPI.
- Máximo costo para llegar al mismo sitio.

**Esfuerzo:** L+ · **Riesgos:** rehacer con distinta forma lo que ya funciona.

## Tradeoff aceptado

Se acepta un alcance grande y un reset del esquema a cambio de eliminar la causa, no el síntoma. La
Opción 1 es más barata pero deja intacto el techo estructural: seguiría siendo imposible habilitar un
tipo sin deploy, que es el requisito de negocio. La Opción 3 paga el mismo costo que la 2 y además
tira trabajo válido.

El reset es viable **solo** porque no hay expedientes reales que conservar en ningún ambiente. Esa es
la condición que hace defendible la decisión; si dejara de cumplirse, este ADR debe revisarse antes
de ejecutar la migración.

Se acepta también perder la trazabilidad histórica de `modalidad_entrada` en reportes: al no haber
datos que migrar, no hay serie histórica que romper.

## Consecuencias

### Lo que se gana
- Habilitar un tipo de trámite es un `UPDATE` más datos de parametrización, no un despliegue.
- Un solo vocabulario: desaparecen los cuatro criterios de parseo que hoy conviven para los mismos
  dos strings (`FromCode` case-sensitive, `EsMatriculaInicial` tolerante, `EsValida` Ordinal,
  `FromFamily` OrdinalIgnoreCase).
- Los documentos legales dejan de mentir: portada, mandato y solicitud virtual rotulan el `name` real
  en vez de "MATRÍCULA INICIAL" por defecto.
- `OTROS` deja de disfrazarse de matrícula en clasificación, reportes, rechazo y radicación.
- Un solo motor de wizard, sin bifurcación por flag.

### Lo que se pierde
- Todos los expedientes existentes en DEV, QA y producción.
- La compatibilidad del contrato: los clientes que envían o filtran por `modalidad` dejan de
  funcionar. Backend y frontend quedan acoplados en el despliegue.
- La red de seguridad del flag: al eliminar la rama estática no hay a dónde volver sin revertir código.

### Cambios operacionales
- **Migración destructiva con puerta humana.** El `DROP SCHEMA tramites CASCADE` arrastra objetos de
  `analytics`, `catalogs` y `admin`, que deben recrearse en la misma migración. No debe ejecutarse por
  `Database:AutoMigrate` sin aprobación explícita y respaldo verificado.
- Los tres triggers que comparan el literal de modalidad se reescriben contra `family`.
- `catalogs.rejection_reasons.modalidad` pasa a `family` con `CHECK` de tres valores.
- Habilitar un tipo exige checklist previo: parametrización de pasos, matriz documental, causales, y
  homologación Quipux/ICT cuando aplique. `wizard_enabled` permanece en `false` hasta cerrarlo.
- `UpsertProcedureSteps` debe dejar de hacer *replace* destructivo: hoy el primer `PUT /steps` desde
  el configurador borraría los `section_type` sembrados.

## ADRs relacionados

- **ADR-0021** (Aceptado) — establece "una sola fuente de verdad" para analítica; esta decisión aplica
  el mismo principio a la conformación del trámite.
- **ADR-0037** — precedente de congelar datos por trámite; `procedure_type_snapshots` cumple aquí el
  mismo papel que el snapshot RUES.
- **ADR-0019** — `procedure_types` es catálogo global administrado por SuperAdmin, sin `tenant_id`;
  esta decisión lo confirma y lo convierte en la fuente de conformación.
- **ADR-0022** — estados de negocio del trámite; no cambia, pero sus gates dejan de consultar modalidad.
- **ADR-0038** — el orden del consolidado lo define el OT; `ConsolidadoOrderingResolver` pasa a
  resolver por familia en vez de por modalidad.
- **ADR-0036** — mandato por OT; el rótulo del mandato pasa a derivarse del `name` del tipo.

## Notas para agentes

- **Database Agent**: baseline `Ddl/79-tramites-baseline-v2.sql` con las 49 tablas del esquema;
  `procedure_instances` sin `modalidad_entrada` ni `tipologia_codigo`; `procedure_types` con
  `wizard_enabled` y `CHECK` sobre `family`; recrear los objetos dependientes de `analytics`,
  `catalogs` y `admin` que el CASCADE tumba. Migración con `Up`/`Down` y veredicto
  `OK_TO_MERGE_DB` antes del merge.
- **Backend Agent**: eliminar `TramiteModalidadEntrada`, `TramiteTipologiaCatalog`,
  `TipologiaResolver` y `TipologiaMatrizCatalog`; `ProcedureFamily` pasa a enum con un único parser;
  cerrar los gaps de F08 (`DocumentRequirements` en el contexto, `prenda_decision`, truncado a
  `sectionTypes[0]`, `SectionConfig` sin asignar) **antes** de retirar la rama estática.
- **Frontend Agent**: `WizardModalidad` es un tipo cerrado — `tsc --noEmit` enumera los sitios a
  migrar y sirve como criterio de avance. La creación pasa a `procedureTypeCode`, que hoy no existe
  en el cliente.
- **QA Agent**: el criterio no negociable es la paridad de `MATRICULA_NUEVA` (5 pasos, VIN-first) y
  `TRASPASO_STANDARD` (6 pasos, placa-first) tras migrar al motor dinámico.
- **Security Agent**: el reset recrea RLS y triggers de auditoría; verificar `tenant_isolation` en
  todas las tablas del baseline, en particular `procedure_type_snapshots`.
- **Infra Agent**: la migración destructiva no debe correr automáticamente al arrancar; requiere
  respaldo verificado y aprobación explícita.

## Referencias externas

- `docs/diagnostico-habilitacion-dinamica-tipos-tramite.md` — diagnóstico de origen (2026-08-21)
- `services/core-api/docs/schema/ddl/05-F08-conformation-profile.sql` — esquema de `gate_profile` y
  catálogo cerrado de `section_type`; el TODO de sus líneas 18-19 es lo que este ADR cierra
