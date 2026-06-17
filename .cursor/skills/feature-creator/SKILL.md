---
name: feature-creator
description: Crea Features en Azure DevOps con objetivo, descripción y criterios funcionales estandarizados, trazabilidad HTML y fallback local. Usar cuando el tech-lead-agent (modo A), el usuario o un agente soliciten crear un Feature nuevo, redactar borrador DOR o registrar work item tipo Feature. No usar para Historias de Usuario, Tasks ni Bugs aislados. Triggers Feature, Azure DevOps, OBJETIVO, CRITERIOS FUNCIONALES, módulo FLIT, feature-creator.
---

# Crear Feature en Azure DevOps

Estandariza la creación de Features. Lee `.env.user-identity` para personalización y menciones en Azure DevOps.

## Requisitos previos

1. **Solo Features:** Si la solicitud es User Story, Task o Bug sin Feature padre, detener y pedir crear el Feature primero con esta skill.
2. Leer `.env.user-identity`: `USER_REAL_NAME`, `USER_REAL_EMAIL`, `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`.
3. Si falla la lectura: informar al usuario, no crear `.env.user-identity`, entregar el borrador en un archivo `.md` local.

## Checklist de ejecución

- [ ] Validar que la solicitud es un Feature
- [ ] Cargar identidad desde `.env.user-identity`
- [ ] Generar borrador con el formato obligatorio
- [ ] Esperar aprobación explícita de `USER_REAL_NAME`
- [ ] Registrar en Azure DevOps (o entregar `.md` si no hay conexión)
- [ ] Comentario HTML de trazabilidad en el Feature
- [ ] Invocar `planification-wiki` si el usuario o el tech-lead lo requieren

## Paso 1 — Borrador (sin registrar)

Generar el borrador con este formato exacto:

```markdown
Título: [MÓDULO] - Descripción de la necesidad

# OBJETIVO
[Qué se quiere lograr — párrafo claro y completo.]

# DESCRIPTION
[Detalle funcional: campos, estados, validaciones, reglas de negocio.]

# CRITERIOS FUNCIONALES
- [ ] Criterio 1
- [ ] Criterio N
```

**No registrar** hasta aprobación explícita del supervisor (`USER_REAL_NAME`).

## Paso 2 — Registro en Azure DevOps

```bash
az boards work-item create \
  --type "Feature" \
  --title "[MÓDULO] - Descripción" \
  --description "$(cat borrador-feature.md)" \
  --assigned-to "${USER_REAL_EMAIL}"
```

- Organización y proyecto desde `.env.user-identity`.
- Título con prefijo `[MÓDULO] - `.
- Si no hay acceso a Azure: solicitar datos al usuario o guardar solo el `.md` local.

## Paso 3 — Trazabilidad

Comentario obligatorio en el Feature:

```html
<div>🤖 Acción registrada por @{Nombre-del-Agente} usando el skill <b>@feature-creator</b> bajo la supervisión de <a href="mailto:{USER_REAL_EMAIL}">@{USER_REAL_NAME}</a></div>
```

## Paso 4 — Wiki (opcional)

Tras crear el Feature en Azure DevOps, invocar la skill `planification-wiki` con el ID del Feature y el borrador de planificación.

## Reglas

- Exclusivo para **Features**; nunca crear HUs con esta skill.
- Comentario HTML obligatorio para tagueo.
- No modificar el formato OBJETIVO / DESCRIPTION / CRITERIOS FUNCIONALES.
