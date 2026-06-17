# Workflow: Deploy Environment

**Objetivo:** Desplegar a un ambiente (DEV, QA o PDN) verificando precondiciones, ejecutando el deploy y registrando el resultado.

**Invocación típica:**
```
Despliega a DEV
Despliega a QA el build #890
Despliega a producción
```

---

## Precondiciones por ambiente

| Ambiente | Precondiciones |
|----------|----------------|
| **DEV** | PR mergeado en `develop` |
| **QA** | Deploy DEV exitoso + HU en `Resolved` + TCs generados por QA |
| **PDN** | Deploy QA exitoso + QA certificó la HU (tag `QA_PDN`) + aprobación del Líder Técnico |

---

## Fases — resumen

| # | Fase | Agente | Gate humano |
|---|------|--------|-------------|
| 1 | Verificar precondiciones | Orchestrator | — |
| 2 | Ejecutar deploy | `infra-agent` | Confirmación para QA y PDN |
| 3 | Registrar resultado en ADO | skill `flit-integration-ado` | — |

---

## Fase 1 — Verificar precondiciones

El orquestador verifica según el ambiente destino:

**Para DEV:**
- ¿El PR objetivo está mergeado en `develop`?
- ¿El CI del commit de merge está en verde?

**Para QA:**
- ¿DEV está estable y el deploy DEV fue exitoso?
- ¿Las HUs relacionadas están en estado `Resolved`?
- ¿Existen TCs creados por el qa-agent?

**Para PDN:**
- ¿El qa-agent certificó con tag `QA_PDN`?
- ¿El Líder Técnico humano aprobó el paso a producción?

Si alguna precondición no se cumple → detener, reportar cuál falta, no continuar.

---

## Fase 2 — Ejecutar deploy

**Agente:** `infra-agent`

**Para DEV (sin gate adicional si las precondiciones pasan):**
```
Usa el infra-agent para desplegar a DEV — commit: [sha o build]
```

**Para QA y PDN (requiere confirmación explícita):**

Mostrar al usuario antes de ejecutar:
```
Ambiente destino: [QA / PDN]
Build / commit: [referencia]
Precondiciones: ✅ todas cumplidas

¿Confirmas el deploy? (sí / no)
```

Si "sí":
```
Usa el infra-agent para desplegar a [ambiente] — build: [referencia]
```

**Outputs esperados:**
- Deploy exitoso con URL del ambiente
- Healthcheck en verde
- Tiempo de deploy registrado

---

## Fase 3 — Registrar resultado en ADO

**Skill:** `flit-integration-ado`

```
Usa flit-integration-ado para registrar el deploy a [ambiente] del Feature/HU #[id] — URL: [url], build: [referencia]
```

**Outputs esperados:**
- Campo `Custom.Deploy[ENV]` = `true` en el work item
- Comentario en Discussion con URL del ambiente y timestamp

---

## Si falla alguna fase

| Situación | Acción |
|-----------|--------|
| Precondición de QA/PDN no cumplida | Reportar cuál falta; no intentar el deploy |
| Deploy falla | Invocar `infra-agent` para diagnóstico; no reintentar automáticamente; evaluar rollback |
| Healthcheck falla post-deploy | Informar al Líder Técnico; el orquestador no decide el rollback — lo propone |
| Rollback necesario | Invocar `flit-rollback-procedure` y escalar al Líder Técnico humano para decisión final |

---

## Restricciones del flujo

- El orquestador nunca ordena un deploy a PDN sin aprobación explícita del Líder Técnico humano.
- El orquestador nunca omite la verificación de precondiciones "para ahorrar tiempo".
- El rollback a producción siempre requiere decisión humana — el orquestador solo lo propone y ejecuta el procedimiento si el humano confirma.
