# AGENTS.override.md — services/python-ml

> Override por servicio para agentes de código. Editar directamente en este archivo.
> Cursor y otros agentes leen este archivo cuando se trabaja bajo `services/python-ml/`.

## Agente principal

[`.cursor/agents/backend-agent.md`](../../.cursor/agents/backend-agent.md) no aplica a Python — usar convenciones de este servicio + skills de calidad del monorepo.

Para cambios en este servicio: implementar en `app/`, tests en `tests/`, seguir `pyproject.toml` (Ruff, pytest).

## Scope

`services/python-ml/**`

## Lectura obligatoria al trabajar en este servicio

- [`.cursor/rules/00-flit-conventions.mdc`](../../.cursor/rules/00-flit-conventions.mdc) — 18 reglas innegociables FLIT
- [`.cursor/skills/flit-conventions-validator/dimensiones-convenciones-flit.md`](../../.cursor/skills/flit-conventions-validator/dimensiones-convenciones-flit.md) — rutas, PRs, commits
- [`README.md`](./README.md) — dev, tests y puertos
- [`pyproject.toml`](./pyproject.toml) — Ruff, pytest, dependencias
- Contrato OpenAPI (cuando exista): `contracts/openapi/python-ml.v1.yaml`

## Quality gates de este servicio

Ejecutar antes de cada commit (desde la raíz del monorepo):

```bash
pnpm run lint:python
pnpm run test:python
```

Equivalente local:

```bash
uv --directory services/python-ml run ruff check app tests
uv --directory services/python-ml run ruff format app tests --check
uv --directory services/python-ml run pytest --cov=app
```

## Mantenimiento

Actualizar este archivo cuando cambien quality gates o el stack del servicio.
