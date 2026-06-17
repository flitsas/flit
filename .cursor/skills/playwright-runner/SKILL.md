---
name: playwright-runner
description: Ejecuta suites E2E con Playwright en modo evidencia completa (screenshots por paso, video del flujo, reporte publicado en Azure DevOps). Incluye previsualización local con navegador visible (--headed) cuando el QA humano lo solicite, antes de la corrida oficial con evidencia. Usar cuando el qa-agent o el usuario necesiten ejecutar TCs automatizados de una HU en ambiente DEV o QA, correr una suite de regresión entregada por regression-selector, o generar evidencia estructurada por TC lista para ADO. Si un TC falla activa bug-reporter. Triggers playwright-runner, ejecutar TCs, ejecutar suite, evidencia E2E, QaCapture, spec files, screenshots, video flujo, correr pruebas, Modo B qa-agent, headed, navegador visible, demo local.
---

# playwright-runner

Ejecuta suites E2E con Playwright en modo evidencia completa: screenshots por paso, video del flujo y documentación publicada en Azure DevOps. En **máquina local del QA** el navegador debe ser **visible** (`--headed`) cuando el humano supervise o pida ver el flujo; en **CI** puede correr headless si no hay display. Funciona en cualquier proyecto — genera y limpia su propia infraestructura temporal.

**Posición en el pipeline QA:**
```
regression-selector ──► playwright-runner ──► TC Pass → evidencia en ADO
                                          └──► TC Fail → bug-reporter
```

## Inputs requeridos

| Campo        | Requerido   | Descripción |
|--------------|-------------|-------------|
| `hu_id`      | Siempre     | ID de la HU cuyos TCs se van a ejecutar |
| `tcs`        | Siempre     | Lista de TCs (Tasks hijo de la HU) con sus spec files |
| `ambiente`   | Siempre     | `DEV` / `QA` — nunca Producción sin autorización |
| `base_url`   | Siempre     | URL base del ambiente (ej. `http://localhost:5173`) |
| `spec_files` | Condicional | Archivos `.spec.ts` a ejecutar — si no existen, generarlos primero |

## Credenciales ADO

Leer de `.env.user-identity`: `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`, `AZURE_PAT`, `USER_REAL_NAME`, `USER_REAL_EMAIL`.
Si el archivo no existe o falta alguna variable, reportar al qa-agent y detenerse.

---

## Proceso

### PASO 0 — Gate y preparación ADO (obligatorio antes de ejecutar)

**0a. Verificar HU en `Resolved` (gate — no negociable):**

```text
wit_get_work_item(hu_id) → System.State
```

| Estado HU | Acción |
|-----------|--------|
| `Resolved` | Continuar |
| Cualquier otro | **DETENER** — reportar al qa-agent. El QA **nunca** transiciona la HU a `Resolved`. |

**0b. Activar solo los TCs que se van a ejecutar en esta corrida:**

Inmediatamente antes del PASO 4, por cada Task/TC a ejecutar:

| Campo | Valor |
|-------|-------|
| `System.AssignedTo` | QA responsable (`USER_REAL_EMAIL` de `.env.user-identity`) |
| `System.State` | `New` → **`Active`** (solo si va a ejecutarse ahora) |

```text
wit_update_work_items_batch (solo TCs de esta corrida):
  System.AssignedTo = {qa_email}
  System.State = Active
```

> No activar TCs que no se ejecutarán en la sesión actual. No modificar `System.State` ni `System.AssignedTo` de la HU padre.

### PASO 1 — Detectar configuración del proyecto

```bash
ls node_modules/.bin/playwright 2>/dev/null && echo "OK" || echo "NOT_FOUND"
ls playwright.config.* 2>/dev/null
ls e2e/*.spec.ts tests/*.spec.ts 2>/dev/null
```

Si Playwright no está instalado, reportar: `"El proyecto no tiene Playwright. Instalar con: npm install -D @playwright/test && npx playwright install chromium"`. No continuar hasta que esté disponible.

### PASO 2 — Generar infraestructura temporal de evidencia

Crear los siguientes archivos con prefijo `_qa_evidence_` antes de ejecutar. Se eliminan al final (PASO 6).

**Si el spec no existe**, generarlo usando los AC Gherkin de la HU. El spec generado siempre debe usar `QaCapture`.
Ver plantilla completa en [`./qa-evidence-templates.md`](./qa-evidence-templates.md).

#### 2a. `playwright.evidence.config.ts` (raíz del proyecto)

Extiende el config existente sin modificarlo. Si el proyecto no tiene config, crear uno mínimo desde cero.
Ver ambas variantes en [`./qa-evidence-templates.md`](./qa-evidence-templates.md).

**Obligatorio en el config temporal:** `use.headless: false` (copiar la plantilla tal cual). **No** usar `QA_HEADED` ni lógica invertida (`headless: true` por defecto). El CLI del PASO 4 siempre incluye `--headed` en local; en CI con `CI=true` se permite `headless: true` solo si el pipeline no tiene entorno gráfico.

#### 2b. `e2e/_qa_evidence_capture.ts` — clase `QaCapture`

Helper que envuelve cada paso en `ev.step(label, fn)` y toma screenshots automáticos por paso.
Código completo en [`./qa-evidence-templates.md`](./qa-evidence-templates.md).

#### 2c. `e2e/_qa_evidence_reporter.ts` — clase `QaEvidenceReporter`

Reporter personalizado que genera evidencia local temporal (`qa-evidence/{fecha}/HU-{id}/INDEX.md`) y publica en ADO.
Código completo en [`./qa-evidence-templates.md`](./qa-evidence-templates.md).

### PASO 3 — Agregar reporter al config de evidencia

Verificar que `playwright.evidence.config.ts` incluya la línea del reporter temporal:

```typescript
['./e2e/_qa_evidence_reporter.ts'],
```

### PASO 3b — Previsualización local con navegador visible (opcional, antes de evidencia ADO)

**Cuándo ejecutar:** el QA humano, el supervisor o el usuario piden ver el flujo en su máquina (*"quiero ver el navegador"*, *"ejecuta en headed"*, *"demo local"*, etc.). El **qa-agent** debe ofrecer o ejecutar este paso **antes** de la corrida oficial (PASO 4) si lo solicitan.

**Qué hace:** corre los mismos `spec_files` con el **config base del proyecto** (`playwright.config.ts`), **sin** `playwright.evidence.config.ts`, **sin** reporter `_qa_evidence_*` y **sin** publicar en ADO. **No** activar ni cerrar TCs en ADO en este paso.

```bash
cd {directorio_frontend_o_raíz_con_playwright}
npx playwright test {spec_files} \
  --headed \
  --project=chromium
```

Un solo TC (recomendado para demo):

```bash
npx playwright test {spec_files} --headed --grep "QA_TC01"
```

Modo paso a paso (depuración visual):

```bash
npx playwright test {spec_files} --headed --grep "QA_TC01" --debug
```

| Aspecto | Previsualización (3b) | Corrida oficial (PASO 4) |
|---------|----------------------|---------------------------|
| Config | `playwright.config.ts` | `playwright.evidence.config.ts` |
| Navegador local | **Siempre `--headed`** | **`--headed`** + `headless: false` en config |
| ADO | No publica | Publica evidencia (reporter) |
| TCs en ADO | Sin cambios de estado | `Active` → `Closed` según resultado |

> Tras confirmación visual del humano, continuar con PASO 0b y PASO 4–7 para evidencia y cierre formal.

### PASO 4 — Ejecutar con modo evidencia

```bash
QA_HU_ID={hu_id} \
QA_BUILD={build_o_version} \
QA_AMBIENTE={DEV|QA} \
AZURE_ORG_URL={org_url} \
AZURE_PROJECT_NAME={project_name} \
AZURE_PAT={pat} \
npx playwright test {spec_files} \
  --config=playwright.evidence.config.ts \
  --headed \
  --project=chromium
```

En **máquina local del QA**: **siempre** `--headed` (nunca omitir). En **CI** sin display: omitir `--headed` y usar `headless: true` en el config temporal solo si `CI=true`.

Para ejecutar un TC específico:
```bash
npx playwright test --config=playwright.evidence.config.ts --headed --grep "QA_TC01"
```

### PASO 5 — Verificar evidencia generada

```bash
ls qa-evidence/{fecha}/HU-{hu_id}/ 2>/dev/null || ls test-results/
npx playwright show-report
npx playwright show-trace test-results/{carpeta-tc}/trace.zip
```

### PASO 6 — Cerrar ciclo ADO según resultado

Tras publicar evidencia (PASO 5), actualizar work items. **La HU padre nunca cambia de estado** — solo tags y comentarios.

**Todos los TCs Pass (QA_PDN):**

| Work item | Acción |
|-----------|--------|
| Cada Task/TC ejecutado | `Active` → **`Closed`**; tag **`QA_PDN`**; `AssignedTo` = QA |
| HU padre | Agregar tag **`QA_PDN`** (`System.Tags`); comentario certificación en Discussion |
| HU padre | Actualizar `Custom.Evidences` con resumen E2E |
| HU padre | **`System.State` sin cambio** — permanece `Resolved` |

**Algún TC Fail (QA_NOVEDAD):**

| Work item | Acción |
|-----------|--------|
| TC fallido | Permanece **`Active`**; tag **`QA_NOVEDAD`**; evidencia en Discussion |
| TCs que pasaron | `Active` → **`Closed`**; tag **`QA_PDN`** |
| HU padre | Agregar tag **`QA_NOVEDAD`**; comentario con TCs fallidos |
| HU padre | **`System.State` sin cambio** — permanece `Resolved` |
| Bug | Activar **`bug-reporter`**: Bug **hijo** de la HU, Repro Steps completos, **`AssignedTo` = `System.AssignedTo` de la HU padre**. Producción → Líder Técnico. |

```text
wit_update_work_items_batch (Pass):
  TC: System.State = Closed, System.Tags += QA_PDN

wit_update_work_items_batch (Fail parcial):
  TC pass: Closed + tag QA_PDN
  TC fail: Active + tag QA_NOVEDAD
  HU: System.Tags += QA_NOVEDAD  (NO cambiar System.State)

bug-reporter (Fail):
  wit_add_child_work_items(parentId=hu_id, workItemType=Bug)
  AssignedTo = HU.System.AssignedTo
```

### PASO 7 — Limpiar archivos temporales

```bash
rm -f playwright.evidence.config.ts
rm -f e2e/_qa_evidence_capture.ts
rm -f e2e/_qa_evidence_reporter.ts
# Si se creó playwright.config.ts desde cero porque no existía, conservarlo
```

---

## Estructura de evidencia generada

```
qa-evidence/          # temporal, gitignored — no versionar
└── {YYYY-MM-DD}/
    └── HU-{hu_id}/
        ├── INDEX.md
        ├── QA_TC01.md
        ├── QA_TC01_step01_navegar.png
        ├── QA_TC01_video.webm
        └── ...
```

## Publicación en ADO

| Destino | Qué se publica |
|---------|---------------|
| HU (work item padre) | Comentario resumen con tabla de todos los TCs + `INDEX.md` adjunto |
| Cada Task/TC (hijo) | Comentario con resultado, screenshots embebidos como `<img>` y video como adjunto descargable |

> Screenshots visibles directamente en ADO. Videos son adjuntos descargables (WebM no es reproducible inline).
> Si no hay credenciales ADO o `QA_HU_ID`, solo genera documentación local.

## Evaluación de resultado por TC

| Resultado | Criterio | Acción |
|-----------|----------|--------|
| Pass      | Todas las assertions pasan | Publicar evidencia en ADO; cerrar TC (`Closed`) — PASO 6 |
| Fail      | Al menos una assertion falla | Activar `bug-reporter` asignado al dev de la HU; tag `QA_NOVEDAD` — PASO 6 |
| Flaky     | Pasó en un intento, falló en otro | Reportar como hallazgo al qa-agent |
| Skip      | No pudo ejecutarse por dependencia | Reportar razón al qa-agent; TC permanece `Active` |

## Restricciones

- **Nunca** cambiar `System.State` de la HU padre — solo tags, campos custom y comentarios
- **Nunca** ejecutar si la HU no está en `Resolved` — detener y escalar al qa-agent
- **Nunca** activar Tasks (TCs) antes de ejecutarlas — `Active` solo al iniciar la corrida del TC
- Nunca ejecutar contra Producción sin autorización explícita del Líder Técnico
- Nunca registrar credenciales en screenshots ni evidencia
- Nunca marcar TC como Pass sin evidencia que lo respalde
- Siempre limpiar archivos temporales `_qa_evidence_*` al terminar
- Si el proyecto no tiene Playwright instalado, no instalar silenciosamente — reportar al qa-agent
- Siempre generar specs con `QaCapture` — nunca specs sin `ev.step` / `ev.shot`
- Si el humano pide ver el navegador en local, ejecutar **PASO 3b** antes del PASO 4; no sustituir la corrida oficial por una ejecución headless silenciosa
- El config temporal de evidencia debe tener `headless: false` alineado con [`qa-evidence-templates.md`](./qa-evidence-templates.md) — no inventar flags `QA_HEADED` contradictorios
