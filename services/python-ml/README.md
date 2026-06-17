# services/python-ml

Python 3.13 + FastAPI service (OCR / ML). MVP scaffold with health and OCR stub endpoints.

## Local dev

From repo root:

```bash
pnpm run install:python
pnpm run dev:python
```

Health (DEV): `http://localhost:4012/health`

## Tests

```bash
uv --directory services/python-ml sync --extra dev
uv --directory services/python-ml run pytest --cov=app
```
