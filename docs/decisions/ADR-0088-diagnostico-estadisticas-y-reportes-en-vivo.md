# ADR-0088: Diagnóstico — estadísticas y reportes en vivo, con el ámbito global en el backoffice, PII estructurada completa y registro de cada acceso

**Fecha**: 2026-11-15
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT. La aceptación es exclusiva del Líder Técnico humano, en su propio PR (regla FLIT 15). Se apoya en las decisiones de producto del usuario U1–U8 del 2026-11-15 y en los supuestos O1–O10 del orquestador (`.claude/state/diagnostico-ola-ux-dashboard/decisiones-usuario.md`).
**Tags**: arquitectura, backend, frontend, persistencia, backoffice, auditoría, Habeas Data, diagnostico
**Épica / Feature**: proyecto FLIT - DIAGNOSTICO, épica #12679. El Feature y sus HUs los crea el orquestador a partir de `particion-hus.md`.
**Supersedes**: ninguno.
**Enmienda**:
- ADR-0073 (Propuesto): se agregan al «grupo A» (rutas `/backoffice` sin empresa explícita) las rutas `/backoffice/estadisticas/global` y `/backoffice/reportes/{tipo}/global[/excel|/pdf]`, con la auditoría por empresa del resultado. Las rutas con `?empresa=` siguen con `ConEmpresaExplicita` y además validan que la empresa tenga el producto activo.
- ADR-0072 (Propuesto): tabla nueva `accesos_reporte` (solo inserción, sin PII, `@pii:low` en `usuario_id`).

**Diseño detallado**: `.claude/state/diagnostico-ola-ux-dashboard/diseno.md`. Contrato: `contrato-api.md` de esa carpeta. Seguridad: `security-previa.md` (SP-1..SP-18).

## Contexto

El usuario pide un dashboard de estadísticas como inicio del producto para todos los roles, y un módulo de reportes con
pantalla, Excel y PDF:
- los admins y usuarios de compañía ven solo su compañía;
- el super admin ve el global o el de una compañía con el producto activo.

Reglas del pedido:
- U3: datos en vivo, sin vistas materializadas.
- U5: PII completa en pantalla, Excel y PDF para todos los roles.

Restricciones del repo:
- `IgnoreQueryFilters()` solo puede aparecer en `Modulos/Backoffice/`, y un test lo hace cumplir.
- Cada lectura entre compañías se audita en `lecturas_backoffice`.
- La visibilidad `PROPIOS` limita al usuario a sus lotes.
- El documento de las personas está cifrado con Data Protection.
- El nombre del propietario no se persiste.
- Todas las exportaciones actuales enmascaran el documento.
- No hay tabla de auditoría del lado de la compañía.

## Decisión

1. **Estadísticas:** un endpoint agregado por ámbito, calculado en vivo con EF sobre `IQueryable` ya acotados:
   - compañía: filtro de tenant + `AccesoLote`;
   - backoffice: accesos por tabla en `Modulos/Backoffice/`, con `tenant_id IN (compañías habilitadas)`.
2. **Reportes:** un catálogo cerrado de 5 tipos con una **lista blanca de columnas** cada uno. Una respuesta tabular genérica (`columnas` + `filas`) alimenta la pantalla paginada, el Excel (`TabularWorkbookWriter`) y el PDF (QuestPDF).
3. **PII completa = PII estructurada:**
   - entra: placa, VIN, tipo y número **completo** del documento (descifrado solo en memoria) y rol;
   - **no** entra: el nombre (no se persiste, y descifrar la respuesta completa de la fuente queda fuera del uso permitido); tampoco ids internos, seudónimos ni intentos manuales.
   - Las descargas existentes por lote y por vehículo siguen enmascaradas.
4. **Registro:**
   - tabla nueva `accesos_reporte`, solo inserción y sin PII, con una fila por petición de cualquier reporte (pantalla o archivo), escrita **antes** de responder (fail-closed);
   - en el backoffice, además, `lecturas_backoffice` por empresa;
   - el id del registro va impreso en el archivo.
5. **Límites:**
   - rango obligatorio de hasta 366 días;
   - pantalla de 1 a 100 filas por página con desplazamiento ≤ 10 000;
   - Excel hasta 5 000 filas y PDF hasta 1 000; por encima, 422 `REPORTE_DEMASIADO_GRANDE` sin generar nada;
   - 1 exportación simultánea por usuario y 4 por proceso; por encima, 429 `EXPORTACION_EN_CURSO`.
6. **Permisos:** permiso nuevo `diagnostico.reportes.ver` para la pantalla; `reportes.descargar` para exportar; `volumetria.ver` para las estadísticas del backoffice.

## Alternativas consideradas

### Opción 1: endpoint agregado y catálogo genérico en vivo (elegida)

**Pros:**
- Cumple U3 sin vistas ni jobs.
- Una visita deja una fila de auditoría por empresa.
- Un solo componente de tabla en el front.
- Reutiliza el grupo A de saneamiento y los exportadores existentes.
- Una sola migración, la de auditoría.

**Cons:**
- La consulta global en vivo puede ser lenta con mucho volumen.
- El tipado del front es más débil.
- Exige disciplina con la lista blanca.

**Esfuerzo:** M
**Riesgos:** rendimiento global (se mide en BE-1); extracción masiva de PII (acotada por topes, freno y registro; escalada al LT).

### Opción 2: endpoints y DTOs por widget o reporte

**Pros:**
- Contrato explícito por pieza.
- Carga progresiva.

**Cons:**
- Entre 12 y 16 rutas de estadísticas y unas 40 de reportes.
- La auditoría se multiplica por widget.
- 5 componentes de tabla.

**Esfuerzo:** L
**Riesgos:** deriva entre filtros; más superficie que asegurar.

### Opción 3: agregados precalculados (vistas materializadas o tablas por job) y reportes asíncronos en cola

**Pros:**
- Lecturas baratas y exportaciones grandes sin bloquear.
- Histórico sin costo.

**Cons:**
- Contradice U3.
- Requiere migraciones, job, refresco y almacenamiento temporal de archivos con PII (retención y borrado).
- El dato llega desfasado.

**Esfuerzo:** L
**Riesgos:** el refresco que no corre (ya ocurrió en la analítica de Trámites); archivos con PII guardados en el servidor.

## Tradeoff aceptado

Se acepta el costo de CPU y la latencia de la consulta en vivo, acotados por el rango, los topes, el freno y un timeout de
30 s, a cambio de:
- datos exactos;
- ningún archivo con PII guardado en el servidor;
- una sola migración.

Se acepta **no** incluir el nombre del propietario: es la única forma de dar «PII completa» sin persistir PII nueva ni
descifrar la respuesta completa de la fuente fuera del detalle. Si el usuario lo exige, es una decisión aparte que enmienda
el ADR de la HU #14087 AC8.

## Consecuencias

### Lo que se gana
- Dashboard y reportes por rol con aislamiento verificable: tenant, `PROPIOS` y compañías habilitadas.
- Rastreabilidad de cada visualización y descarga (Ley 1581, art. 17 lit. d), incluido el archivo filtrado (registro impreso).
- Un patrón reutilizable de reporte tabular.

### Lo que se pierde
- El precedente «toda exportación enmascara el documento» se rompe en el módulo nuevo (U5).
- La consulta global depende de Identidad: sin la lista de habilitadas, 503.
- Sin p95 de tiempos (solo promedio).

### Cambios operacionales
- Migración `S2_AccesosReporte`.
- Claves opcionales `Diagnostico:Reportes:*`, con valores por defecto en código.
- El permiso nuevo entra por el manifiesto. Hay que verificar que la plataforma lo propague a los roles ya asignados.
- Retención de `accesos_reporte`: se propone la misma de `lecturas_backoffice`. Si esa tabla no tiene política, el LT la define antes del PR.

## Finalidad y base legal (SP-17)

- **Finalidad del módulo de reportes:** seguimiento del diagnóstico vehicular que la compañía contrató.
- **Base legal:** el mismo encargo de tratamiento del diagnóstico. FLIT actúa como encargado y cada compañía como responsable.
- **Decisión U5:** la tomó el usuario el 2026-11-15 y **queda para revisión del Líder Técnico y de seguridad**.
- **Archivos:** no se guardan en el servidor; se generan y se entregan, así que no hay retención de archivos de reportes.

### Riesgo residual escalado al LT

Estos riesgos no se mitigan sin reabrir U5:
- extracción masiva de documentos por cualquier usuario con `reportes.ver` y `reportes.descargar`;
- vista global con PII para el SuperAdmin y para FLIT con `backoffice.ver`. Se sugiere MFA obligatorio para el SuperAdmin y una cláusula en el contrato de encargo;
- control nulo del archivo una vez descargado;
- CPU por las consultas en vivo.

## ADRs relacionados

- [ADR-0069] (`services/core-api/docs/adr/`) — lectura entre compañías acotada y auditada.
- [ADR-0072] — clasificación y retención de tablas de Diagnóstico (enmendado).
- [ADR-0073] (`ADR-0073-diagnostico-acceso-backoffice.md`; hay otro ADR-0073 de permisos que no aplica) — acceso del backoffice y empresa explícita (enmendado: grupo A ampliado).
- [ADR-0087] — saneamiento: precedente del grupo A y de los límites de exportación.

## Notas para agentes

**Backend Agent:**
- La proyección vive en `Modulos/Reportes/Catalogo/` y `Modulos/Estadisticas/`, y recibe `IQueryable` acotados.
- Los accesos globales van en `Modulos/Backoffice/`, por tabla concreta y con joins por `tenant_id`.
- El conteo se hace antes de descifrar.
- Logs solo con `[LoggerMessage]` sin PII.

**Frontend Agent:**
- Las capacidades `verReportes` y `verEstadisticasGlobales` salen de `/sesion`.
- La URL de reportes nunca lleva la placa.
- Nada en `localStorage` ni en `sessionStorage`.
- Las descargas usan `Blob` y llaman a `revokeObjectURL`.

**QA Agent:**
- Matriz de autorización SP-1.
- PROPIOS, compañía apagada y 422/429.
- Sin PII en los logs.
- E2E de dashboard y reportes.

**Security Agent:**
- Verificar SP-1..SP-18 en la Fase 3.
- El riesgo residual va al LT.

**Database Agent:**
- `accesos_reporte` según el checklist §A, solo inserción, comentarios `@pii`.
- Ningún índice nuevo salvo que BE-1 mida p95 > 2 s.

**Infra Agent:** ninguna variable obligatoria nueva.

## Referencias externas

- Ley 1581 de 2012, arts. 4, 12 y 17.
