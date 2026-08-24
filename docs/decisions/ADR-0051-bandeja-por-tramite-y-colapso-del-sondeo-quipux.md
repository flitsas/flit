# ADR-0051: Bandeja por trámite y colapso del sondeo en la trazabilidad Quipux

**Fecha**: 2026-08-24
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), equipo core-api, equipo frontend
**Tags**: arquitectura, backend, frontend, quipux, log-qx, soporte, feature-11784
**Relacionado**: Feature #10792 (HUs #10793 a #10796) — experiencia que este ADR reemplaza; `ADR-0024` (workers Quipux dentro de core-api con claim `FOR UPDATE SKIP LOCKED`); `ADR-0047-gate-navegacion-dock-igual-url.md` (gate del dock por permiso)
**HU origen**: Feature #11784 — HUs #11785 a #11790

---

## Contexto

El módulo LOG QX expone la trazabilidad de la integración Quipux para soporte y administración
de FLIT. La implementación vigente (Feature #10792) resolvió la captura de eventos, el endpoint
de consulta, el gate por permiso `logqx.read` y el enmascarado de datos sensibles. Su interfaz,
en cambio, exige una búsqueda exacta por uno de tres ejes excluyentes para mostrar cualquier
dato, y vuelca los eventos de una radicación sin jerarquía alguna.

Con los cinco registros de datos de ejemplo sembrados en DEV el resultado es manejable. Con datos
reales no lo es.

### El caso de referencia

El trámite **27172** de FLIT 1.0 (matrícula inicial, Secretaría de Ibagué) acumula **1.065
eventos** para una sola radicación: uno cada diez minutos durante 7,4 días, todos con
`codigo = 81` («Los datos se almacenaron correctamente») y `estadoTramite.codigo = 1` («sin
cambios»). Los eventos que aportan información distinta son **cinco**: el consolidado, la subida
del documento, el envío de la radicación, la respuesta de registro y —cuando llegue— la decisión
del organismo.

FLIT 1.0 presenta esos 1.065 registros como 107 páginas de diez filas, sin filtros internos y sin
forma de saltar al evento relevante. FLIT 2.0, con su diseño actual, los volcaría todos dentro de
una misma tarjeta. Las dos versiones cometen el mismo error en espejo: **tratan un latido de
sondeo como si fuera un hito**.

### Hechos que condicionan el diseño

1. **Un trámite puede acumular varias radicaciones.** La consulta de elegibilidad del worker
   excluye únicamente las radicaciones en estado `pendiente` o `registrado`
   (`NOT EXISTS (... s.status IN ('pendiente','registrado'))`). Una radicación fallida o
   rechazada vuelve a hacer elegible el trámite y genera una radicación nueva.
2. **El radicado de Quipux no se persiste.** FLIT 1.0 lo almacena y lo expone como `idQuipux`
   (por ejemplo, 1974679 para el trámite 27172). FLIT 2.0 no guarda ese identificador, de modo
   que no es posible buscar por él ni contrastar una radicación con la secretaría. El eje que la
   interfaz actual denomina «Código QX» filtra en realidad por `qx_register_code`, por lo que
   buscar `81` devuelve todas las radicaciones exitosas: es un filtro de estado presentado como
   identificador.
3. **La antigüedad de un trámite elegible sin radicar sí es derivable.** Sale de
   `tramites.procedure_instance_status_history` (`to_status = 'preparado'`). No requiere
   persistencia adicional.

### Restricción transversal

**No se modifica el comportamiento de la integración Quipux.** El alcance de este ADR y del
Feature #11784 es lectura y presentación. La lógica de radicación, sondeo, elegibilidad y
consolidado permanece intacta.

---

## D1 — Unidad de fila de la bandeja

### Alternativa A — Una fila por radicación (modelo vigente)

- **Pros**: proyección directa sobre `tramites.quipux_submissions`; sin agregación.
- **Contras**: un trámite con tres intentos aparece tres veces; una búsqueda por placa devuelve
  duplicados aparentes que confunden a quien consulta; no admite representar los trámites
  elegibles que aún no tienen radicación, que son precisamente el caso más costoso para soporte.
- **Esfuerzo**: bajo. **Riesgo**: la lista deja de ser legible en cuanto hay reintentos.

### Alternativa B — Una fila por trámite (elegida)

- **Pros**: coincide con la forma en que llega la consulta a soporte, que siempre parte del
  trámite o de la placa; admite los elegibles sin radicación; el historial de intentos queda
  donde pertenece, dentro de la trazabilidad.
- **Contras**: exige agregación por trámite y una unión con los elegibles sin radicación.
- **Esfuerzo**: medio. **Riesgo**: consulta más pesada; ver la sección de riesgos.

**Decisión: alternativa B.** El costo es una consulta más elaborada; el beneficio es que la
lista responde la pregunta que soporte trae realmente.

---

## D2 — Dónde se agrupa el sondeo repetido

### Alternativa A — Agrupar en el navegador

- **Pros**: el endpoint no cambia; la regla de agrupación vive en un solo lugar.
- **Contras**: transfiere 1.065 eventos para representar cinco, y ese número crece sin techo
  mientras el organismo no resuelva; el filtro de solo errores obligaría a descargar el histórico
  completo antes de poder aplicarse.
- **Esfuerzo**: bajo. **Riesgo**: alto — el tiempo de apertura degrada con la antigüedad del
  trámite, justo en los casos que más se consultan.

### Alternativa B — Agrupar en el servidor (elegida)

- **Pros**: payload acotado y constante con independencia de la antigüedad; filtros y paginación
  resueltos en SQL; tiempo de apertura estable.
- **Contras**: dos endpoints en lugar de uno; la regla de agrupación vive en backend.
- **Esfuerzo**: medio. **Riesgo**: bajo.

### Alternativa C — Vista materializada de hitos

- **Pros**: lectura mínima en consulta.
- **Contras**: introduce sincronización sobre datos que cambian cada diez minutos por radicación
  activa; complejidad no justificada al volumen actual.
- **Esfuerzo**: alto. **Riesgo**: desincronización silenciosa entre la vista y los eventos.

**Decisión: alternativa B.** La regla de agrupación queda definida así: *eventos consecutivos
de etapa de consulta, resultado correcto y sin cambio de estado del trámite*. Cualquier evento
que rompa la condición corta el bloque y se emite como hito propio.

---

## D3 — Ubicación de la pantalla de trazabilidad

### Alternativa A — Panel lateral sobre la bandeja

- **Pros**: conserva el contexto de la lista; patrón ya presente en el proyecto
  (`PrevalidacionDetailDrawer`, `OtSidePanel`, `DrilldownPanel`).
- **Contras**: el ancho disponible (480–600 px) es insuficiente para comparar los payloads
  enviado y recibido; el contenido —resumen, hitos, log paginado y detalle técnico— se convierte
  en un desplazamiento largo dentro de una columna estrecha, que es una forma peor del mismo
  problema que se quiere resolver.
- **Esfuerzo**: medio.

### Alternativa B — Pantalla propia bajo `/log-qx/{id}` (elegida)

- **Pros**: ancho suficiente para la comparación de payloads; URL propia, adjuntable a un ticket
  de soporte; el gate de `logqx.read` se aplica sobre una ruta dedicada, sin condicionar
  pantallas compartidas.
- **Contras**: se abandona la bandeja al navegar, mitigado llevando los filtros en el query
  string para restituirlos al volver.
- **Esfuerzo**: medio.

### Alternativa C — Pestaña dentro de `/tramites/{instanceId}`

- **Pros**: sitúa la trazabilidad junto a documentos, checklist y estado del trámite, que es
  información que soporte suele necesitar en la misma sesión.
- **Contras**: introduce una herramienta de diagnóstico interna de FLIT dentro de una pantalla
  que consultan otros roles, y obliga a gatear una pestaña por permiso dentro de una vista
  compartida.
- **Esfuerzo**: medio. **Riesgo**: erosión del límite de acceso.

**Decisión: alternativa B**, complementada con un vistazo expandible en la propia fila de la
bandeja para el caso frecuente, que se resuelve sin navegar. Se conserva el enlace cruzado en
ambos sentidos entre la trazabilidad y el detalle del trámite.

---

## D4 — Persistencia del radicado de Quipux

### Alternativa A — No persistir (situación vigente)

- **Pros**: ningún cambio en el worker de radicación.
- **Contras**: imposibilita la búsqueda por radicado y el contraste con la secretaría, que es el
  número por el que el organismo y el cliente identifican el trámite.
- **Esfuerzo**: nulo. **Riesgo**: la carencia se vuelve más cara cuanto más tarde se corrija,
  porque el histórico sin radicado crece.

### Alternativa B — Persistir el identificador al radicar (elegida)

- **Pros**: habilita búsqueda y contraste; recupera la paridad con FLIT 1.0.
- **Contras**: toca el worker de radicación y exige una migración.
- **Esfuerzo**: bajo. **Riesgo**: bajo — el cambio se limita a leer un campo de la respuesta y
  guardarlo, sin alterar el flujo.

**Decisión: alternativa B.** Es el único cambio de este Feature que toca el worker. **Sin
backfill**: las radicaciones anteriores a la migración quedan con el radicado vacío y la interfaz
las presenta así, sin error.

---

## Consecuencias

### Backend (core-api)

- Columna nueva en `tramites.quipux_submissions` con su migración EF, y lectura del identificador
  en la respuesta de registro — HU #11785.
- Consulta de bandeja con agregación por trámite y unión de los elegibles sin radicación,
  replicando el predicado de elegibilidad del worker (`external_refs -> 'quipux'`, banderas
  `quipux_registration` / `quipux_transfer` / `quipux_other` de la secretaría y DIVIPO presente)
  — HU #11786.
- Dos endpoints de trazabilidad: hitos con la agrupación resuelta en servidor, y eventos con
  paginación y filtros en SQL — HU #11787.
- Se reutiliza `LogQxSensitiveDataMasker` sin modificaciones. La lectura sigue siendo
  cross-tenant, igual que el repositorio actual del LOG QX.

### Frontend

- Se reescribe `components/atom/modules/LogQx.tsx` como bandeja — HU #11788.
- Ruta nueva `/log-qx/{id}` con dos pestañas — HUs #11789 y #11790.
- Los filtros viajan en el query string para que el retorno desde la trazabilidad los conserve.
- La traducción de códigos de Quipux (81, 72, 76), estado del trámite (1, 2, 3) y origen de los
  eventos vive en esta capa, siguiendo el patrón de `STAGE_LABEL` ya presente. Se corrige la
  etiqueta vigente que presenta los códigos de negocio como códigos HTTP.

### QA

- El caso 27172 es el escenario de prueba obligatorio: con el interruptor de ocultar consultas
  activo deben verse alrededor de seis eventos; al desactivarlo deben recuperarse los 1.065 sin
  pérdida de ningún registro.
- Debe verificarse un trámite con varias radicaciones y un trámite elegible sin radicación.

### Seguridad

- Sin cambios en el modelo de acceso: permiso `logqx.read`, lectura cross-tenant, destinada
  exclusivamente a soporte FLIT. El enmascarado de datos sensibles se conserva en ambos endpoints.
- El módulo permanece de **solo lectura**. Las acciones de reintentar y cancelar siguen viviendo
  en la consola de cola QX y no se incorporan aquí.

### Infraestructura

- Sin impacto. No se introducen servicios, jobs ni dependencias nuevas.

---

## Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| La regla de agrupación oculta un evento relevante porque un cambio de estado no se refleja en `estadoTramite` | El interruptor del log completo devuelve la totalidad de los eventos, y el filtro de solo errores los expone con independencia de la agrupación |
| La consulta de bandeja es la más pesada del módulo (agregación, unión de elegibles y filtros combinables) | Revisar los índices sobre `procedure_instance_id` y `occurred_at` antes del despliegue a PDN; filtros y paginación resueltos en SQL, nunca en memoria |
| El histórico sin radicado convive con el nuevo | La interfaz lo presenta vacío sin error (AC3 de la HU #11785); no se hace backfill |
| Los datos de ejemplo del LOG QX pueden estar sembrados fuera de DEV | La migración `F11_LogQxMockSeed` se activa con `ASPNETCORE_ENVIRONMENT` en `Development`, valor que según `docker-compose.prod.yml` usan DEV, QA y PDN por igual. Verificar y, en su caso, retirar los registros `QXSEED` de los ambientes que no correspondan. Queda fuera del alcance de este Feature |

---

## Notas

El diseño fue validado sobre un prototipo interactivo antes de redactar las historias, con el
caso 27172 y sus 1.065 consultas reproducido para comprobar el colapso del sondeo.
