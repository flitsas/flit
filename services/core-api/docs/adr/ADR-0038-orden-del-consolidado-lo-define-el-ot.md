# ADR-0038: El orden del expediente consolidado lo define el Organismo de Tránsito

**Fecha**: 2026-07-31
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (pendiente), Product Owner
**Tags**: arquitectura, backend, modulo-tramites, modulo-admin-ot, documental

**Relacionado**: [ADR-0032-regeneracion-consolidado-tras-rechazo] (vigencia del consolidado), HU #10455 / #10522 / #10706 (órdenes por modalidad), HU #10926 / #10936 (escrituras en el expediente).

## Contexto

El PDF consolidado se arma con un orden de prelación que hoy vive **en código**, en tres listas
hardcodeadas: `TraspasoConsolidadoOrdering`, `MatriculaConsolidadoOrdering` y
`GenericConsolidadoOrdering`. El requerimiento del líder (`ot-y-rl.txt`, bloque OT) pide que sea el
organismo quien decida ese orden, arrastrando los documentos en su consola.

La infraestructura para hacerlo ya existía casi entera —tabla `admin.ot_document_precedence` con RLS
por tenant, endpoints `GET`/`PATCH /document-precedence`, y una pantalla con drag & drop y
reordenamiento por teclado WCAG— pero **desconectada**, con tres roturas:

1. `ListByProcedureTypeAsync` devolvía solo las filas ya persistidas y **nada las sembraba**: la
   pantalla salía vacía, y `ReorderBatchAsync` respondía 422 si no existía la fila. Inoperante.
2. El consolidado del **wizard** ignoraba la matriz por completo (usaba la lista hardcodeada); solo
   el **maestro** la consultaba.
3. Los documentos que **genera el sistema** (FUR, certificados, mandato, escrituras) no existían en
   `tramites.document_types`, y como `ot_document_precedence.document_type_id` es FK a esa tabla, no
   había forma de reordenarlos. Además `GenericConsolidadoOrdering.SelectByResolvedMatrix` anteponía
   una **cabecera fija** (`fur`, `licencia_transito`, certificados) antes de la matriz: aunque el OT
   configurara algo, **no podía mover el FUR de la primera página**, que es justo lo pedido.

Restricción dura: la obligatoriedad documental **no puede cambiar** (es otro eje, vive en
`procedure_document_requirements` + `document_requirement_overrides`), y ningún trámite en curso
puede ver alterado su expediente por esta HU.

## Decisión

**El orden del expediente lo define el OT; las listas hardcodeadas quedan como respaldo.**

1. Los documentos generados entran al **catálogo** (`document_types`) con una marca
   `is_system_generated` y un `generated_sort_order` que reproduce el orden vigente. La marca es
   **aditiva, no excluyente**: el checklist del gestor sigue saliendo de
   `procedure_document_requirements`, sin cambios.
2. La lista que el OT reordena es la **unión** matriz base ∪ generados ∪ overrides, deduplicada por
   documento; el `PATCH` es un **upsert**.
3. El consolidado —wizard y maestro— usa el orden configurado **solo cuando el OT lo configuró**. La
   lista vacía es la señal de respaldo: sin configuración, el expediente sale exactamente como hoy.
4. En el camino configurado **no se antepone la cabecera fija**: el FUR ocupa la posición que el
   organismo eligió.

## Alternativas consideradas

### Opción 1: Matriz configurable con respaldo por modalidad (elegida)

**Pros:**
- Riesgo acotado: un OT que no ha tocado nada conserva su expediente byte a byte. Los golden tests de
  traspaso y matrícula lo blindan.
- Reutiliza la tabla, los endpoints y la pantalla que ya existían; el trabajo es conectarlos.
- Un puerto propio (`IOtConfiguredDocumentOrderProvider`) separa "qué documentos se piden"
  (checklist) de "en qué orden se imprimen" (expediente). Mezclarlos en el resolutor de la matriz
  habría metido los generados en el checklist del gestor.
- Resolver la prelación **dentro** del handler del maestro hace que el envío por el canal de
  radicación (Quipux) salga con el mismo orden que la consola, sin tocar cada llamador.

**Cons:**
- Convive un segundo camino de ordenamiento mientras haya OT sin configurar; las listas hardcodeadas
  no se pueden borrar todavía.
- La cabecera fija sobrevive en el camino legado del maestro (`SelectByResolvedMatrix`), que es el
  que preserva la no-regresión.

**Esfuerzo:** M

### Opción 2: Migrar el orden hardcodeado a la matriz de todos los OT y borrar las listas

**Pros:** un único camino; el código queda limpio de golpe.

**Cons:** obliga a sembrar prelación para **todos** los OT y tipos de trámite; cualquier hueco del
seed cambia el expediente de un cliente en producción sin que nadie lo haya pedido. Y deja al
organismo con una configuración que él no creó, difícil de distinguir de una decisión suya.

**Esfuerzo:** L

### Opción 3: `is_system_generated` como "excluido del checklist"

**Pros:** una sola bandera resolvería a la vez el catálogo de generados y el filtro del checklist.

**Cons:** **rompe el requisito de obligatoriedad.** `compraventa` e `impronta` son a la vez
generados y documentos del checklist —`compraventa` es *obligatoria* en la matriz base de traspaso—,
así que leer la marca como exclusión los borraría del checklist del gestor.

**Esfuerzo:** S

## Consecuencias

**Positivas:**
- El OT arma su expediente sin pedir un despliegue; los documentos generados se reordenan igual que
  los adjuntos.
- El checklist y la obligatoriedad quedan intactos y con test de no regresión.
- La pantalla de prelación pasa a ser operativa: lista completa desde el primer día.

**Negativas / deuda:**
- Reordenar **no** regenera los expedientes ya emitidos (decisión D6): aplica desde la siguiente
  generación, y la pantalla lo advierte. Invalidar cruzaría N trámites de todos los clientes del OT.
- Quedan dos caminos de ordenamiento hasta que todos los OT configuren el suyo.
- La equivalencia código ↔ tipo de adjunto (`ConsolidadoDocumentCodeMap`) es una tabla manual: un
  generador nuevo que use un tipo distinto del código del catálogo debe registrarse ahí.
