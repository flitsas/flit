# ÉPICA — Sincronización incremental de trámites hacia Flito

> **✅ REGISTRADO EN ADO** (2026-09-21). Épica **#12737** creada en `FLIT - EVOLUTION` (iteración raíz),
> estado `New`, asignada a David Alejandro Chica Hernandez, tags `DOR; adopcion-ia; fase-1-diseño`,
> con comentario de trazabilidad. Rama `develop` @ `7273bb43`.
> Enlace: https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12737
> Contrato reconciliado con la sesión consumidora de Flito (su PO confirmó el JSON anidado sin cambios).
>
> **⚠️ AJUSTADO A LA ESTRATEGIA v3 (2026-09-29) — PENDIENTE DE REFLEJAR EN ADO.** Solo pull: se
> retira el aviso de cambios de la v2. Borradores fuera. El texto publicado en el work item #12737
> sigue siendo el del 2026-09-21. Acuerdos en los Anexos E y F; contrato de cable v3 en
> `docs/integraciones/external-api-tramites-sync.md`.

| Campo | Valor |
|-------|-------|
| Tipo de work item | `Epic` |
| Título | `[INTEGRACIONES] — Sincronización incremental de trámites hacia Flito` |
| Estado | `New` (activación = gate humano; `Closed` exclusivo del PO) |
| Iteración | `FLIT - EVOLUTION` (raíz; las Features hijas se ubican en el sprint siguiente al activo) |
| Área | `FLIT - EVOLUTION` |
| AssignedTo | David Alejandro Chica Hernandez (david.chica@flitsas.com) |
| Tags | `DOR; adopcion-ia; fase-1-diseño` |
| ADO ID | **#12737** |
| Consumidor | Flito (sistema propio de FLIT; consulta automática cada 5 min y a demanda, desde la radicación y sin carga inicial del histórico) — Épica Flito **#12736** (registra #12737 como dependencia externa) |
| Contrato técnico | `docs/integraciones/external-api-tramites-sync.md` |
| Features hijas | Anexo B (crear con `feature-creator`, vínculo `System.LinkTypes.Hierarchy-Reverse`) |

---

## OBJETIVO

Exponer en la plataforma FLIT un servicio de sincronización incremental de trámites, de alcance
cross-compañía, seguro y auditable, que permita a Flito (sistema propio de FLIT con otro propósito)
obtener, en cada consulta periódica o a demanda, todo trámite radicado
cambiado desde su última lectura —con datos del vehículo, del comprador, del organismo de tránsito, de
la compañía gestora, la referencia al adjunto de factura y las fechas clave— para ejecutar sus procesos
posteriores sin intervención manual ni cargas completas en cada corrida.

## DESCRIPCIÓN

### Contexto

Flito necesita conocer el universo de trámites de todas las compañías y reaccionar a cualquier cambio
(estado, datos del vehículo, actores, adjuntos). Hoy la plataforma no ofrece un canal de lectura para
sistemas externos: la API exige sesión de usuario y opera acotada a una compañía, los listados
actuales cargan el detalle completo de cada trámite y están limitados a 200 filas, y no existe una
marca confiable de "última modificación" que abarque los datos relacionados del trámite. Flito
sincronizará con una corrida programada cada 5 minutos y un botón «Sincronizar» a demanda, con un
volumen de referencia de ~2.700 trámites/mes, arrancando desde el presente sin cargar el histórico.

### Marca de agua de cambios

Cada trámite mantiene un número de versión de sincronización global y estrictamente creciente, junto
con la fecha del último cambio, que se actualizan automáticamente en la base de datos ante cualquier
modificación del trámite o de sus datos relacionados: actores, campos del vehículo, historial de
estado, adjuntos y datos comerciales. Un guardado que toca varios registros relacionados en una misma
operación produce un único incremento. Se realiza una asignación inicial de versión a todo el
histórico, de modo que una eventual carga completa usa el mismo mecanismo que la sincronización
diaria (la puesta en marcha con Flito no la usa: arranca desde el presente). Se aplica una ventana de
estabilidad de 5 s, configurable, para no entregar cambios de transacciones aún no confirmadas. Los trámites eliminados lógicamente también actualizan su versión y
se entregan como marca de borrado.

### Servicio de sincronización

Un endpoint de solo lectura bajo un prefijo dedicado a integraciones externas recibe un cursor opaco y
un tamaño de página, y devuelve los trámites modificados después de ese cursor, ordenados por versión
de sincronización, con el cursor siguiente y un indicador de páginas restantes. Cuando no se dispone
de cursor se puede iniciar desde una fecha, que filtra por fecha de último cambio (no por fecha de
creación), de modo que se reciben cambios de estado de trámites antiguos. La ausencia de resultados
responde vacío, nunca error. La semántica es "al menos una vez": Flito realiza upsert por
identificador estable y descarta versiones ya procesadas.

Cada ítem incluye: identificador estable, radicado y consecutivo, versión y fecha de último cambio,
marca de borrado, estado (código interno), tipo de trámite (código, nombre y familia), fechas de
creación, radicación y aprobación; vehículo (VIN, placa, clase, marca, línea, año modelo, carrocería,
cilindraje, capacidad, número de motor, número de serie, tipo de servicio); organismo de tránsito
(código RUNT, nombre, código de secretaría DIVIPOLA, ciudad y departamento); compradores —o propietarios
en su defecto—, todos los copropietarios con su orden y porcentaje de participación (rol, tipo de persona, tipo de documento en código canónico de FLIT, número, nombre
completo, dirección, ciudad, celular y correo); adjunto de factura (identificador, nombre de archivo y
fecha de carga); y compañía gestora (identificador, NIT y razón social). Todas las claves están
siempre presentes; un dato inexistente se entrega como nulo, nunca como cadena vacía o espacio.

Se entregan los trámites **radicados al menos una vez**, es decir, los que alguna vez han llegado al
organismo de tránsito. Un trámite que nunca se radicó —en borrador o preparado— no se entrega: es
trabajo en curso interno de la empresa, y la plataforma ya aplica ese mismo criterio con el organismo
de tránsito, que tampoco los ve. **Una vez entregado por primera vez, el trámite permanece en la
sincronización de forma definitiva:** si retrocede a borrador o a preparado, se entrega como un cambio
de estado más y nunca como una baja. La única forma de baja es la marca de borrado.

### Frecuencia de consulta

El consumidor consulta de forma programada (cada 5 minutos) y a demanda, siempre por el mismo cursor y
sin dos lecturas simultáneas. La plataforma no envía avisos: una consulta sin cambios devuelve una
respuesta vacía de bajo coste, y la frescura de los datos en el consumidor es la de su intervalo.

### Descarga del adjunto de factura

Bajo el mismo esquema de autenticación, un endpoint entrega una URL firmada de corta vida para
descargar el adjunto de factura de un trámite a partir de su identificador de adjunto, reutilizando el
mecanismo de URLs firmadas ya existente en la plataforma. Cada solicitud queda en la bitácora de
acceso externo.

### Autenticación de clientes externos

Se incorpora un modelo de clientes de integración propio de la plataforma: credenciales con secreto
almacenado únicamente como hash robusto, alcances (scopes) explícitos, rotación de secreto, bloqueo
temporal por intentos fallidos y desactivación. El cliente obtiene un token de corta vida mediante
credenciales de cliente y lo presenta en cada consulta. Se provisiona un cliente por ambiente
(DEV, QA, PDN). La administración de clientes —alta, rotación, desactivación y consulta de accesos—
queda disponible para el superadministrador.

### Protección, auditoría y datos personales

El prefijo externo tiene límite de tasa por cliente y límite por IP en la obtención de token, y
registra cada solicitud en una bitácora de acceso (cliente, rango de versiones sincronizado, cantidad
de ítems, compañías tocadas, IP, duración y resultado) con trazabilidad suficiente para reconstruir
qué datos personales consultó cada cliente, en cumplimiento de la Ley 1581 de 2012. Los campos
personales se identifican en el contrato y su entrega sin enmascarar depende de un alcance específico
que Flito posee. El acceso cruza compañías mediante un ámbito de lectura explícito, exclusivo del
servicio externo y cubierto por pruebas de arquitectura; nunca reutiliza el ámbito del
superadministrador.

### Contrato y documentación

El contrato se publica en la especificación OpenAPI de la plataforma con el nuevo esquema de
seguridad, los campos personales marcados y ejemplos con datos ficticios, y se acompaña de una guía de
consumo para Flito (flujo de token, manejo de cursor, idempotencia por identificador, reintentos ante
límite de tasa) en un documento del repositorio que Flito puede copiar al suyo.

## CRITERIOS FUNCIONALES

### Marca de agua

- Cualquier cambio en un trámite o en sus actores, campos de vehículo, historial de estado, adjuntos o
  datos comerciales hace que el trámite aparezca en la siguiente sincronización.
- Un guardado que modifica varios registros relacionados en una sola operación produce un único
  incremento de versión.
- Todo el histórico queda con versión asignada en orden de creación, sin generar ruido en la auditoría
  de negocio.
- Los borrados lógicos actualizan la versión y se entregan como marca de borrado.

### Sincronización

- Un cliente con cursor válido obtiene únicamente trámites modificados después de ese cursor, en orden
  estable, sin omisiones aun con transacciones concurrentes.
- La carga inicial completa se realiza con el mismo endpoint iterando páginas desde cursor vacío; el
  parámetro de fecha inicial filtra por fecha de último cambio. Esta capacidad se conserva, pero la
  puesta en marcha con Flito NO la usa: arranca desde el presente. Recuperar el histórico más adelante
  es una sola corrida desde cursor vacío, sin cambios de código.
- Sin resultados, el endpoint responde 200 con lista vacía y cursor.
- Cada ítem incluye todos los campos acordados con Flito, con todas las claves presentes y nulos
  explícitos; el mapa de origen de cada campo está documentado.
- El tipo de documento se entrega en el código canónico de FLIT; la fecha de aprobación corresponde a
  la última transición a «aprobado» del historial.
- Se entregan los trámites radicados al menos una vez; los que nunca salieron de borrador o preparado
  no se entregan, ni los migrados desde FLIT 1, que ya llegan a Flito por esa fuente.
- Se entregan todos los compradores del trámite (copropiedad, hasta 4) con orden y porcentaje; sin
  ninguno, la lista va vacía y el trámite se entrega igual.
- Un trámite ya entregado que retrocede a borrador o preparado sigue entregándose, como un cambio de
  estado más y nunca como una baja: la única forma de baja es la marca de borrado.
- La regla se evalúa sobre el historial de estados del trámite, no sobre marcas auxiliares del flujo
  de identidad: una vez que el trámite llegó al organismo, la condición se cumple para siempre.
- Un cursor corrupto, un tamaño de página fuera de rango, una fecha mal formada o el cursor y la fecha
  de inicio en la misma consulta responden 400 con detalle estándar.
- Una página de tamaño máximo responde por debajo de 1,5 s (p95) en QA con datos representativos y
  siempre por debajo de 30 s.

### Adjunto de factura

- Con identificador de trámite y de adjunto válidos se obtiene una URL firmada de corta vida; un
  adjunto inexistente o ajeno al trámite responde 404.

### Autenticación y protección

- Solo un cliente autenticado con el alcance de lectura de trámites accede al servicio; sin token o
  con alcance insuficiente recibe 401/403.
- Los secretos nunca se almacenan ni exhiben en claro; se muestran una única vez al crear o rotar.
- Cinco intentos fallidos consecutivos bloquean temporalmente al cliente durante 15 minutos, con
  desbloqueo automático o por administrador.
- Un cliente que exceda su cuota recibe 429 con tiempo de espera indicado.
- Cada solicitud queda registrada en la bitácora de acceso externo.
- Sin el alcance de datos personales, los campos sensibles se entregan enmascarados.

### Contrato

- El contrato figura en la especificación OpenAPI con esquema de seguridad propio, campos personales
  marcados y ejemplos ficticios, y supera la validación automática del repositorio.

## DEPENDENCIAS Y RESTRICCIONES

- Cambio de esquema en la tabla de trámites (columnas de versión y fecha de sincronización, secuencia
  global, índices) y triggers en cinco tablas relacionadas, con asignación inicial de versión al
  histórico en una ventana de mantenimiento corta.
- Lectura cross-compañía: exige un ámbito explícito y auditado, exclusivo del servicio externo, que
  no se puede activar desde ningún otro endpoint.
- El Gateway debe enrutar el prefijo externo sin exigir sesión de usuario y con tiempo de espera
  suficiente; la autenticación la resuelve la API con su esquema dedicado.
- La lectura es en vivo sobre la base transaccional (sin réplica); el límite de tasa y el tamaño de
  página acotado la protegen.
- Datos personales sujetos a Ley 1581: finalidad documentada, acceso auditado, retención de bitácora
  de 12 meses. Ningún ejemplo en ADO, documentación o pruebas contiene datos reales.
- FLIT no almacena factura contable: se entrega la referencia al adjunto de factura y su descarga.
- FLIT no separa nombres y apellidos: se entrega nombre completo (razón social en persona jurídica).
- La puesta en marcha debe fijar el punto de arranque con la hora real del momento, no con una fecha
  escrita de antemano: un punto anterior a la asignación inicial de versiones entrega el histórico
  completo sin que nada falle ni avise.
- PRs acotadas (≤ 800 líneas) a `develop`; las decisiones técnicas nacen como ADR en estado Propuesto.

## FUERA DE ALCANCE

- Cualquier escritura desde Flito hacia FLIT.
- Avisos de cambio hacia el consumidor (webhook, bus de eventos o broker): descartados en v3; la
  integración es solo por consulta.
- Separación de nombres y apellidos; captura de factura contable; datos comerciales (valor de venta,
  método de pago); datos del gestor asignado.
- Réplica de lectura, data warehouse o vistas materializadas.
- Portal de autoservicio para clientes externos (solo administración por superadministrador).
- Habilitación de terceros ajenos a FLIT (el modelo lo permite, pero no se provisiona).
- Cómo Flito trata internamente los ítems recibidos (por ejemplo, la marca de borrado pasa el trámite
  a anulado en Flito): es decisión de Flito.

## DECISIONES CERRADAS (2026-09-22)

Las cuatro decisiones que quedaban pendientes desde el 2026-09-21 las cerró el PO:

- Tamaño de página máximo **1.000** ítems y cuota de **120 solicitudes/min** por cliente (Flito usará
  páginas de 500).
- Ventana de estabilidad **5 s**; bloqueo temporal por intentos fallidos **15 min**.
- Retención de la bitácora de acceso externo **12 meses**.
- Vigencia de la URL firmada del adjunto **10 min**, reutilizando el mecanismo existente (el ADR-0029
  exige ≤ 15 min). El tiempo de expiración informado es orientativo: el real lo firma el servicio de
  almacenamiento y puede no coincidir, así que el consumidor debe descargar de inmediato y volver a
  pedir la URL si falla, en vez de confiar en el dato informado.

Fechas: se sostienen las comprometidas (DEV ~20-10, QA ~03-11, PDN ≥ 10-11); retirar el canal de aviso
(v3) quita trabajo y no toca el camino crítico.

## PENDIENTE DE UN HUMANO (no lo resuelve el equipo de desarrollo)

- **Entrega del secreto del cliente de Flito** (uno por ambiente). Se exhibe una sola vez al darlo de
  alta y debe llegar a Flito por un canal fuera de banda; no puede viajar por herramientas de trabajo
  cuyo historial quede en disco.

---

## Anexo A — Contrato de respuesta y mapa de origen

Contrato completo, ejemplos y guía de consumo: `docs/integraciones/external-api-tramites-sync.md`.
Tabla resumida (PII marcado):

| Clave JSON | Campo Flito | Origen FLIT | PII |
|---|---|---|---|
| `id` | — | `procedure_instances.id` (clave de upsert) | |
| `radicado` | Id | `reference_number` (`FTn-NNNNNNN`, único e inmutable) | |
| `consecutivo` | — | `consecutivo` | |
| `syncVersion` / `fechaUltimoCambio` | — | `sync_version` / `sync_changed_at` (nuevos) | |
| `eliminado` | — | `deleted_at IS NOT NULL` | |
| `estado` | Estado | `status` (código interno) | |
| `tramite.{codigo,nombre,familia}` | Tramite | `procedure_types.{code,name,family}` | |
| `fechaCreacion` / `fechaRadicacion` | fechaCreacion | `created_at` / `submitted_at` | |
| `fechaAprobacion` | fecha_aprobacion | `MAX(status_history.changed_at) WHERE to_status='aprobado'` | |
| `vehiculo.vin` / `.placa` | Vin / Placa | `vin` / `plate` (denormalizados) | |
| `vehiculo.clase` / `.marca` / `.linea` | clase / marca / modelo | `field_values` `vehicle_class` / `vehicle_brand` / `vehicle_line` | |
| `vehiculo.modeloAno` | modeloAno | `vehicle_year` (fallback legado `vehicle_model`) | |
| `vehiculo.carroceria` / `.cilindraje` / `.capacidad` | carroceria / cilindraje / capacidad | `vehicle_body_type` / `vehicle_engine_displacement` / `vehicle_passengers` | |
| `vehiculo.numeroMotor` / `.numeroSerie` | numeroMotor / numeroSerie | `vehicle_engine_number` / `vehicle_series` | |
| `vehiculo.tipoServicio.{codigo,nombre}` | tipoServicio | `vehicle_service` normalizado (`VehicleServiceTypeCode`) | |
| `organismo.codigoTransito` / `.nombre` | — / Transito | `catalogs.transit_offices.code` / `.name` | |
| `organismo.codigoSecretaria` / `.ciudad` / `.departamento` | codigoSecretaria / Ciudad / — | `.city_code` / `.city_name` / `.department_name` | |
| `compradores[].ordinal` / `.porcentajeParticipacion` | orden / porcentaje | `actors.ordinal` / `.ownership_percentage` (ADR-0053) | |
| `compradores[].rolActor` / `.tipoPersona` | — | `actors.actor_type` (comprador > propietario) / `.person_type` | |
| `compradores[].tipoDocumento` | tipo | `actors.document_type` (CC, NIT, CE, PAS, TI…) | |
| `compradores[].numeroDocumento` | cedulanit | `actors.document_number` | alta |
| `compradores[].nombreCompleto` | nombres + apellidos | `actors.full_name` | media |
| `compradores[].direccion` / `.ciudad` | direccion / — | `actors.metadata.direccion` / `.ciudad` | alta / — |
| `compradores[].celular` / `.correo` | celular / correoelectronico | `actors.phone` / `.email` | media / alta |
| `factura.{adjuntoId,nombreArchivo,cargadaEn}` | factura | `procedure_instance_attachments` `tipo='factura'` más reciente | |
| `companiaGestora.{tenantId,nit,nombre}` | CompaniaGestora | `identity.tenants.{id,tax_id,legal_name}` | |

## Anexo B — Features hijas propuestas

**Creadas en ADO el 2026-09-29** en Sprint 9 (el activo, por decisión del PO), hijas de #12737. Tags:
`DOR; adopcion-ia; fase-1-diseño`. Prioridad: F1 → F3, con F2 en paralelo (sin F2 el endpoint no es
alcanzable desde fuera); después F4; F5 opcional.

### F1 — #13062 `[TRAMITES] - Marca de agua de sincronización de trámites` (3 HU)
**Objetivo:** dotar a cada trámite de una versión de sincronización global que cambie ante cualquier
modificación propia o de sus datos relacionados, con asignación inicial al histórico e índices de soporte.
- Al modificar el trámite, un actor, un campo de vehículo, el historial de estado, un adjunto o datos
  comerciales, la versión aumenta y la fecha de último cambio se actualiza.
- Un guardado que toca varios registros hijos en una sola operación produce un único incremento.
- Todo el histórico queda con versión asignada en orden de creación sin ruido en la auditoría de negocio.
- Existe índice para recorrer trámites por versión en tiempo constante por página, e índices de apoyo
  para aprobación (historial) y adjunto de factura.
- Los borrados lógicos también actualizan la versión.
HUs: (1) columnas + secuencia + trigger del padre; (2) triggers statement-level en hijas + backfill;
(3) índices de apoyo + validación de esquema + ADR.

### F2 — #13065 `[INTEGRACIONES] - Cliente de integración para sistemas externos` (3 HU)
**Objetivo:** dar a Flito, y a futuros sistemas externos, un usuario de máquina propio con el mismo
modelo que ya opera ICT (`core-ict`, `IntegrationClient`): secreto generado por el sistema, pase de
corta vida, rotación, bloqueo y permisos. Se replica el patrón en core-api sin compartir tabla ni
código. Hay dos diferencias con ICT: el cliente no queda acotado a una compañía, y el login usa los
nombres del contrato v3 (`/external/auth/token`, pase de 30 min).
- Cliente activo con credenciales válidas obtiene pase con sus permisos; inválidas o inactivo → 401
  sin revelar cuál falló.
- Cinco fallos consecutivos bloquean 15 minutos; desbloqueo automático o por el superadministrador.
- Rotación obligatoria → 403 `secret_rotation_required` hasta rotar; el secreto anterior vale durante
  la ventana de gracia.
- El superadministrador lista, crea, edita y regenera el secreto; se muestra una sola vez.
- Un pase sin el permiso requerido, o un pase de plataforma o de ICT, recibe 403 en el prefijo externo.
- Límite de solicitudes por IP en la obtención del pase.
- Clientes `flito-dev`, `flito-qa` y `flito-pdn` dados de alta con ambos permisos y su finalidad.
HUs: (1) schema `integrations` + entidad + repositorio, calcados de `IntegrationClient`; (2) endpoint de
pase con bloqueo, rotación, límite por IP y policies por permiso; (3) endpoints de administración
para el superadministrador y alta de los clientes de Flito. La pantalla de administración queda fuera
de esta fase: se administra por API, igual que hoy los clientes ICT.

### F3 — #13066 `[INTEGRACIONES] - Endpoint de sincronización de trámites, URL de adjunto de factura y contrato OpenAPI` (8 HU)
**Objetivo:** entregar el endpoint incremental con cursor opaco, la descarga firmada del adjunto de
factura, la respuesta completa mapeada a los campos de Flito y el contrato publicado.
- Con cursor vacío se recorre el universo completo por páginas; con cursor solo los modificados
  posteriores, en orden estable y sin omisiones bajo concurrencia; `since` filtra por último cambio.
- La respuesta cumple el contrato (todas las claves presentes, nulos explícitos, tombstones).
- Se entregan todos los compradores (copropiedad, hasta 4) con orden y porcentaje; comprador tiene
  precedencia sobre propietario; sin ninguno, la lista va vacía y el ítem se entrega igual; fecha de aprobación desde la última transición a
  aprobado; tipo de servicio normalizado; borradores nunca finalizados excluidos.
- La URL firmada del adjunto de factura es válida, de corta vida y solo para adjuntos del trámite.
- Parámetros inválidos responden 400 con detalle estándar; sin resultados responde 200 vacío.
- Página máxima en p95 < 1,5 s en QA con datos representativos.
- Contrato en OpenAPI con esquema de seguridad propio, PII marcada y ejemplos ficticios; pasa el lint.
HUs: ámbito cross-tenant + repositorio SQL keyset; pivot vehículo + normalización; comprador /
aprobación / factura / compañía / organismo; endpoint + cursor + validaciones + exención de middleware
de tenant; endpoint URL firmada de adjunto; OpenAPI + guía de consumo; pruebas de integración
(Testcontainers) y de rendimiento; ruta Gateway `/api/v1/external/**` + timeout (sin ella el endpoint
no es alcanzable desde fuera en DEV).

### F4 — #13067 `[INTEGRACIONES] - Protección, auditoría y cumplimiento del acceso externo` (2 HU)
**Objetivo:** proteger la plataforma y dejar constancia de los datos personales entregados (Ley 1581)
con lo mínimo necesario para la puesta en marcha.
- Cliente que excede su cuota (120/min, configurable) recibe 429 con tiempo de espera.
- Cada solicitud queda registrada con cliente, fecha, rango de versiones, cantidad, compañías tocadas,
  resultado y si hubo datos personales en claro; retención de 12 meses.
- Ni la bitácora ni los logs contienen datos personales, secretos ni pases.
HUs: (1) límite de tasa por cliente; (2) tabla y middleware de bitácora.
**Segunda fase (fuera de esta entrega):** métricas y alertas (tasa de 429 y 30 min sin lectura
exitosa) y depuración automática de la bitácora. Mientras tanto, la vigilancia la hace el aviso en
pantalla de FLITO a los 30 min.

### F5 (opcional) — #13068 `[INTEGRACIONES] - Consulta de detalle de trámite para clientes externos` (2 HU)
**Objetivo:** permitir a Flito reconsultar un trámite puntual por identificador o radicado con el mismo
contrato de ítem.
- Por identificador o radicado se obtiene el ítem con el contrato de sincronización; inexistente → 404.
- Exige el mismo alcance y queda en la bitácora.
- Un trámite eliminado devuelve la marca de borrado.

Total aproximado: 16 HU sin F5 (18 con F5). F6 «Canal de aviso de cambios» retirada en v3.

## Anexo C — ADRs a proponer y riesgos

**ADRs (estado Propuesto):**
1. Marca de agua de sincronización por secuencia global y triggers en trámites (vs. `updated_at` con
   trigger; vs. outbox por cambio).
2. Autenticación de clientes externos en core-api con JWT propio y scopes (vs. API key estática; vs.
   reutilizar el cliente de integración de ICT).
3. Paginación keyset con cursor opaco para APIs de sincronización (vs. offset/limit; vs. `updatedSince`
   puro por timestamp).
4. Lectura cross-compañía auditada para integraciones externas (ámbito dedicado, bitácora y alcance
   PII; Ley 1581).

**Riesgos y mitigación:**
- Orden de commit ≠ orden de secuencia → ventana de estabilidad + al-menos-una-vez + prueba de
  integración con transacciones concurrentes.
- Ruido en `audit_log` y bump de `row_version` por cada cambio hijo → triggers statement-level (un
  UPDATE por operación), backfill con triggers deshabilitados, evaluar que el trigger de auditoría
  ignore cambios que solo tocan columnas de sincronización; verificar concurrencia optimista de EF en
  flujos que guardan padre e hijos (patrón ya existente para vin/plate).
- Carga sobre la base transaccional → páginas acotadas, rate limit, LATERAL por fila, índices
  parciales; plan B: tabla proyección desnormalizada.
- Fuga cross-tenant → ámbito dedicado activable solo desde el endpoint externo, cubierto por test de
  arquitectura; nunca reutilizar el ámbito de superadmin.
- Datos inconsistentes en campos de vehículo (año, cilindraje como texto) → parseo tolerante con campo
  `*Texto` de respaldo, documentado en el contrato.
- Secretos en claro en logs → detector de seguridad inline; secreto mostrado una vez; hash Argon2id.
- Gateway abre un prefijo sin JWT → ruta específica (no comodín), rate limit, y la API rechaza todo lo
  que no traiga el esquema `ExternalClient`.

## Anexo D — Acuerdos con Flito (reconciliación cross-sesión, 2026-09-21)

- Flito consume por un puerto/adaptador propio (`flit2`), guarda el ítem crudo y `sync_version`, hace
  upsert por `id`, muestra `radicado` como identificador visible, y guarda el cursor por fuente.
- Sincronización manual a demanda (sin cron), páginas de 500, carga inicial en una sola corrida.
- Auth client_credentials aceptada; Flito cachea el JWT hasta `exp − 60 s` y re-hace login ante 401.
  Necesita ambos scopes (`external.tramites.read`, `external.tramites.pii.read`).
- Pedidos de Flito incorporados: lockout temporal, cliente por ambiente, 200 vacío sin resultados,
  `since` por último cambio, todas las claves presentes con `null`, excluir borrador, tombstones,
  `tipoDocumento` canónico FLIT, `estado` en códigos internos, `organismo.departamento`, endpoint de URL
  firmada del adjunto de factura, contrato copiable a su repo sin PII real.
- Descartado a petición de Flito: `gestor`, `valorVenta`, `metodoPago`, código corto de tipo de documento.
- Flito registra este contrato como dependencia externa de su Épica **#12736** y de su Feature de
  conexión; tombstone → anulado en Flito con rastro en historial. `cilindrajeTexto` aceptado (Flito
  toma el numérico y, si es null, el crudo).

## Anexo E — Acuerdos con Flito (estrategia v2, 2026-09-22)

El Anexo D se conserva tal como quedó el 2026-09-21 para no perder el rastro de por qué se decidió cada
cosa. Estos acuerdos lo sustituyen donde se contradicen.

- **Transporte: aviso por webhook firmado**, reutilizando el mecanismo que la plataforma ya opera. Se
  descartó montar un intermediario de mensajería dedicado: no existe ninguno desplegado y añadirlo
  supondría un componente con estado propio en un servidor compartido por los tres ambientes, con su
  propia operación, sin resolver ni la carga inicial ni la reconciliación.
- **El aviso es delgado y no lleva datos personales**: identificador, versión y fecha. El detalle se
  obtiene por el servicio de sincronización, donde el enmascarado por alcance sigue siendo aplicable.
  Un aviso con el ítem completo no podría enmascararse.
- **El aviso es un acelerador, no una fuente de verdad.** La posición del consumidor la define su
  cursor, no los avisos recibidos.
- **No se ingieren los borradores.** El alcance arranca en la radicación. Esta decisión se tomó en dos
  tiempos y conviene dejar el recorrido escrito: primero se decidió incluirlos, por coherencia con
  Flito —donde los borradores de FLIT 1 sí entran— y después se revirtió al confirmarse que en Flito
  **ningún borrador dispara proceso alguno** (ni SOAT, ni impuestos, ni logística), y que los procesos
  del sistema consumidor arrancan en estados posteriores.
- **Coste asumido y declarado:** Flito tiene una alerta operativa de «más de 5 días en borrador» que
  vigila expedientes estancados. Esa alerta **seguirá cubriendo FLIT 1 y no cubrirá FLIT 2**. Se
  decidió con el coste a la vista y **no es un olvido**. Se descartó también retirarla de FLIT 1: se
  queda como está. El argumento que sostiene la decisión es que con FLIT 1 el sistema consumidor era
  la capa de operación, mientras que en FLIT 2 el gestor trabaja el expediente dentro de la propia
  plataforma; si la vigilancia del borrador se echa en falta, se pide como necesidad propia de FLIT 2
  y no reabriendo esta integración.
- **Efecto secundario favorable:** al no ingerir borradores desaparece casi entero el riesgo de dos
  registros para el mismo vehículo (borrador que reserva el vehículo, se abandona, se anula, libera la
  llave y nace otro trámite con otro identificador). Queda solo el caso estrecho de un trámite ya
  radicado que se anula o se revoca, que ya ocurre hoy con el sistema anterior.
- **Una vez dentro, no sale nunca.** Un trámite entregado que retrocede a borrador sigue
  entregándose. La alternativa —dejar de entregarlo— lo habría congelado en el consumidor en un estado
  que este considera terminado, mientras en realidad estaba siendo corregido, y sin ninguna señal que
  lo delatara.
- **Sin carga inicial del histórico.** Flito arranca desde el presente. Decisión reversible en
  cualquier momento con una sola corrida desde cursor vacío.
- **Latencia objetivo: minutos**, no segundos. No se afina por debajo de ~5 minutos.
- La factura sigue viajando por URL firmada. Los binarios nunca van por el canal de avisos.
- Secciones 2 (autenticación), 3 (sincronización, salvo la regla de borradores) y 4 (ítem) del contrato
  de cable quedan sin cambios funcionales: las fechas comprometidas se sostienen.

## Anexo F — Acuerdos con Flito (estrategia v3, 2026-09-29)

Sustituye al Anexo E donde se contradicen; el Anexo E se conserva por el rastro de decisiones.

- **Se descarta el aviso de cambios.** A petición del PO, la integración vuelve a un esquema de
  consulta como el de la primera versión de FLIT, pero conservando lo que la mejora: autenticación con
  alcances, cursor paginado, marcas de borrado, datos personales enmascarados por alcance, bitácora y
  factura por URL firmada. Se retira la Feature F6 y el requisito de conectividad hacia Flito.
- **Disparo en Flito:** corrida programada cada **5 min** más botón a demanda, ambos por el mismo
  cursor y sin solaparse. Ante un 429, Flito respeta el tiempo de espera indicado.
- **Sin carga inicial del histórico:** se mantiene. La primera corrida la fija el sistema con la hora
  real del arranque; el usuario no elige fecha.
- **Borradores y preparados sin radicar: no entran.** Se mantiene el alcance del Anexo E.
- **Autenticación sin cambios:** canje de identificador y secreto del cliente por un pase de 30 min.
  Se descartó adoptar el formulario estándar OAuth2.
- **Alerta de cliente sin sincronizar:** 30 min sin lectura exitosa, en lugar de 24 h.
- **Cursor y fecha de inicio juntos → 400.** Sin ETag. Páginas de 500 en Flito. F5 sigue opcional.
- **Copropiedad:** el comprador pasa a ser una lista `compradores` con orden y porcentaje de
  participación (hasta 4, ADR-0053). Nunca nula; puede llegar vacía en un trámite radicado (retroceso,
  subsanación o tipos sin comprador) y el consumidor la guarda sin rechazar el trámite.
- **Trámites migrados de FLIT 1: fuera del feed** (decisión del PO, 2026-09-29). Ya llegan a Flito por
  FLIT 1; entregarlos por FLIT 2 duplicaría el trámite con otro radicado.
- **Mejora no requerida:** sellar en la asignación inicial la fecha real de último cambio de cada
  trámite, para que una fecha de inicio anterior a la migración signifique lo que dice.
