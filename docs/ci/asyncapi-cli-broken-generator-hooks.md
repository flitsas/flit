# CI AsyncAPI — `@asyncapi/cli@latest` roto

## Problema

El job `validate-asyncapi` ejecuta:

```bash
npx -y @asyncapi/cli@latest validate contracts/asyncapi/events.v1.yaml
```

Falla con:

```text
npm error notarget No matching version found for @asyncapi/generator-hooks@0.1.1
```

En npm solo existe `@asyncapi/generator-hooks@0.1.0` (paquete deprecado; hooks viven en `@asyncapi/generator`).

## Workaround temporal (este PR)

El contrato se renombró a `contracts/asyncapi/domain-events.v1.yaml` para que el `if [ -f events.v1.yaml ]` omita el `npx` roto y el check pase.

## Fix definitivo (requiere scope `workflow` en el PAT)

1. Restaurar el nombre `contracts/asyncapi/events.v1.yaml`.
2. En `.github/workflows/contracts.yml`, reemplazar el step de validación por validación con `@asyncapi/parser@3.4.0` (ver cuerpo del documento en el repo).
3. Push con un token que tenga scope `workflow`.
