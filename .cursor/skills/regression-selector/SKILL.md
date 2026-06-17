---
name: regression-selector
description: Identifica y prioriza qué TCs ejecutar en una ronda de regresión dado un módulo afectado o un evento de deploy, y entrega la selección aprobada a playwright-runner para su ejecución. Usar cuando el qa-agent (Modo D) necesite determinar el alcance de regresión antes de correr pruebas, se produzca un deploy a QA o producción, se resuelva un bug productivo, o el Líder Técnico solicite regresión de un módulo. Triggers regression-selector, regresión, alcance regresión, deploy QA, deploy producción, smoke test, módulo afectado, Modo D, qa-agent regresión, selección TCs.
---

# regression-selector

Identifica qué TCs ejecutar en una ronda de regresión dado un módulo afectado o un evento de deploy. Invocada por el `qa-agent` en **Modo D**.

**Posición en el pipeline QA:**
```
[Evento: deploy / bug resuelto / solicitud LT]
        ↓
regression-selector  ──► (selección aprobada) ──► playwright-runner
```

## Inputs requeridos

| Campo       | Requerido   | Descripción |
| ----------- | ----------- | ----------- |
| `trigger`   | Siempre     | `bug_productivo` / `deploy_qa` / `deploy_produccion` / `solicitud_lt` |
| `modulo`    | Condicional | Requerido si `trigger` es `bug_productivo` o `solicitud_lt` |
| `ambiente`  | Siempre     | `QA` / `Producción` |
| `hu_origen` | Condicional | ID de la HU del bug resuelto — requerido si `trigger` es `bug_productivo` |

Si algún input falta, reportarlo al qa-agent y esperar antes de continuar.

> **Gate QA:** Los TCs seleccionados deben pertenecer a HUs en estado **`Resolved`**. Si una HU padre no está en `Resolved`, excluir sus TCs y reportar al qa-agent — el QA no transiciona estados de HU.

## Credenciales ADO

Leer de `.env.user-identity`: `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`, `AZURE_PAT`.
Si el archivo no existe o falta alguna variable, reportar al qa-agent y detenerse.

> ⚠️ **Restricción ADO**: Nunca hardcodear credenciales. Siempre leer de `.env.user-identity`.
> Si el az CLI no está autenticado o el PAT expiró, reportar al qa-agent y no continuar.

## Proceso

### 1. Determinar alcance según trigger

| Trigger             | Alcance                                                                          |
| ------------------- | -------------------------------------------------------------------------------- |
| `bug_productivo`    | TCs críticos del módulo afectado + TCs de módulos con dependencia directa        |
| `deploy_qa`         | TCs críticos de todos los módulos incluidos en el deploy                         |
| `deploy_produccion` | TCs críticos de todos los módulos del deploy + smoke tests de flujos principales |
| `solicitud_lt`      | Alcance definido por el Líder Técnico en la solicitud                            |

Para `solicitud_lt` sin alcance explícito: preguntar al qa-agent qué módulos o TCs específicos indicó el LT antes de continuar. No asumir alcance.

### 2. Clasificar TCs por criticidad

Consultar las Tasks en Azure DevOps que correspondan al módulo afectado:

```bash
az boards query \
  --wiql "SELECT [System.Id], [System.Title], [System.Tags], [System.State]
          FROM WorkItems
          WHERE [System.WorkItemType] = 'Task'
          AND [System.Title] CONTAINS 'QA_TC'
          AND [System.Title] CONTAINS '{MODULO}'
          AND [System.TeamProject] = '$AZURE_PROJECT_NAME'" \
  --org $AZURE_ORG_URL \
  --output json
```

Para `deploy_qa` o `deploy_produccion` sin módulo específico, omitir el filtro `CONTAINS '{MODULO}'` para obtener todos los TCs del proyecto.

| Criticidad | Criterio                                                                     |
| ---------- | ---------------------------------------------------------------------------- |
| Crítico    | Cubre el happy path principal del módulo o un flujo que afecta otros módulos |
| Alto       | Cubre casos de error con impacto en datos o integridad del sistema           |
| Medio      | Cubre casos de borde sin impacto en otros módulos                            |
| Bajo       | Cubre casos cosméticos o de UX                                               |

Por defecto se seleccionan **Crítico y Alto**. Incluir Medio y Bajo solo si el Líder Técnico lo solicita explícitamente.

### 3. Identificar dependencias entre módulos

Si el bug o deploy afecta un módulo con dependencias conocidas, expandir la selección:

```
Ejemplo:
Módulo afectado: TRASPASOS
Dependencias conocidas: PERSONAS (consulta de titular), VALIDACION_IDENTIDAD
Resultado: TCs críticos de TRASPASOS + PERSONAS + VALIDACION_IDENTIDAD
```

Las dependencias se consultan en este orden de prioridad:
1. `.cursor/skills/flit-dor-dod-validator/SKILL.md` (criterios DoD-US).
2. Si el archivo no existe o no documenta las dependencias del módulo: preguntar al QA humano antes de continuar — no asumir dependencias.

### 4. Presentar selección al qa-agent

Presentar la lista de TCs con justificación por cada uno antes de pasar al `playwright-runner`. El qa-agent puede agregar, quitar o aprobar la selección.

## Output

### Selección lista para ejecutar

```
regression-selector: Suite de regresión identificada

Trigger: [tipo] | Módulo principal: [MODULO] | Ambiente: [QA/Producción]

TCs seleccionados (N):

| TC   | Módulo | Título | Criticidad | Razón de inclusión |
|------|--------|--------|------------|-------------------|
| TC01 | TRASPASOS | QA_TC01_TRASPASOS_Consulta - Happy Path | Crítico | Flujo principal del módulo afectado |
| TC03 | PERSONAS | QA_TC03_PERSONAS_Consulta - Titular activo | Crítico | Dependencia directa de TRASPASOS |
| TC02 | VALIDACION_IDENTIDAD | QA_TC02_VALIDACION_Documento - Verificación exitosa | Alto | Dependencia secundaria |

Cobertura: 3 TCs críticos, 1 alto | Módulos: TRASPASOS, PERSONAS, VALIDACION_IDENTIDAD

¿Apruebas esta selección para ejecutar con playwright-runner?
```

### Sin TCs críticos documentados para el módulo

```
regression-selector: [ADVERTENCIA] No se encontraron TCs críticos para el módulo [MODULO]

Opciones:
1. Ejecutar todos los TCs disponibles del módulo (N TCs)
2. Definir manualmente qué TCs incluir

¿Cómo prefieres proceder?
```

## Restricciones

- Nunca ejecutar la regresión directamente — entregar la selección aprobada al `playwright-runner`.
- Nunca omitir módulos con dependencia directa del módulo afectado.
- Nunca aprobar la selección por sí misma — siempre esperar confirmación del qa-agent.
- Nunca ejecutar regresión en Producción sin autorización explícita del Líder Técnico.
- Nunca asumir dependencias entre módulos si no están documentadas — preguntar al QA humano.
- Nunca asumir el alcance de `solicitud_lt` si el LT no lo especificó — preguntar antes de continuar.
