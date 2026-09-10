# ADR-0051: Bandeja por trámite y colapso del sondeo en la trazabilidad Quipux

**Fecha**: 2026-08-24
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), equipo core-api, equipo frontend
**Tags**: arquitectura, backend, frontend, quipux, log-qx, soporte, feature-11784
**Relacionado**: Feature #10792 (HUs #10793 a #10796) — experiencia que este ADR reemplaza; `ADR-0024` (workers Quipux dentro de core-api con claim `FOR UPDATE SKIP LOCKED`); `ADR-0047-gate-navegacion-dock-igual-url.md` (gate del dock por permiso)
**HU origen**: Feature #11784 — HUs #11786 a #11790 (la #11785 quedó cancelada; ver D4)

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
2. **Quipux no emite ningún radicado.** Verificado sobre el evento de radicación original del
   trámite 27172: el envío contiene `vin`, `placa`, `documento`, `consumidor`, `tipoTramite`,
   `codigoDivipo`, `documentoFlit`, `tipoRequisito`, `idRegistration` y los documentos de
   propietario y funcionario; la respuesta contiene únicamente `codigo`, `descripcion` y
   `status`. El valor `idQuipux` (1974679) aparece solo en el *request* de la consulta de estado
   y es un identificador **interno de FLIT 1.0** — la clave primaria de su propia tabla de
   radicaciones —, cuyo equivalente en FLIT 2.0 ya existe: `quipux_submissions.id`.
   Aun así, la identificación ante la secretaría sigue sin resolverse en la interfaz vigente: el
   eje que denomina «Código QX» filtra en realidad por `qx_register_code`, por lo que buscar `81`
   devuelve todas las radicaciones exitosas. Es un filtro de estado presentado como identificador.
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

## D4 — Cómo se identifica una radicación ante la secretaría

La formulación inicial de esta decisión era «persistir el radicado que devuelve Quipux». La
verificación descrita en el contexto la invalidó: **ese radicado no existe**. La decisión real es
qué dato cumple esa función.

### Alternativa A — Persistir un identificador propio y exponerlo

- **Pros**: control total sobre el formato.
- **Contras**: un identificador que solo conoce FLIT no sirve para contrastar con la secretaría,
  que es justamente para lo que se necesita. Reproduce el `idQuipux` de FLIT 1.0, que tampoco
  significaba nada fuera de FLIT.
- **Esfuerzo**: bajo. **Riesgo**: resuelve la forma y no el problema.

### Alternativa B — Exponer el nombre del documento, ya persistido (elegida)

- **Pros**: es la llave real de correlación — Quipux localiza el trámite por `documento` más
  organismo, y es el dato que viaja en el envío y en la consulta de estado. Ya está almacenado en
  `quipux_submissions.document_name`. **No requiere migración, ni columna nueva, ni tocar el
  worker.**
- **Contras**: es una cadena larga (`TESLA_MI_20260811_1220_LRWYGCFJ3TC767907`), menos cómoda de
  dictar por teléfono que un número corto. Se mitiga admitiendo búsqueda por fragmento.
- **Esfuerzo**: nulo en persistencia; se resuelve dentro de la consulta de bandeja.

**Decisión: alternativa B.** Se expone como **Documento QX** en la bandeja y en la cabecera de la
trazabilidad, y se admite como filtro por coincidencia parcial.

Consecuencia relevante: con esta decisión **el Feature #11784 queda íntegramente de solo lectura**
y no toca la integración Quipux en ningún punto, ni modifica el esquema de datos.

> FLIT 2.0 calcula `document_name` una sola vez y lo persiste, corrigiendo el defecto de FLIT 1.0
> que lo regeneraba en cada intento con precisión de minuto. La evidencia está en el propio
> trámite 27172: su evento de registro usa `TESLA_MI_20260811_1220_…` y los de sondeo
> `TESLA_MI_20260818_1740_…`. Dos nombres para el mismo trámite, que en 1.0 lo volvían
> inconsultable y podían duplicarlo en Quipux.

---

## Consecuencias

### Backend (core-api)

- **Sin cambios de esquema y sin migraciones.** La HU #11785, que preveía una columna nueva y una
  modificación del worker de radicación, quedó cancelada al invalidarse su premisa (ver D4).
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
| El nombre del documento es largo y engorroso de dictar | La búsqueda admite coincidencia parcial, de modo que basta la placa o el VIN que el propio nombre incorpora |
| Los datos de ejemplo del LOG QX pueden estar sembrados fuera de DEV | La migración `F11_LogQxMockSeed` se activa con `ASPNETCORE_ENVIRONMENT` en `Development`, valor que según `docker-compose.prod.yml` usan DEV, QA y PDN por igual. Verificar y, en su caso, retirar los registros `QXSEED` de los ambientes que no correspondan. Queda fuera del alcance de este Feature |

---

## Notas

El diseño fue validado sobre un prototipo interactivo antes de redactar las historias, con el
caso 27172 y sus 1.065 consultas reproducido para comprobar el colapso del sondeo.
