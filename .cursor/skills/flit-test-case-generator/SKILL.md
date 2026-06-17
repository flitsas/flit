---
name: flit-test-case-generator
description: Genera Casos de Prueba en formato FLIT QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO} desde los Criterios de Aceptación Gherkin de una HU, con escenarios positivos, negativos y de borde. Usar cuando el qa-agent (modo A) necesite crear TCs antes de validar con tc-formatter. Triggers generar TC, test case, Gherkin, modo A, flit-test-case-generator.
---

Genera borradores de TCs; **no valida** formato final ni publica en ADO — eso es `tc-formatter`. Invocada por `qa-agent` en Modo A.

**Posición en el pipeline QA:**
```
flit-test-case-generator ──► tc-formatter ──► Tasks en ADO (Child de la HU) ──► playwright-runner
```

> Por restricción de plataforma FLIT, los TCs se registran como **Tasks** vinculadas a la HU (`Child`), no como Test Cases nativos de Azure. Ver `qa-agent` → «Restricción de plataforma — Azure DevOps».

## Pre-flight

1. `assets/test-case.template.md` — si existe en el repo
2. `az boards work-item show --id <ID> --output json` — leer AC Gherkin y Tasks hijo existentes
3. Diseño técnico en ADO Wiki (`@planification-wiki`) — contexto adicional

## Formato FLIT (estricto)

```
QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}
```

- `{##}`: dos dígitos (`01`–`99`)
- `{MODULO}` / `{ALCANCE}`: MAYÚSCULAS, sin espacios
- Separador obligatorio: ` - ` (espacio guión espacio)

## Checklist

- [ ] Extraer AC Gherkin de la HU
- [ ] Matriz positivo / negativo / borde por AC
- [ ] Redactar cuerpo de cada TC (precondiciones, pasos, datos, esperado, limpieza)
- [ ] Proponer consecutivos según Tasks existentes (validación final en `tc-formatter`)
- [ ] Entregar borrador al qa-agent → invocar **`tc-formatter`** (paso obligatorio siguiente)
- [ ] **No publicar en ADO** — la publicación como Task la ejecuta `tc-formatter` tras confirmación del QA humano

## Cobertura

- Cada AC positivo: al menos un TC
- Cada AC negativo: al menos un TC
- Bordes no triviales: al menos un TC por AC con límites
- Mínimo recomendado por HU: **5 TCs** (happy path + borde + error + adicionales)

## Cuerpo del TC

```markdown
# QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}

## Precondiciones
- ...

## Pasos
1. ...

## Datos de prueba
| Campo | Valor |
|-------|-------|

## Resultado esperado
...

## Postcondiciones
- ...
```

## Entrega al siguiente paso (`tc-formatter`)

Al terminar, reportar al qa-agent la lista de TCs propuestos (título + cuerpo + escenario Gherkin de origen + tipo: Happy Path / Borde / Error). **`tc-formatter`** validará formato, consecutivos, trazabilidad y cobertura mínima; solo entonces, con «sí» del QA humano, publicará cada TC como **Task** en ADO:

- `--type "Task"` (nunca `"Test Case"`)
- Vínculo **`Child`** de la HU (`System.LinkTypes.Hierarchy-Reverse`)
- Estado inicial: `New`, sin `AssignedTo` (asignación al QA en Modo B)

Ver comandos y reglas de publicación en `tc-formatter/SKILL.md`.

## Output esperado

```
flit-test-case-generator: [OK] N TCs propuestos para HU #[ID]

| TC   | Título propuesto | Gherkin origen | Tipo       |
|------|------------------|----------------|------------|
| TC01 | QA_TC01_...      | Scenario: ...  | Happy Path |

Siguiente paso: invocar tc-formatter para validar y publicar.
```

## Prohibido

- Alterar el formato FLIT
- TCs sin AC explícitos en la HU
- Mezclar positivo y negativo en un mismo TC
- Datos reales de producción
- TCs dependientes del orden de ejecución
- Más de 8–10 pasos por TC (partir en varios TCs)
- **Publicar work items en ADO** — exclusivo de `tc-formatter` tras confirmación humana
- Crear ítems tipo **Test Case** nativo — FLIT usa **Tasks** como contenedor de TC
