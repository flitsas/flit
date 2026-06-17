---
name: tc-formatter
description: Valida y estructura Casos de Prueba en formato FLIT estricto (QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}), garantizando consecutivos correctos, trazabilidad con el escenario Gherkin de origen y cobertura mínima, antes de publicarlos como Tasks en Azure DevOps. Usar cuando el qa-agent (Modo A) haya generado TCs con flit-test-case-generator y necesite validar formato, consecutivos y trazabilidad Gherkin antes de publicar. Es el paso 2 obligatorio del flujo QA: flit-test-case-generator → tc-formatter → Tasks en ADO. Triggers tc-formatter, validar TCs, formato TC, QA_TC, consecutivos, publicar Tasks ADO, Modo A, trazabilidad Gherkin, cobertura mínima.
---

# tc-formatter

Valida y formatea Casos de Prueba en formato FLIT antes de publicarlos como Tasks en Azure DevOps. Invocada por el `qa-agent` en **Modo A**.

**Posición en el pipeline QA:**
```
flit-test-case-generator ──► tc-formatter ──► (Tasks publicadas en ADO) ──► playwright-runner
```

> Esta skill es el paso 2 del flujo. Invocarla antes implica que `flit-test-case-generator` ya produjo los TCs propuestos.

## Inputs requeridos

Antes de ejecutar, verificar que todos estén presentes. Si alguno falta, reportarlo y detenerse — no producir output hasta recibirlos todos.

- `hu_id` — ID de la HU en Azure DevOps
- `modulo` — nombre del módulo tal como aparece en el work item
- `escenarios_gherkin` — lista de escenarios Gherkin de los AC de la HU
- `tcs_propuestos` — lista de TCs generados por el qa-agent para validar
- `tasks_existentes` — Tasks actuales vinculadas a la HU (para determinar el siguiente consecutivo)

## Credenciales ADO

Leer de `.env.user-identity`: `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`, `AZURE_PAT`, `USER_REAL_NAME`, `USER_REAL_EMAIL`.
Si el archivo no existe o falta alguna variable, reportar al qa-agent y detenerse.

> ⚠️ **Restricción ADO**: Nunca hardcodear credenciales. Siempre leer de `.env.user-identity`.
> Si el az CLI no está autenticado o el PAT expiró, reportar al qa-agent y no continuar.

## Consulta de Tasks existentes en ADO

Antes de asignar consecutivos, consultar las Tasks hijo de la HU para determinar el último `QA_TC##` usado:

```bash
az boards work-item show \
  --id {hu_id} \
  --expand relations \
  --org $AZURE_ORG_URL \
  --project $AZURE_PROJECT_NAME \
  --output json
```

Filtrar relaciones de tipo `System.LinkTypes.Hierarchy-Forward` con `System.WorkItemType = Task`. Extraer el número `##` del patrón `QA_TC##_` para determinar el último consecutivo. Si no hay Tasks previas, empezar en `TC01`.

## Pasos de validación

### 1. Formato del título

Cada TC debe cumplir estrictamente:

```
QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}
```

| Componente    | Regla |
| ------------- | ----- |
| `QA_TC`       | Prefijo fijo — siempre en mayúsculas |
| `{##}`        | Consecutivo de 2 dígitos con cero a la izquierda: `01`, `02` ... `99` |
| `{MODULO}`    | Nombre exacto del módulo del work item — mayúsculas, sin espacios (guión bajo si aplica) |
| `{ALCANCE}`   | Funcionalidad específica dentro del módulo — en mayúsculas |
| `{ESCENARIO}` | Descripción corta del caso — en español, capitalizado, sin abreviaciones |

Ejemplos válidos:
```
QA_TC01_PERSONAS_Registro - Happy Path con todos los archivos validos
QA_TC02_PERSONAS_Registro - Falta campo obligatorio numero_identificacion
QA_TC01_TRASPASOS_Consulta - Traspaso exitoso entre cuentas del mismo titular
```

Ejemplos inválidos:
```
QA_TC1_PERSONAS_Registro - Happy Path       → consecutivo sin cero: debe ser TC01
qa_tc01_personas_registro - happy path      → debe ir en mayúsculas
QA_TC01_PERSONAS - Happy Path               → falta el ALCANCE
QA_TC01_PERSONAS_Registro Happy Path        → falta el separador " - "
```

### 2. Consecutivos

- Consultar Tasks existentes en la HU vía ADO para determinar el último consecutivo usado.
- El siguiente TC debe ser `último + 1`.
- Si no hay Tasks previas, empezar en `TC01`.
- Si se detecta un salto o duplicado, reportarlo al qa-agent antes de continuar.

### 3. Trazabilidad Gherkin

Cada TC propuesto debe estar trazado a un escenario Gherkin específico de los AC. Verificar:

- Que exista un escenario Gherkin de origen para cada TC.
- Que el `{ALCANCE}` y `{ESCENARIO}` del título reflejen el escenario Gherkin de origen.
- Que no haya TCs sin escenario Gherkin asociado.

Si un TC no tiene trazabilidad clara, marcarlo como `[ADVERTENCIA: sin trazabilidad]` y reportarlo al qa-agent.

### 4. Cobertura mínima

| Tipo       | Mínimo            |
| ---------- | ----------------- |
| Happy Path | 1                 |
| Borde      | 1                 |
| Error      | 1                 |
| Total      | 3 (recomendado 5) |

Si la cobertura mínima no se cumple, advertir al qa-agent indicando los tipos faltantes.

## Output

### Todos los TCs son válidos

```
tc-formatter: [OK] N TCs validados

| TC   | Título                                      | Gherkin origen     | Tipo       |
|------|---------------------------------------------|--------------------|------------|
| TC01 | QA_TC01_{MODULO}_{ALCANCE} - {ESCENARIO}   | Scenario: [nombre] | Happy Path |
| TC02 | QA_TC02_{MODULO}_{ALCANCE} - {ESCENARIO}   | Scenario: [nombre] | Borde      |

Cobertura: [OK] Happy Path (N) | [OK] Borde (N) | [OK] Error (N)
Consecutivos: [OK] TC01 → TCN sin saltos ni duplicados
Listos para publicar como Tasks en HU #[ID]

¿Confirmas la publicación en Azure DevOps?
```

Tras confirmación explícita del qa-agent, publicar cada TC como Task en ADO:

```bash
az boards work-item create \
  --type "Task" \
  --title "QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}" \
  --org $AZURE_ORG_URL \
  --project $AZURE_PROJECT_NAME \
  --output json

az boards work-item relation add \
  --id {task_id_recien_creado} \
  --relation-type "System.LinkTypes.Hierarchy-Reverse" \
  --target-id {hu_id} \
  --org $AZURE_ORG_URL \
  --project $AZURE_PROJECT_NAME
```

Repetir por cada TC. Al finalizar, reportar los IDs de Tasks creadas al qa-agent.

**Estado inicial de Tasks publicadas (Modo A):**
- `System.State` = **`New`**
- `System.AssignedTo` = **vacío** (se asigna al QA en Modo B al ejecutar)
- No activar (`Active`) ni cerrar (`Closed`) en Modo A

**Si el qa-agent rechaza la confirmación:** solicitar los cambios específicos, corregir los TCs afectados, revalidar desde el paso 1 y volver a presentar el output para una nueva confirmación. No publicar hasta recibir aprobación explícita.

### Se encontraron errores

```
tc-formatter: [ADVERTENCIA] N problemas encontrados

TC02: [ERROR] Consecutivo incorrecto — debe ser TC02, se recibió TC2
TC04: [ERROR] Falta ALCANCE en el título
TC06: [ADVERTENCIA] Sin trazabilidad Gherkin — no se encontró escenario de origen

Cobertura: [OK] Happy Path | [ERROR] Faltan casos de Error

Corrige los problemas antes de publicar.
```

## Restricciones

- Nunca publicar un TC con formato inválido — devolver error al qa-agent.
- Nunca asumir el módulo si no está explícito en el input.
- Nunca omitir la validación de trazabilidad Gherkin aunque el título sea formalmente correcto.
- Nunca asignar consecutivos sin consultar primero las Tasks existentes en la HU vía ADO.
- Nunca publicar Tasks en ADO sin confirmación explícita del qa-agent.
- Nunca activar ni cerrar Tasks al publicarlas — eso ocurre en Modo B (`playwright-runner`)
- Nunca asignar Tasks al QA en Modo A — la asignación es al iniciar ejecución
