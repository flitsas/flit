---
name: flit-adr-generator
description: Genera Architecture Decision Records (ADR) con formato Michael Nygard adaptado a FLIT, siempre con 2-3 alternativas y estado Propuesto. Usar cuando architecture-agent, tech-lead-agent o el usuario documenten decisiones técnicas, evalúen opciones o pregunten por qué se eligió X. Triggers ADR, decisión arquitectónica, documentar decisión, Propuesto, flit-adr-generator.
---

Plantilla completa en `references/plantilla-adr-flit.md`.

## Cuándo usar

- Registrar decisión: "ADR", "documentar esta decisión"
- Architecture Agent tras evaluar opciones
- Tech Lead durante descomposición de Feature
- Elección entre alternativas significativas (framework, BD, API)
- Consultar ADRs existentes ante "¿por qué elegimos X?"

**No usar** para decisiones operativas rutinarias (usar tech-lead modo D).

## Reglas innegociables FLIT

1. Estado por defecto: **`Propuesto`** — nunca `Aceptado` (solo el Líder Técnico humano en PR aparte).
2. Ruta preferida al versionar: `docs/decisions/ADR-NNNN-{kebab-case}.md` (si el directorio existe). Si no, borrador `.md` local + registro en ADO Discussion hasta versionar.
3. Numeración: 4 dígitos (`ADR-0001`)
4. Fecha ISO `YYYY-MM-DD`
5. **Siempre 2–3 alternativas** con pros/contras/effort/riesgos
6. Referencias: `[ADR-NNNN]` en código y PRs
7. Contenido en español; términos técnicos en inglés cuando sea estándar

## Pre-flight

1. Listar ADRs existentes en el repositorio: `ls **/ADR-*.md 2>/dev/null` o `rg -l '^# ADR-' .`
2. Listar ADRs existentes para número siguiente y contradicciones
3. Leer READMEs de servicios afectados

```bash
find . -name 'ADR-*.md' 2>/dev/null | sort
```

## Checklist

- [ ] Identificar decisión (una pregunta aclaratoria si es vago)
- [ ] Redactar 2–3 alternativas
- [ ] Recomendar una con tradeoff explícito
- [ ] Mostrar borrador completo al humano
- [ ] Tras aprobación: escribir archivo (en `docs/decisions/` si existe) o entregar borrador local; registrar en ADO Discussion

## Flujo

1. Contexto y restricciones
2. Alternativas (nunca una sola)
3. Recomendación con tradeoff concreto
4. Consecuencias operativas por agente (Backend, Frontend, QA, Security, Infra)
5. Preguntar: "¿Apruebas el borrador? Quedará en **Propuesto** hasta tu PR de aceptación."
6. Escribir solo tras confirmación

## Prohibido

- ADR en `Aceptado` sin proceso humano
- Una sola opción
- Escribir sin confirmación
- Datos sensibles de clientes
- Contradecir ADR `Aceptado` sin línea `Supersedes ADR-XXXX`
