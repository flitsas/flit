# Workflow: Decompose Feature

**Objetivo:** Tomar un Feature existente en ADO y producir sus Historias de Usuario con AC en Gherkin, Story Points y tipo (BACKEND/FRONTEND), listas para ser creadas en ADO.

**Invocación típica:**
```
Descompón el feature #4520 en historias de usuario
```

---

## Precondiciones

- El Feature existe en ADO con estado `New` o activo en el board.
- El Feature tiene título, descripción y criterios funcionales.
- Existe un diseño técnico aprobado (si no existe, recomendar ejecutar primero `requirement-to-delivery.md` Fases 1-2 [y 2b si hay schema] o pedir al architecture-agent un diseño).

---

## Fases — resumen

| # | Fase | Agente | Gate humano |
|---|------|--------|-------------|
| 1 | Validar DoR del Feature | `tech-lead-agent` (modo C) | — |
| 2 | Descomponer en HUs | `tech-lead-agent` (modo B) | Aprobación de las HUs antes de crearlas |
| 3 | Crear HUs en ADO | skill `flit-crear-hu` | — |

---

## Fase 1 — Validar DoR del Feature

**Agente:** `tech-lead-agent` (modo C)

**Instrucción:**
```
Usa el tech-lead-agent (modo C) para validar el DoR del Feature #[feature_id]
```

**Outputs esperados:**
- Veredicto: `OK_TO_TRANSITION` o `MISSING_N` con lista de campos faltantes

**Si DoR no pasa:** reportar al usuario los campos faltantes. No continuar hasta que el Feature esté completo. El orquestador puede sugerir invocar `tech-lead-agent` (modo A) para completarlo, pero no lo hace automáticamente.

---

## Fase 2 — Descomponer en HUs

**Agente:** `tech-lead-agent` (modo B)

**Instrucción:**
```
Usa el tech-lead-agent (modo B) para descomponer el Feature #[feature_id]
```

Si existe diseño técnico:
```
Usa el tech-lead-agent (modo B) para descomponer el Feature #[feature_id] usando el diseño en ADO Wiki (planification-wiki)
```

**Outputs esperados:**
- Lista de HUs con:
  - Título descriptivo
  - Tipo: `[BACKEND]` / `[FRONTEND]`
  - Descripción: Como [rol] / quiero [acción] / para [beneficio]
  - AC en formato Gherkin (Given / When / Then)
  - Story Points (Fibonacci: 1, 2, 3, 5, 8 — nunca 4, 6, 7 u otros)
  - Dependencias entre HUs si existen
- Si el diseño incluye entidades/tablas nuevas: HU `[BACKEND]` de schema/migración **antes** de HUs que consumen esos datos (agente: `database-agent`; omitir si Fase 2b de `requirement-to-delivery` ya materializó el DDL)
- Máximo 8 HUs por Feature. Si se necesitan más → proponer partir el Feature en dos.

**Gate:** Presentar la lista de HUs al usuario.
- Si aprobadas → continuar a Fase 3
- Si rechazadas → iterar con el tech-lead-agent con el feedback

---

## Fase 3 — Crear HUs en ADO

**Skill:** `flit-crear-hu`

Por cada HU aprobada:
```
Usa flit-crear-hu para crear la HU "[título]" como hija del Feature #[feature_id]
```

**Outputs esperados:**
- Cada HU creada en ADO con su ID
- Vinculada al Feature como `Child`
- Asignada al sprint siguiente (nunca al activo)
- Con todos los campos del paso anterior

**Trazabilidad:** Al terminar, publicar en Discussion del Feature:
```html
<div>[Orchestrator] Descomposición completada.<br/>
HUs creadas: [lista de IDs y títulos]<br/>
Siguiente: implementación. Usa "Implementa la historia #ID" para cada una.</div>
```

---

## Si falla alguna fase

| Situación | Acción |
|-----------|--------|
| DoR del Feature no pasa | Reportar campos faltantes; sugerir completar con tech-lead-agent modo A |
| Más de 8 HUs necesarias | Proponer al usuario partir el Feature en dos antes de continuar |
| HU rechazada en gate | Iterar con feedback; máx. 2 iteraciones antes de escalar al Líder Técnico |
| Error al crear en ADO | Entregar las HUs como borrador `.md` local; el usuario las crea manualmente |
