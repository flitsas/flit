---
name: flit-conflict-resolver
description: Propone resolución estructurada de conflictos de merge en PRs (ours/theirs/manual) con justificación por archivo y riesgo, sin ejecutar merge final sin confirmación humana. Usar cuando integration-agent encuentre conflictos en merge a develop. Triggers conflicto merge, conflict resolution, integration-agent, flit-conflict-resolver.
---

Invocada por **integration-agent**. No hace merge final sin confirmación.

## Checklist

- [ ] Listar archivos en conflicto (`git diff --name-only --diff-filter=U`)
- [ ] Clasificar por módulo y tipo (código, config, lockfile)
- [ ] Proponer estrategia por archivo con justificación
- [ ] Señalar riesgos (migraciones, contratos API, package-lock)
- [ ] Presentar plan al humano antes de `git checkout --ours/--theirs` o edición manual

## Plantilla de propuesta

```markdown
# Resolución de conflictos — PR !<N>

| Archivo | Estrategia | Justificación | Riesgo |
|---------|------------|---------------|--------|
| ... | manual / ours / theirs | ... | alto/medio/bajo |

## Pasos sugeridos
1. ...
2. Re-ejecutar tests: `npm run build` / CI
```

## Reglas

- **Lockfiles** (`package-lock.json`, `pnpm-lock.yaml`): preferir regenerar tras resolver package.json, no mezclar a ciegas
- **Migraciones**: nunca elegir versión que modifique migración ya aplicada
- **Código de negocio**: preferir merge manual preservando AC de la US del PR

## Prohibido

- Merge automático sin revisión en archivos de seguridad o auth
- Resolver conflictos fuera del alcance del PR sin avisar
