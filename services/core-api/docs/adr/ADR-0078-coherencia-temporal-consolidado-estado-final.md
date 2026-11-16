# ADR-0078: Coherencia temporal del consolidado PDF en estado final (regeneración JIT)

**Fecha**: 2026-11-10  
**Status**: Propuesto  
**Deciders**: Líder Técnico FLIT, Tech Lead, Architecture  
**Tags**: arquitectura, backend, tramites, expediente-pdf, consolidado  
**Feature**: #14493  
**Relacionados**: Épica #12760, Feature #14205, PRs #748/#749/#750

## Contexto

La entrega del consolidado (`EntregarConsolidadoHandler`, HU #12785) trata los estados finales como inmutables: sirve el PDF existente sin regenerar (AC3), aunque la transición haya ejecutado `InvalidarConsolidados()`. La proyección `ConsolidadoVigencia` fuerza `vigente`+`definitivo` en final sin comparar `Consolidado*GeneradoEn` con el momento del cambio de estado.

En producción eso entrega PDFs con marca de agua del estado previo y puede omitir adjuntos posteriores al snapshot (p. ej. LT). La UX unificada (#14205) eliminó el override manual permanente, así que la corrección debe vivir en el backend de entrega.

No existe `StatusChangedAt` en `ProcedureInstance`; el historial de transiciones ya se persiste como eventos `cambio_estado` en `procedure_instance_events`.

## Decisión

Evaluar coherencia temporal en la ruta de entrega también en estado final: si `T_pdf < T_cambio_relevante` (o la bandera de vigencia está en false) y no aplican excepciones (migrado V1, Quipux `radicado_fijo`, `Source=user`), regenerar JIT con el fail-safe existente y servir el PDF nuevo como definitivo.

`T_cambio_relevante = max(CreatedAt del último evento cambio_estado, ExpedienteActualizadoEn?)`.  
`T_pdf = Consolidado*GeneradoEn` (fallback `UploadedAt` del adjunto).

**No** se añade columna `StatusChangedAt` en este ADR.

## Alternativas consideradas

### Opción 1: Gate temporal en entrega con sellos + historial (elegida)

**Pros:** sin migración; reutiliza #12790/#12798; corrige el síntoma en el punto único de lectura; compatible con botón único.  
**Cons:** query extra de eventos en casos stale; fallback para trámites sin historial.  
**Esfuerzo:** M  
**Riesgos:** latencia en primer Ver post-aprobación; legacy sin `GeneradoEn`.

### Opción 2: Columna `StatusChangedAt` denormalizada

**Pros:** comparación O(1); alineada a especificación externa literal.  
**Cons:** migración + backfill; duplica `cambio_estado`.  
**Esfuerzo:** L  
**Riesgos:** inconsistencia historial vs columna.

### Opción 3: Solo worker anticipado al entrar a final

**Pros:** entrega sin lógica nueva.  
**Cons:** carrera con el usuario; no repara stock ya final; no cumple el cambio de AC3.  
**Esfuerzo:** S  
**Riesgos:** alto residual en PDN.

## Tradeoff aceptado

Preferimos una lectura puntual del historial existente frente a una migración, porque el valor de negocio es corregir la entrega ahora y el dato temporal ya está en bitácora. La denormalización queda como mejora opcional futura.

## Consecuencias

### Lo que se gana
- PDF final coherente con estado/documentos actuales sin fricción UX.
- Misma ruta transparente «Ver consolidado».
- Fail-safe #12798 intacto.

### Lo que se pierde
- Garantía AC3 absoluta de “nunca regenerar en final” (se sustituye por “definitivo solo si temporalmente coherente”).
- Posible coste de CPU/storage en el primer Ver tras aprobación con PDF stale.

### Cambios operacionales
- Actualizar tests que fijaban “nunca regenera en final”.
- Matriz QA PDN post-deploy enfocada en marca de agua y LT.
- ADR queda **Propuesto** hasta aceptación explícita del Líder Técnico humano.

## ADRs relacionados

- ADR asociados a Épica #12760 / regeneración consolidado (vigencia, entrega, fail-safe).
- [ADR-0076] — conversión PDF ilegible (complementario; fuera de alcance de este Feature).

## Notas para agentes

- **Backend Agent**: implementar gate en `EntregarConsolidadoHandler` + helper de coherencia + ajuste `ConsolidadoVigencia`; tests Gherkin/AC.
- **Frontend Agent**: sin CTA nuevo; revisar proyección vigencia/comentarios si aplica.
- **QA Agent**: matriz estado final stale/fresco + excepciones + fallo JIT.
- **Security Agent**: sin superficie nueva; revisar bitácora sin PII.
- **Infra Agent**: promote estándar tras merge.
- **Database Agent**: NA.

## Referencias externas

- Feature ADO #14493
- Spec: `ESPECIFICACION_ARQUITECTURA_PDF_CONSOLIDADOS.md` (diagnóstico; Opción 2 del spec descartada a favor de historial)
- PR #750 (UX botón único)
