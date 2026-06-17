# Workflows — Orquestador FLIT

Este directorio contiene los flujos que ejecuta el `orchestrator-agent`.
El orquestador **siempre lee el workflow antes de actuar** — el flujo vive aquí, no dentro del agente.

---

## Flujos disponibles

| Archivo | Descripción | Cuándo usarlo |
|---------|-------------|---------------|
| `requirement-to-delivery.md` | Ciclo completo: requerimiento → Feature → diseño → HUs → implementación → deploy DEV | Requerimiento nuevo que aún no existe en ADO |
| `implement-story.md` | Una Historia de Usuario: implementación → review → integración | HU ya creada en ADO, lista para desarrollar |
| `review-pr.md` | Pipeline de revisión: code review + security + comentarios consolidados | PR abierto y listo para revisión |
| `decompose-feature.md` | Descomponer un Feature en Historias de Usuario con validación DoR | Feature aprobado en ADO, sin HUs hijas |
| `deploy-env.md` | Desplegar a DEV, QA o PDN con verificación de precondiciones | Post-merge o solicitud de deploy manual |

---

## Estructura de cada workflow

Todos los workflows siguen el mismo formato:

1. **Objetivo** — qué produce este flujo al terminar
2. **Precondiciones** — qué debe existir antes de empezar
3. **Fases** — tabla con: fase, agente responsable, inputs, outputs esperados, gate si aplica
4. **Detalle de cada fase** — instrucción exacta para invocar el agente + qué verificar
5. **Si falla** — qué hacer cuando una fase no completa
6. **Trazabilidad** — comentario a publicar en ADO al finalizar

---

## Cómo agregar un workflow nuevo

1. Crea un archivo `.md` en esta carpeta siguiendo la estructura de arriba.
2. Agrega una fila en la tabla del README.
3. Agrega la intención y el nombre del archivo en la tabla de routing del `orchestrator-agent.md`.
4. Haz PR hacia `develop` con el nuevo workflow para revisión del Líder Técnico.
