---
name: bug-reporter
description: Redacta bugs estructurados con Repro Steps, severidad y evidencia, y los radica en Azure DevOps con trazabilidad hacia la HU o el TC de origen. Usar cuando el qa-agent (Modo C) detecte un fallo durante la ejecución de TCs, cuando playwright-runner reporte un TC en estado Fail, o cuando el usuario detecte manualmente un defecto en DEV, QA o Producción. Es el paso final del ciclo QA cuando hay un resultado negativo. Triggers bug-reporter, radicar bug, TC fallido, fallo detectado, defecto, error en ambiente, Modo C, resultado Fail, novedad QA, bug ADO.
---

# bug-reporter

Redacta bugs estructurados con Repro Steps, evidencia y severidad, y los radica en Azure DevOps. Invocada por el `qa-agent` en **Modo C** o directamente por el usuario ante un defecto detectado manualmente.

**Posición en el pipeline QA:**
```
playwright-runner (TC Fail) ──► bug-reporter ──► Bug radicado en ADO
usuario (fallo manual)      ──► bug-reporter ──► Bug radicado en ADO
```

## Inputs requeridos

| Campo                 | Requerido   | Descripción |
| --------------------- | ----------- | ----------- |
| `origen`              | Siempre     | `hu` / `ambiente` / `productivo` — determina el flujo de asignación |
| `hu_id`               | Condicional | Requerido si `origen` es `hu` o `ambiente` con HU asociada |
| `modulo`              | Siempre     | Módulo afectado tal como aparece en el work item |
| `descripcion`         | Siempre     | Descripción corta del fallo observado |
| `repro_steps`         | Siempre     | Pasos para reproducir el bug — puede venir del TC fallido |
| `resultado_esperado`  | Siempre     | Qué debería ocurrir según los AC |
| `resultado_observado` | Siempre     | Qué ocurrió realmente |
| `evidencia`           | Siempre     | Screenshots, logs, responses o videos del fallo |
| `ambiente`            | Siempre     | `DEV` / `QA` / `Producción` |
| `asignado_a`          | Condicional | Email del responsable — para novedades en HU: **`System.AssignedTo` de la HU padre** (obligatorio leer de ADO) |
| `tc_id`               | Condicional | ID del Task/TC fallido — incluir en Repro Steps y link en el Bug |
| `assertion_fallida`   | Condicional | Mensaje/stack de la assertion o error Playwright/Jest/Vitest |

## Credenciales ADO

Leer de `.env.user-identity`: `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`, `AZURE_PAT`, `USER_REAL_NAME`, `USER_REAL_EMAIL`.
Si el archivo no existe o falta alguna variable, reportar al qa-agent y detenerse.

> ⚠️ **Restricción ADO**: Nunca hardcodear credenciales. Siempre leer de `.env.user-identity`.
> Si el az CLI no está autenticado o el PAT expiró, reportar al qa-agent y no continuar.

## Proceso

### 1. Determinar severidad

| Severidad | Criterio |
| --------- | -------- |
| Crítico   | Bloquea un flujo completo en producción, sin workaround posible |
| Alto      | Afecta funcionalidad principal, existe workaround pero es difícil o costoso |
| Medio     | Afecta funcionalidad secundaria o existe workaround fácil |
| Bajo      | Error cosmético o de UX, no afecta funcionalidad |

Cuando hay duda entre dos niveles, asignar el más alto e indicar al qa-agent que lo revise.

### 2. Determinar asignación y vínculo

| Origen | Asignación (`System.AssignedTo`) | Vínculo en ADO |
| ------ | -------------------------------- | -------------- |
| Novedad en HU (DEV o QA) | **`System.AssignedTo` de la HU padre** — leer con `wit_get_work_item(hu_id)` | Bug como **hijo** de la HU (`wit_add_child_work_items`) |
| DEV/QA sin HU asociada | Dev responsable del módulo o Líder Técnico | Bug independiente |
| Bug productivo (soporte, operaciones, cliente) | Siempre Líder Técnico — nunca directo al dev | Según origen |

> Para novedades QA: el Bug **no** se asigna al QA — va al desarrollador que tiene la HU padre.

Si `System.AssignedTo` de la HU está vacío, detener y escalar al qa-agent (violación regla FLIT #4).

### 3. Redactar el bug (Repro Steps completos — obligatorio)

El bug debe permitir **replicar y corregir** el escenario sin contexto adicional. Checklist mínimo antes de radicar:

- [ ] **Precondiciones** explícitas (usuario, rol, datos en BD/localStorage, feature flags)
- [ ] **URL o endpoint** exacto (`/traspasos`, `POST /api/...`)
- [ ] **Datos de prueba** en tabla (sin PII real — anonimizar)
- [ ] **Pasos numerados** 1…N, uno por acción observable
- [ ] **Resultado esperado** alineado al AC Gherkin / TC
- [ ] **Resultado observado** con mensaje de error, HTTP status, selector DOM o stack trace
- [ ] **TC origen** — link al Task `#9242` y título `QA_TC01_...`
- [ ] **Assertion fallida** — texto literal del test (`expect(...).toBeVisible()`)
- [ ] **Evidencia** — screenshots del paso fallido, video WebM, log de consola (sin secretos)
- [ ] **Ambiente y build** — DEV/QA, commit o build desplegado

```
Título: [MODULO] [descripción corta del fallo] — [ambiente]

Severidad: Crítico / Alto / Medio / Bajo
Ambiente: DEV / QA / Producción
HU padre: #[ID] (Child link)
TC origen: #[task_id] — QA_TC0N_MODULO
Módulo: [MODULO]
Asignado a: [email del System.AssignedTo de la HU padre]

--- PRECONDICIONES ---
- [Estado del sistema antes de reproducir]

--- PASOS PARA REPRODUCIR ---
1. [Paso 1 — acción concreta]
2. [Paso 2]
3. [Paso N — punto exacto del fallo]

--- RESULTADO ESPERADO ---
[Qué debería ocurrir según AC/TC]

--- RESULTADO OBSERVADO ---
[Qué ocurrió — error, status HTTP, mensaje UI, stack trace]

--- ASSERTION / ERROR DEL TEST ---
[Texto literal de la assertion fallida o error Playwright]

--- DATOS DE PRUEBA ---
| Campo | Valor |
|-------|-------|
| ... | ... |

--- EVIDENCIA ---
[Screenshots embebidos / links a adjuntos ADO / video]

--- INFORMACIÓN DEL SISTEMA ---
Ambiente: [DEV / QA / Producción]
Build / versión: [commit o build]
Navegador: [chromium / firefox si aplica]
Fecha y hora: [timestamp ISO]
```

### 4. Validar antes de radicar

Presentar el bug redactado al qa-agent o al usuario. **Rechazar radicación** si falta cualquier ítem del checklist de la sección 3. No radicar sin aprobación explícita.

### 5. Radicar en ADO (solo tras confirmación)

**5a. Novedad en HU — Bug como hijo (canal preferido MCP):**

```text
1. wit_get_work_item(hu_id) → leer System.AssignedTo (email del dev)
2. wit_add_child_work_items:
     parentId: {hu_id}
     workItemType: Bug
     items: [{ title, description: HTML completo con Repro Steps }]
3. wit_update_work_item(bug_id):
     System.AssignedTo = {email_dev_hu_padre}
     Microsoft.VSTS.Common.Severity = {severidad}
     Microsoft.VSTS.TCM.ReproSteps = {repro_steps_html}
     System.Tags = QA_NOVEDAD; {modulo}
4. wit_add_work_item_comment(hu_id): link al Bug #{bug_id}
5. wit_update_work_item(hu_id): System.State = Active  (si estaba Resolved — re-entrega dev)
6. wit_add_work_item_comment(hu_id): HU reactivada por novedad QA + link Bug
```

> **Reactivación HU (obligatoria):** Tras radicar un Bug hijo de una HU con novedad (`QA_NOVEDAD`), la HU padre debe pasar a **`Active`** para que el desarrollador corrija y re-entregue. No pedir confirmación adicional si el supervisor ya estableció esta política.

**5b. Bug sin HU padre — REST/az CLI (fallback):**

```bash
DESCRIPCION_HTML="<div>... Repro Steps completos ...</div>"

az boards work-item create \
  --type "Bug" \
  --title "[{MODULO}] {descripcion_corta} — {AMBIENTE}" \
  --org $AZURE_ORG_URL \
  --project $AZURE_PROJECT_NAME \
  --fields \
    "System.Description=${DESCRIPCION_HTML}" \
    "Microsoft.VSTS.Common.Severity={severidad}" \
    "Microsoft.VSTS.TCM.ReproSteps=${REPRO_HTML}" \
    "System.AssignedTo={asignado_a}" \
  --output json
```

> **Prohibido** usar solo `Affects-Forward` cuando el origen es novedad en HU — el Bug debe ser **hijo** (`Child`) de la User Story.

Reportar el ID del Bug creado al qa-agent para trazabilidad.

## Output

### Bug listo para radicar

```
bug-reporter: [OK] Bug redactado

Título: [MODULO] [descripción] — [ambiente]
Severidad: [nivel]
Asignado a: [dev / Líder Técnico]
HU relacionada: #[ID]

[Cuerpo completo del bug]

¿Confirmas la radicación en Azure DevOps?
```

Tras confirmación, ejecutar el Paso 5 y reportar:

```
bug-reporter: [OK] Bug radicado en ADO

Bug ID: #{bug_id}
Título: [MODULO] [descripción] — [ambiente]
Severidad: [nivel]
Vinculado a HU: #[hu_id] (si aplica)
```

### Inputs insuficientes

```
bug-reporter: [ADVERTENCIA] Inputs insuficientes para radicar el bug

Falta:
- resultado_esperado: ¿qué debería haber ocurrido según los AC?
- evidencia: adjunta screenshot o log del fallo

Proporciona los datos faltantes para continuar.
```

## Restricciones

- Nunca radicar un bug productivo asignado directamente al desarrollador.
- Nunca publicar un bug sin evidencia adjunta.
- Nunca radicar sin confirmación explícita del qa-agent o del usuario.
- Nunca omitir precondiciones, pasos numerados, esperado vs observado ni assertion fallida.
- Nunca radicar novedad de HU sin Repro Steps **completos** — el dev debe poder replicar solo con el Bug.
- Nunca asignar el Bug al QA — en novedades de HU, asignar siempre al **`System.AssignedTo` de la HU padre**.
- Nunca vincular novedad de HU solo con `Affects` — usar **Child** de la User Story.
- Nunca asumir la asignación si el origen no está claro — preguntar antes de continuar.
