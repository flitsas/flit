# ADR-0053: Múltiple Propietario — ordinal + porcentaje en el actor existente

**Fecha**: 2026-09-01
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente — aceptación del ADR). Product Owner: los 2 supuestos de la
versión inicial (duplicidad de dos niveles, alcance de "todos firman") **ya fueron confirmados por el
usuario** el 2026-09-01 y están incorporados en este texto — no quedan puntos pendientes de negocio
para Fase 1.
**Tags**: arquitectura, backend, frontend, modulo-tramites, modulo-identidad

## Contexto

`tramites.procedure_instance_actors` admite hoy **exactamente un actor por rol** (comprador,
vendedor, locatario) por trámite: lo impone el índice único
`(ProcedureInstanceId, ProcedureEntityId)` en BD y `Dictionary<ParteRol, Guid>`/`duplicate_rol` en
`ActorsCommand.cs`. El negocio necesita representar **copropiedad**: hasta 4 personas por lado
(comprador y/o vendedor, como repartos independientes) en matrícula inicial y traspaso, cada una con
un porcentaje de propiedad (2 decimales, suma exacta 100% por lado, ninguno en 0%), y cada una con su
**propio ciclo de identidad** (consulta RUNT/RUES, validación biométrica/VID, representante legal si
es jurídica) — sin validador "principal" que cubra al resto.

El barrido de impacto (ver nota de diseño técnico) encontró que la cardinalidad "un actor por rol"
está asumida en **~30 archivos** de `services/core-api`. La decisión de este ADR es de **modelo de
datos**; el barrido completo, los sequence diagrams y el contrato ampliado viven en
`docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md`.

**Documentos autogenerados:** este ADR decide el **modelo de actores**, no el layout de PDF. El
encargo original dejó FUR/consolidado/compraventa fuera de alcance. **HU #12048 (2026-09-02)** cerró
FUR overlay, compraventa, mandato y solicitud virtual para 2–4 copropietarios; consolidado, impronta,
RUES y escrituras siguen en `ordinal=1`. Ver
`docs/design/MULTIPLE-PROPIETARIO-documentos-autogenerados.md`. Este ADR **no** prescribe geometría de
overlay ni copy de QuestPDF.

## Decisión

**Ordinal + porcentaje en la tabla `procedure_instance_actors` existente** (Opción 1 abajo): cada
copropietario es una fila más de esa tabla, distinguida por `(ProcedureInstanceId,
ProcedureEntityId, ordinal)`, con `ownership_percentage` nullable (null cuando el lado tiene un solo
actor). El índice único que hoy impone "un actor por rol" se amplía con `ordinal`; la garantía de
"máximo 4 y suma=100" pasa a validarse en `Flit.Tramites.Application` (mismo patrón ya vigente para
vendedor≠comprador), no en un CHECK de fila.

## Alternativas consideradas

### Opción 1: `ordinal` + `ownership_percentage` en la tabla existente (elegida)

**Pros:**
- Migración puramente aditiva y reversible (`ALTER TABLE ... ADD COLUMN`); filas existentes quedan en
  `ordinal=1`, `ownership_percentage=NULL` por `DEFAULT` — cero impacto en trámites en curso.
- Cada copropietario sigue siendo una fila de `procedure_instance_actors`: identidad
  (`procedure_instance_biometric_validations`, ya correlacionada por `PartyRole` + `DocumentNumber`,
  no por FK a una fila de actor) sigue funcionando sin cambio de schema — ya admite N filas activas
  por `PartyRole` siempre que difieran en documento.
- El actor `ordinal=1` es indistinguible de un actor legacy: la familia de documentos singulares
  (FUR/consolidado, fuera de alcance) sigue leyendo exactamente lo mismo sin ningún cambio.
- Reutiliza el patrón ya vigente en el repo (`EffectiveParte`/`ValidateTraspasoPartes` en
  `ActorsCommand.cs`) para la validación de reparto sobre el conjunto EFECTIVO (request ∪
  conservados) — sin abstracción nueva.

**Cons:**
- El índice único deja de ser, por sí solo, la garantía de "máximo 1 actor por rol"; la garantía de
  negocio (máximo 4, suma=100, ninguno en 0) vive en aplicación, no en un CHECK de fila cruzando
  filas del mismo grupo.
- Un `CONSTRAINT TRIGGER` de BD que sumara el reparto en tiempo real complicaría el patrón de upsert
  de dos fases (DELETE + SaveChanges, luego INSERT + SaveChanges) que ya usa `PutActorsHandler`; se
  descarta a propósito (ver Tradeoff aceptado).

**Esfuerzo:** M
**Riesgos:** medio — el riesgo real no es el schema, es el barrido de ~30 consumidores que asumen "un
actor por rol" (documentado en la nota de diseño, §5).

### Opción 2: Tabla hija `procedure_instance_actor_shares`

Mantener `procedure_instance_actors` como el actor "representativo" (sin tocar su índice) y crear una
tabla nueva para el reparto (`ordinal` + `ownership_percentage`), 1:N desde la instancia.

**Pros:**
- No toca el índice único ni el significado actual de `procedure_instance_actors`; un rollback de la
  tabla nueva no puede degradar al actor principal.

**Cons:**
- Reintroduce el mismo problema que resuelve la Opción 1, pero peor: los ~30 consumidores igual
  necesitan saber que "el vendedor" puede ser 1..4 personas, solo que ahora vía JOIN a una tabla
  nueva. La identidad de cada copropietario necesita su propio documento — si ese documento vive en
  la tabla nueva, ESA es el actor de facto y la tabla vieja queda como alias redundante del
  `ordinal=1`. Dos tablas para un mismo concepto de dominio (persona-parte-de-un-trámite) sin caso de
  uso real que las necesite separadas (nunca hay un % sin saber de quién).

**Esfuerzo:** L
**Riesgos:** alto — separa un dato que el negocio trata como una sola entidad y duplica el trabajo de
JOIN en cada uno de los ~30 consumidores del barrido, sin reducir el riesgo real (que es de
comportamiento, no de schema).

### Opción 3: Actor "grupo" con `owners jsonb` embebido

Un solo actor por rol (sin tocar el índice único) con un array JSON de copropietarios embebido.

**Pros:**
- Cero cambio al índice único existente; cero riesgo de que un insert futuro cuele un actor de más.

**Cons:**
- Rompe la relación de identidad: `procedure_instance_biometric_validations` está pensada para
  correlacionarse con una FILA de actor por documento; meter copropietarios en JSON obliga a una
  segunda clave (rol + índice-en-JSON) que ningún consumidor de identidad/firma/notificación entiende
  hoy — contradice B1/B10 del checklist de schema (lógica de correlación sobre JSON no tipado). Un
  copropietario deja de tener PK, FK a `procedure_entities`, índices ni RLS propios, y el encargo pide
  explícitamente que "el flujo se replica ÍNTEGRO por cada actor" — eso exige fila, no elemento de
  array.

**Esfuerzo:** S (schema) mal compensado por L (todo lo demás, identidad en particular).
**Riesgos:** altísimo — se descarta.

## Tradeoff aceptado

Se prefiere la Opción 1 porque el dominio real es "un copropietario es un actor más, con una posición
y un reparto" — es literalmente la descripción del negocio ("hasta 4 propietarios por lado con
porcentaje") — y porque preserva sin tocar todo lo que ya está construido alrededor de "una fila de
`procedure_instance_actors` = una persona validable" (identidad, RUES/RUNT, consentimiento Habeas
Data). El costo — que la BD ya no sea la única barrera de "máximo N por rol" y que "suma=100" viva en
aplicación — se acepta porque **ya es el patrón vigente** del repo: `ValidateTraspasoPartes` (vendedor
≠ comprador) y `ValidateLocatario` (locatario ≠ propietario) son reglas de negocio sobre el conjunto
efectivo, evaluadas en `Flit.Tramites.Application`, nunca en un CHECK de fila. Un `CONSTRAINT TRIGGER`
deferrable que sumara automáticamente añadiría complejidad de BD para una garantía que la aplicación
ya debe dar de todos modos (el backend valida el resultado del PUT independientemente del frontend,
por requisito explícito del encargo), así que duplicarla en BD es defensa en profundidad de esfuerzo
alto para un riesgo que el patrón de upsert de dos fases ya hace improbable en la práctica (ambas
`SaveChanges` del PUT ocurren en la misma request/transacción lógica del handler).

## Consecuencias

### Lo que se gana
- Hasta 4 propietarios por lado, cada uno con su fila propia, su documento propio y su ciclo de
  identidad propio, sin romper ningún trámite existente (todos migran en `ordinal=1`,
  `ownership_percentage=NULL`, comportamiento idéntico al actual).
- Reutilización total del patrón de validación de conjunto efectivo ya vigente en `ActorsCommand.cs`
  para vendedor≠comprador/locatario≠propietario, extendido a duplicidad intra-lado y a suma=100.
- La familia de documentos autogenerados ya no es un bloque único: FUR, compraventa, mandato y
  solicitud virtual listan copropietarios (HU #12048). Consolidado, impronta, RUES y escrituras
  siguen en `ordinal=1` (brecha residual documentada en la nota de diseño).

### Lo que se pierde
- El índice único deja de ser, por sí mismo, la garantía completa de cardinalidad — la garantía de
  negocio (máximo 4, suma=100, ninguno en 0) depende de que `ActorsCommand.cs` la aplique siempre
  (mitigado: es el único punto de escritura de actores vía API; `SyncSellerActorFromConsultationsCommand`
  se ajusta para nunca escribir `ordinal>1`).
- ~18 archivos de `services/core-api` (identidad, gates, listados de biometría, firma diferida por
  actor y notificaciones) requieren cambios de cardinalidad (de "un actor" a "todos los actores del
  rol") para que Múltiple Propietario funcione de punta a punta — detallado y clasificado uno a uno en
  la nota de diseño técnico §5 (incluye las 3 confirmaciones del usuario: duplicidad de dos niveles,
  mecanismo de "todos firman" = identidad+baúl por actor, y notificaciones a todos los copropietarios).

### Cambios operacionales
- Migración EF Core: `ALTER TABLE tramites.procedure_instance_actors ADD COLUMN ordinal, ADD COLUMN
  ownership_percentage` + reemplazo del índice único + 2 CHECKs (`ordinal BETWEEN 1 AND 4`,
  `ownership_percentage` NULL o `(0, 100]`). Reversible con matiz: el `Down` solo es seguro si ninguna
  fila tiene `ordinal > 1` (documentar como rollback condicionado).
- Contrato `ActorInput`/`ActorDto` (`contracts/openapi/core-api.v1.yaml`, ya actualizado en la nota de
  diseño) gana `ordinal` y `porcentaje`, ambos opcionales/nullable — aditivo, no breaking para el caso
  de 1 actor por lado.

## ADRs relacionados

- Ninguno existente modela actores múltiples por rol; este ADR no supersede ningún ADR previo.
- Precedente de reglas de negocio sobre conjunto efectivo en `Application` (sin CHECK de BD):
  patrón ya vigente en `ActorsCommand.cs` (`ValidateTraspasoPartes`/`ValidateLocatario`), documentado
  aquí como el mismo criterio que valida suma=100.

## Notas para agentes

- **Database Agent**: aplicar la migración descrita en §Consecuencias / nota de diseño técnico §3.
  Confirmar que ningún seed/fixture existente tenga ya un `ordinal` implícito distinto de 1 antes de
  crear el índice nuevo. Checklist §A: A12 (nombres `ck__`/`uq_` siguen convención), A17 (Up/Down, Down
  condicionado).
- **Backend Agent**: implementar según la nota de diseño técnico §4 y §8. Los PDF de FUR/compraventa/
  mandato/solicitud virtual de copropiedad viven en la HU #12048 (no reabrir el modelo de actores).
  Consolidado/impronta/RUES/escrituras siguen `ordinal=1` hasta un encargo explícito. Reglas de negocio
  CONFIRMADAS a implementar sin necesidad de validación adicional: (1) duplicidad de dos niveles — intra-lado
  siempre bloqueada (`actor_duplicado_mismo_lado`), entre lados (`partes_duplicadas`) solo cuando
  ambos lados quedan en exactamente 1 actor efectivo (§4.4); (2) "todos firman" = la misma cobertura
  de identidad de `IdentityApprovalResolver` extendida a todos los actores del lado — persona natural
  con su identidad propia, persona jurídica con la firma del baúl de su RL (o la identidad del RL si
  el gestor eligió ese mecanismo) — sin pieza de firma nueva (§4.2/§4.3); `mandate_signers` no se toca;
  (3) `TramiteNotificationRecipientResolver.cs` notifica a todos los actores del rol — los proyectores
  de contenido del correo no requieren cambio (§5 #16-#18).
- **Frontend Agent**: `ActorsForm.tsx` gana pestañas por lado y bloque de porcentaje solo cuando
  `count>1`; reutilizar `rotuloDelActor` parametrizado por ordinal (nunca un literal fijo "Propietario
  N"); los dos mensajes de bloqueo son textualmente los del encargo, no paráfrasis.
- **QA Agent**: casos de frontera de cardinalidad (1↔2↔4 actores), redondeo de 2 decimales en la suma,
  identidad de un solo copropietario rechazada bloqueando el trámite completo, repartos independientes
  por lado en traspaso.
- **Security Agent**: cada copropietario agregado es PII nueva sujeta a Habeas Data; el consentimiento
  de reúso cross-trámite (ADR-0031) se captura por actor, no una vez por lado.
- **Infra Agent**: sin cambios de despliegue; migración EF Core estándar.

## Referencias externas

- Ninguna (regla de negocio interna, sin marco normativo específico más allá de Ley 1581 ya cubierta
  por las prácticas de Habeas Data existentes en el repo).
