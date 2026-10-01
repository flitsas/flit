# ADR-0068: Paginación del feed de sincronización por cursor keyset opaco

**Fecha**: 2026-09-30
**Status**: Propuesto
**Deciders**: David Alejandro Chica Hernandez (PO), Claude Code (agente de implementación). Aceptación: Líder Técnico.
**Tags**: arquitectura, backend, api, integraciones, rendimiento
**Épica / Feature**: #12737 / #13066 (HU #13076, #13081, #13080) · **Contrato**: `docs/integraciones/external-api-tramites-sync.md` (v3.1, §3)

## Contexto

`GET /api/v1/external/tramites/sync` entrega a un sistema externo (Flito) los cambios de trámites de
todas las compañías. El consumidor recorre el feed cada 5 minutos y a demanda, persiste dónde quedó y
sigue desde ahí. Restricciones:

1. **No perder cambios**: una transacción larga puede confirmar después que otras con versiones mayores
   (ADR-0066, punto 7). Un cursor que solo mire la versión se la saltaría.
2. **Estable bajo escritura concurrente**: el feed se lee mientras la plataforma escribe.
3. **Coste constante por página** con cientos de miles de trámites (p95 < 1,5 s con páginas de 1000, HU #13083).
4. **Reanudable**: el consumidor guarda un valor y continúa en otra corrida, días después.
5. **Evolucionable** sin romper al consumidor si cambia la clave de orden (ya cambió una vez: de
   `syncVersion` a `(transacción, syncVersion)`).

## Decisión

Paginación **keyset** sobre `(sync_xact, sync_version)` con un **cursor opaco**: base64url de un JSON
versionado `{"v":1,"x":"<transacción>","sv":<versión>}`. La transacción va como texto porque es un
`xid8` (entero de 64 bits sin signo) que no cabe en un número JSON seguro para todos los clientes. La
página pide una fila de más para calcular `hasMore` sin una segunda consulta. Arranque por fecha con
`since` (excluyente con `cursor`); si esa primera página viene vacía, el cursor devuelto recuerda la
fecha (`{"v":1,"since":"…"}`) para no perder lo que llegue después. `nextCursor` siempre presente.

## Alternativas consideradas

### Opción 1: Offset / número de página (`?page=N`)

**Pros:** - Trivial de implementar y de entender. - Permite saltar a una página.
**Cons:** - Inestable bajo escritura: un trámite que cambia mueve a los demás de página y se pierden o
repiten filas. - Coste creciente: `OFFSET 500000` recorre medio millón de filas. - No expresa «desde
dónde quedé» entre corridas.
**Esfuerzo:** S
**Riesgos:** Pérdida silenciosa de cambios; degradación con el volumen. Descartada.

### Opción 2: Cursor por fecha de último cambio (`?since=<timestamp>`)

**Pros:** - Legible para el consumidor. - Sin estado opaco.
**Cons:** - Empates: muchos cambios comparten milisegundo (una importación, un trigger por sentencia).
- `now()` es la hora de inicio de la transacción, no la de confirmación: una transacción larga
confirma con una fecha anterior a la última entregada y se pierde. - Los relojes no son una
secuencia.
**Esfuerzo:** S
**Riesgos:** Pérdida de cambios bajo concurrencia, el mismo defecto que ADR-0066 corrigió. Se conserva
solo como **arranque** (`since`), nunca como cursor de continuación.

### Opción 3: Keyset `(transacción, versión)` con cursor opaco versionado (elegida)

**Pros:** - Estable y sin huecos: junto con el filtro `pg_snapshot_xmin` (ADR-0066 punto 7) nunca
entrega una transacción antes que otra que aún pueda confirmar con clave menor. - Coste constante por
página con el índice `ix_procedure_instances_sync_cursor` (DDL 126). - Opaco: la clave de orden puede
cambiar subiendo `v` sin tocar el contrato. - Reanudable indefinidamente.
**Cons:** - El consumidor no puede inspeccionar ni fabricar cursores. - Los ítems llegan en el orden
del cursor, que no siempre coincide con `syncVersion` entre trámites distintos (la regla de upsert por
`id` con `syncVersion` lo absorbe). - Hay que validar cursores manipulados.
**Esfuerzo:** M
**Riesgos:** Un cursor corrupto o de otra versión: se responde `400 invalid_cursor`, nunca se
reinterpreta. Tope de 512 caracteres antes de decodificar.

## Tradeoff aceptado

Se renuncia a que el cursor sea legible a cambio de no perder cambios bajo concurrencia y poder
cambiar la clave de orden sin romper a nadie. La fecha queda como arranque porque es lo que un
consumidor nuevo sabe expresar («desde hoy»), no como continuación.

## Consecuencias

### Lo que se gana
- Feed sin huecos ni repeticiones por paginación, con coste constante por página.
- Cambios futuros de la clave de orden sin versión nueva del contrato (cursor `v:2`).

### Lo que se pierde
- Legibilidad del cursor; depurar exige decodificarlo (base64url → JSON).
- Orden global por `syncVersion` entre trámites distintos.

### Cambios operacionales
- `pageSize` 1..1000, por defecto 200. `400` con `invalid_cursor`, `invalid_page_size`, `invalid_since`
  o `cursor_and_since_exclusive`.
- `since` exige ISO-8601 con zona: sin zona la fecha se tomaría en la hora local del servidor.
- Un cursor `v:1` debe seguir aceptándose mientras haya consumidores que lo tengan guardado.

## ADRs relacionados

- ADR-0066 — marca de agua de sincronización y recorrido por `(transacción, versión)` (punto 7).
- ADR-0069 — lectura entre compañías acotada y auditada: el ámbito en que corre esta consulta.

## Notas para agentes

- **Backend Agent**: el cursor se codifica y valida solo en `ExternalSyncCursor` (`Flit.Tramites.Domain`).
  Cambiar la clave de orden = nueva versión `v`, sin dejar de leer las anteriores.
- **QA Agent**: probar cursor manipulado, de otra versión, demasiado largo, y `cursor`+`since` juntos.
  El recorrido completo no debe perder ni repetir trámites (`ExternalSyncEndToEndTests`).
- **Security Agent**: el cursor no es un secreto ni una autorización; el alcance lo da el pase.
- **Infra Agent**: sin impacto.
- **Frontend Agent**: sin impacto.

## Referencias externas

- PostgreSQL, `pg_current_snapshot` / `pg_snapshot_xmin`: https://www.postgresql.org/docs/16/functions-info.html
