---
name: expert-doc-engine
description: >-
  Conocimiento operativo de la Resolución 20233040017145 de 2023 (Mintransporte)
  y del diligenciamiento del FUR (Anexo 46). Usar con el agente expert-doc-engine
  o cuando haya que dictaminar casillas, firmas y requisitos RNA/RNRS.
  Triggers: ExpertDocEngine, FUR, Formulario Único, Resolución 20233040017145,
  Anexo 46, casilla trámite solicitado, diligenciamiento FUR.
---

# Skill ExpertDocEngine

El agente **expert-doc-engine** (ExpertDocEngine) es el dueño de esta skill. Otros agentes **leen** el dictamen; no duplican la interpretación normativa.

## Pre-flight

1. Artefacto de casillas/observaciones: `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` (obligatorio)
2. PDF de la resolución: `docs/ot/resolutions/Resolución 20233040017145 de 2023 Ministerio de Transporte.pdf`
3. Ejemplares: `docs/ot/fur/`
4. Referencias de esta skill (`references/`)
5. Mapper FLIT: `FurFieldMapper.MarkTramite` (casillas que **sí** se pintan hoy; contrastar con el artefacto)

## Referencias

- `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` — **canónico**: numeral 3 en tres capas + literales de observaciones
- `references/resolucion-20233040017145.md` — mapa de artículos y reglas que FLIT usa a diario
- `references/fur-diligenciamiento.md` — rejilla 1–18, ejemplares y contraste con el overlay

## Procedimiento de dictamen

1. Clasificar el escenario (matrícula / traspaso / otro + simultáneos).
2. Citar el artículo de la resolución.
3. Elegir el PDF ejemplar más cercano por nombre de archivo.
4. Si la pregunta es “qué hace FLIT”, abrir el mapper y declarar gaps.
5. Entregar tabla casilla normativa | FLIT | evidencia (artículo o PDF).

## Prohibido

- Inventar casillas 6 o 14 (no hay tipo en catálogo).
- Exigir firma del locatario en traspaso unilateral (art. 5.3.2.2).
- Sustituir la resolución por un ADR o por un comentario de código.
