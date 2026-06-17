---
name: flit-conventions-validator
description: Valida código, PRs, ramas, commits y work items contra convenciones FLIT en 7 dimensiones (título PR, rama, commits, rutas, campos ADO, denylist, estilo). Usar antes de abrir PR o en code-review-agent. Triggers convenciones FLIT, validar PR, branch agent/, pre-commit, flit-conventions-validator.
---

Solo lectura; no modifica código. Detalle de regex y ejemplos en `./dimensiones-convenciones-flit.md`. Salida en `./plantilla-salida-convenciones.md`.

## Pre-flight

2. `.cursor/rules/00-flit-conventions.mdc` y `./dimensiones-convenciones-flit.md`
3. `CLAUDE.md`, `AGENTS.md`
4. ADRs en el repositorio (`**/ADR-*.md`)

## Checklist (7 dimensiones)

- [ ] 1. Formato título PR
- [ ] 2. Nombre de rama
- [ ] 3. Mensajes de commit
- [ ] 4. Rutas de archivos
- [ ] 5. Campos personalizados ADO (si aplica)
- [ ] 6. Denylist de archivos prohibidos
- [ ] 7. Estilo (eslint frontend, dotnet format, ruff, tsc, console.log, TODOs)

## Comandos útiles

```bash
gh pr view <N> --json title,headRefName,commits
git log --oneline origin/develop..HEAD
git diff --name-only origin/develop...HEAD
```

## Veredicto

- **PASS** en todas las dimensiones aplicables
- **FAIL** con lista accionable por dimensión
- Dimension 5 = **NA** si no hay cambios en ADO

## Prohibido

- Modificar código
- Autofix violaciones de denylist (escalar)
- Omitir dimensiones por PR pequeña
