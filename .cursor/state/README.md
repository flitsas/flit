# .cursor/state/ — Fallback de trazabilidad

Esta carpeta es el **plan B** del orquestador para cuando ADO no está disponible.

## Uso normal

El orquestador escribe el progreso del flujo como **comentarios en Azure DevOps** (Discussion del Feature o HU). Esa es la fuente de verdad del estado.

## Cuándo se usa esta carpeta

Solo cuando ADO no está disponible (MCP caído, sin PAT, sin conexión):

- El orquestador guarda los comentarios pendientes en `pending-ado-comments.md`
- Al recuperar la conexión, publica esos comentarios en ADO y borra el archivo local

## Archivos esperados

```
pending-ado-comments.md   — comentarios a publicar en ADO cuando vuelva la conexión
```

## Importante

- Esta carpeta está **gitignored** — su contenido nunca se versiona
- Si encuentras archivos aquí, significa que hubo un flujo interrumpido con ADO caído
- Publicar los comentarios manualmente en ADO y borrar el archivo es el procedimiento correcto
