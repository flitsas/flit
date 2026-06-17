# Workflow: Review PR

**Objetivo:** Ejecutar el pipeline completo de revisión de un PR: code review formal + análisis de seguridad profundo, producir comentarios consolidados y emitir status check final.

**Invocación típica:**
```
Revisa el PR !456
```

---

## Precondiciones

- El PR existe y está abierto con target `develop`.
- El PR tiene vínculo a una HU en ADO.
- El diff del PR no supera 800 líneas (si supera, el code-review-agent lo bloqueará inmediatamente).

---

## Fases — resumen

| # | Fase | Agente | Gate humano |
|---|------|--------|-------------|
| 1 | Revisión formal de código | `code-review-agent` | — |
| 2 | Análisis de seguridad | `security-agent` | — |
| 3 | Consolidar y reportar | Orchestrator | — |

---

## Fase 1 — Revisión formal de código

**Agente:** `code-review-agent`

**Instrucción:**
```
Usa el code-review-agent en el PR !N
```

El agente evalúa en 6 dimensiones (convenciones, ADRs, calidad, cobertura AC→tests, seguridad inline, metadata) y emite:
- Status: `pass` o `fail`
- Comentario consolidado con estructura: ✅ / 🚨 / 🚫 / 💡 / 📊

**Outputs esperados:**
- Status check documentado
- Comentario publicado en el PR (o redactado para publicación)

**Restricciones del agente — el orquestador no debe pedirle:**
- Modificar código para corregir los hallazgos → eso es el implementador
- Hacer merge del PR → eso es el integration-agent con gate humano
- Ejecutar SAST o gitleaks → eso es el security-agent

---

## Fase 2 — Análisis de seguridad

**Agente:** `security-agent`

**Instrucción:**
```
Usa el security-agent en el PR !N
```

El agente ejecuta 4 capas: SAST, SCA (dependencias), secretos (gitleaks), Habeas Data (Ley 1581).

**Outputs esperados:**
- Reporte con tabla por capa: Critical / High / Medium / Low
- Status: `PASS` / `FAIL` / `FAIL-WITH-EXCEPTIONS`
- Si hay secretos detectados: bloqueo absoluto + notificación inmediata al Líder Técnico

**Restricciones del agente — el orquestador no debe pedirle:**
- Modificar el código para corregir vulnerabilidades → eso es el implementador
- Aprobar el PR → solo emite status check
- Detectar patrones inline sin herramientas externas → eso es el code-review-agent

---

## Fase 3 — Consolidar y reportar

El orquestador consolida los dos resultados y reporta al usuario:

```
## Resultado del Review — PR !N

| Dimensión      | Estado |
|----------------|--------|
| Code Review    | ✅ PASS / ❌ FAIL |
| Security       | ✅ PASS / ❌ FAIL |

### Veredicto final
- ✅ LISTO PARA MERGE — no hay bloqueantes
- ❌ BLOQUEADO — [N] bloqueante(s) pendiente(s) de resolver:
  - [lista de bloqueantes con agente responsable de corregir]
```

**Si el veredicto es LISTO PARA MERGE:**
- Informar al usuario que puede proceder con el merge
- El merge NO se hace aquí; se hace en el workflow `implement-story.md` Fase 6 con su gate

**Si hay bloqueantes:**
- Informar al implementador original con el detalle
- El review se repite cuando el implementador suba nuevos commits al PR
- El orquestador NO corrige los bloqueantes por su cuenta

---

## Si falla alguna fase

| Situación | Acción |
|-----------|--------|
| PR supera 800 líneas | El code-review-agent emite FAIL automático. Informar al implementador para partir el PR. |
| Security detecta secretos | Bloqueo absoluto. Notificar al Líder Técnico. No continuar con ningún otro paso. |
| PR no tiene vínculo a HU | El code-review-agent emite FAIL. Informar al implementador para agregar el vínculo. |
