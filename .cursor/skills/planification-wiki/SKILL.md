---
name: planification-wiki
description: Documenta la planificación de Features (HUs, diseño, frontend y backend) en la Wiki de Azure DevOps o en archivo local de respaldo. Usar después de feature-creator, tras aprobar un Feature, o cuando el tech-lead-agent solicite wiki de planificación. Triggers wiki, planificación, Azure DevOps wiki, planification-wiki, diseño UI, descomposición HU.
---

# Documentar planificación en Wiki

Registra o actualiza la planificación de un Feature en la Wiki de Azure DevOps con trazabilidad. Plantilla detallada en `references/plantilla-pagina-wiki.md`.

## Requisitos previos

1. Nombre del Feature e ID (si existe en Azure DevOps).
2. Contenido de planificación (salida de `feature-creator` u otros agentes).
3. Leer `.env.user-identity`: `USER_REAL_NAME`, `USER_REAL_EMAIL`, `AZURE_ORG_URL`, `AZURE_PROJECT_NAME`.
4. Opcional para notificaciones: `WIKI_NOTIFY_NAME`, `WIKI_NOTIFY_EMAIL` (si no existen, usar `USER_REAL_NAME` / `USER_REAL_EMAIL`).

Si no hay `.env` o falla la conexión: generar la wiki en un archivo `.md` local e informar explícitamente al usuario.

## Checklist

- [ ] Verificar si ya existe página `[Nombre del Feature] - [Fecha]`
- [ ] Completar plantilla sin placeholders vacíos
- [ ] Crear o actualizar en Azure Wiki (`az devops wiki`) o archivo local
- [ ] Comentar en el Feature con URL (solo si se publicó en la nube)
- [ ] Mencionar responsable de wiki vía HTML `mailto:`

## Paso 1 — Identificación

- **Nombre de página:** `[Nombre del Feature] - [Fecha actual ISO o legible]`
- Si existe página del mismo día para ese Feature → **actualizar**; si no → **crear**.

## Paso 2 — Contenido

Seguir `./plantilla-pagina-wiki.md`. Sustituir todos los placeholders con contenido real (HUs, diseños, planes FE/BE).

## Paso 3 — Publicación

**Azure (preferido):**

```bash
az devops wiki page create \
  --wiki <wiki-id> \
  --path "/Planificación/[Nombre-del-Feature]" \
  --file-content "$(cat planificacion.md)" \
  --project "${AZURE_PROJECT_NAME}"
```

**Local (fallback):** guardar `planificacion-[feature-slug].md` en el workspace y avisar que Azure no estuvo disponible.

## Paso 4 — Notificación en el Feature

Solo si se publicó en Azure:

```bash
az boards work-item update --id <FeatureID> --discussion "..."
```

Comentario HTML sugerido:

```html
<div>📋 Planificación registrada en Wiki: <a href="[URL_WIKI]">[título página]</a></div>
<div>Notificación: <a href="mailto:{WIKI_NOTIFY_EMAIL}">@{WIKI_NOTIFY_NAME}</a></div>
```

## Reglas

- Incluir usuario y fecha en el documento.
- No duplicar páginas del mismo día para el mismo Feature.
- Si es local, advertir claramente la indisponibilidad de Azure DevOps.
