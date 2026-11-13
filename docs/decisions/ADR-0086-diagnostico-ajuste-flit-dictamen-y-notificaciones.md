# ADR-0086: Diagnóstico — el ajuste FLIT del dictamen es una versión nueva con versión de fila en el tablero, y la campana usa notificaciones persistentes por usuario consultadas por sondeo

**Fecha**: 2026-11-12
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT. La aceptación es exclusiva del Líder Técnico humano, en su propio PR (regla FLIT 15); este ADR no pasa a Aceptado por ningún otro camino. Se apoya en las decisiones de producto del PO del 2026-11-12 que se citan en línea en «Decisiones de producto que este ADR asume».
**Tags**: arquitectura, backend, frontend, persistencia, polling, auditoría, Habeas Data, diagnostico
**Épica / Feature / HU**: proyecto FLIT - DIAGNOSTICO en Azure DevOps. Épica #12638 / Feature #14561 «[DIAGNOSTICO] - Rediseño UI Kit 2026 de Diagnóstico Vehicular (14 pantallas) con ajustes de backend de soporte» / HU [FRONTEND] #14562 y HU [BACKEND] #14563. Este ADR cubre los bloques B1, B7 y B8 de la HU #14563 y su consumo en la HU #14562.
**Supersedes**: ninguno.
**Enmienda**:
- ADR-0072 (Propuesto): añade las «Excepciones por tabla» de `diagnostico.notificaciones` (sección «Excepciones a ADR-0072» de este ADR) y la columna de versión de fila en `vehiculos`.
- ADR-0077 (Propuesto): añade `redactado_por = 'MANUAL'` y las columnas y CHECK de ajuste en `dictamenes`.

**Diseño detallado**: adjunto de implementación de la HU [BACKEND] #14563 (bloques B1, B7 y B8, y sus diagramas de secuencia) en Azure DevOps.
**Registro de cambios**:
- 2026-11-12: versión inicial. Propuesto.
- 2026-11-13: alineado con el código de la HU #14563 (AC41, AC46). Se añaden la sección «Contrato y migraciones» con las rutas y los archivos reales; el PUT ya no acepta el cuerpo legado sin motivo (B-PR6, retirado junto con el front en la misma oleada); `version_esperada` solo es obligatoria con alertamientos; el filtro de 6 o más dígitos aplica al texto de los alertamientos `AJUSTE_FLIT` y no al motivo (P3 = b); el GET de versiones exige `diagnostico.auditoria.ver`; la purga suma la opción `TamanoLote`. Sigue en Propuesto. (Corregido por la entrada siguiente: el GET de versiones exige `diagnostico.auditoria.ver` + `RequiereOperacionFlit`.)
- 2026-11-13 (code review de la oleada, M-2 y menores 2, 3, 4, 5, 6, 7, 8, 10 y 11): el GET de versiones exige `diagnostico.auditoria.ver` + `RequiereOperacionFlit` (revisión de seguridad M3: un SuperAdmin de otra compañía no lee `ajuste_motivo`). `version_esperada` pasa a ser obligatoria en todo PUT, también en el de solo veredicto, porque B-PR6 retiró el cuerpo legado y el front la envía siempre: no queda el caso «gana el último». `confirmado` es opcional y, si viene en false, responde 422. Las claves desconocidas responden 422 `VALOR_INVALIDO`. El texto de los alertamientos se mide en puntos de código. Notificaciones: la deduplicación dentro de la unidad de trabajo usa la misma clave del índice (salvo `LOTE_TERMINADO`, que sigue por lote), y el reconciliador trata un 23505 en `ux_notificaciones_idempotencia` como reintento. El reconciliador recorre los ítems por lote y por id. El ETag de la bandeja suma el hash de los nombres de los lotes. `ix_vehiculos_tablero` se crea sin `CONCURRENTLY` («Cambios operacionales»). Sigue en Propuesto.

## Decisiones de producto que este ADR asume

Decisiones del PO del 2026-11-12. Este ADR no las reabre.

- **D1 = a**: el detalle de consulta mantiene los datos completos y la respuesta cruda del proveedor (decisión del 2026-10-27, HU #14087 AC8). El rediseño solo cambia la presentación.
- **D2 = c**: el UI Kit 2026 aplica solo a Diagnóstico. La campana es un componente de Diagnóstico montado en `DiagnosticoShell`, no en `SuiteShell`; no se tocan `@flit/ui` ni `@flit/shell` para lo propio.
- **D3 = c**: entran todos los opcionales de backend, incluidos B7 (ajuste de alertamientos con auditoría) y B8 (campana con notificaciones en servidor).
- **D6 = a**: el cliente ve el dictamen corregido con la marca «ajustado por FLIT» y la fecha. El dictamen original y quién hizo cada cambio quedan en una auditoría visible solo en backoffice.
- **D7 = a**: si después de un ajuste manual hay un recálculo automático del dictamen, gana el recálculo; la marca «ajustado por FLIT» desaparece y el ajuste queda en la auditoría.
- **D8 = b**: el ajuste FLIT del dictamen no se notifica al cliente; el cliente solo ve la marca al entrar al lote. B8 queda con ZIP listo o fallido, derivación terminada y lote terminado.
- **D9 = a**: el motivo es obligatorio en todo ajuste nuevo, de veredicto o de alertamientos. Durante la transición, el cuerpo `{veredicto, confirmado}` sin motivo se acepta solo si no cambia el veredicto, y se retira cuando la pantalla nueva del backoffice (F-09) esté en DEV.
- **O2** (objeción del PO resuelta): fuera del detalle de consulta, el enmascarado sigue como está hoy; el panel del dictamen y la campana no muestran documentos.

## Contexto

El rediseño del UI Kit 2026 (Feature #14561, criterios CF-13, CF-14, CF-24 y CF-27) introduce tres necesidades de backend que sientan precedente:

1. **El tablero debe ver los cambios de dictamen sin recargar.** La grilla es incremental por versión (`?desde=`), pero solo compara la versión **de la celda**: `Modulos/Tablero/ConsultaGrilla.cs:125`. La emisión automática viaja porque va en la transacción del cierre de la última celda (`Modulos/Dictamen/EmisorDictamen.cs:328-357`, `EmitirAsync`). En cambio, el override de backoffice (`Modulos/Backoffice/OverrideDictamenEndpoints.cs:55-80`) y el cierre de una derivación (`Modulos/Derivacion/CierreItemDerivacion.cs:59`) cambian la fila sin tocar ninguna celda, así que el sondeo nunca los ve. El front lo tapa a medias con `recargar()` (`frontend-diagnostico/lib/tablero/useGrillaIncremental.ts:29-33`) y, además, deja de sondear cuando el lote termina (mismo archivo, líneas 3-6).
2. **Soporte FLIT debe poder agregar, editar y eliminar alertamientos (B7)**, no solo cambiar el veredicto. Por D6, la compañía ve el dictamen corregido con la marca «ajustado por FLIT» y la fecha; el original y la autoría quedan solo en una auditoría de backoffice. El dictamen ya es de solo agregación con versión (`version`, `vigente`, `analisis_override`, `usuario_override_id`, `override_en`; ADR-0077). Hoy el override exige que el veredicto cambie (`VEREDICTO_SIN_CAMBIO`) y devuelve los textos a la plantilla (`EmisorDictamen.cs:255-288`, `FilaDeOverride`).
3. **Campana con notificaciones persistentes en servidor (B8)**, montada en `DiagnosticoShell` y no en `SuiteShell` (D2). No hay SSE ni SignalR (fuera de alcance del Feature). `core-notificaciones` solo hace correo y webhooks. Las notificaciones no deben llevar documentos ni PII (riesgo R7 del Feature y O2).

Restricciones:
- ADR-0072: clase (a) se versiona y no se borra; clase (b) es de solo inserción; R-20 dice que el DTO de compañía nunca lleva el origen manual, el usuario ni el payload.
- ADR-0076: respuesta cruda cifrada.
- ADR-0084: derivación con revalidación en el consumidor.

## Decisión

**D-1 (versión de fila).** `diagnostico.vehiculos` gana `version_tablero`. Todo escritor que cambie un hecho de la fila (dictamen nuevo, ajuste FLIT, trámite creado, omitido o fallido) llama a un único `EscritorCeldas.SubirVersionFilaAsync`. Esa función sube `diagnosticos.version_tablero` y la copia en el vehículo. La grilla devuelve los vehículos con celdas **o** fila con versión mayor que `desde`. Los conteos del lote (`bloqueados`, `listos_tramite`, `con_error`, `con_deuda`) viajan siempre, calculados con todo el lote. El front mantiene un sondeo de reposo cada 30 s, con la pestaña visible, aunque no queden pendientes.

**D-2 (versionado del dictamen ajustado).** Cada ajuste FLIT es una **versión nueva** de `dictamenes`, siguiendo el mecanismo existente: se apaga la vigente y se inserta N+1. La versión nueva lleva:
- `analisis_override = true`, el autor y la fecha.
- Columnas nuevas: `ajuste_motivo` (`@pii:low`: texto libre digitado por soporte, la base no garantiza que no traiga PII; solo backoffice) y `ajustado_desde_version` (= `version - 1`).
- `redactado_por = 'MANUAL'`: el LLM no vuelve a redactar textos editados a mano.
- Una marca `ajuste: AGREGADO | EDITADO` en cada alertamiento. Los eliminados se calculan contra la versión anterior.

Reglas del PUT:
- **Estado completo deseado.** Sin alertamientos, el PUT solo reemplaza el veredicto (los textos vuelven a la plantilla y la cola los redacta); con alertamientos, nace una versión `MANUAL`/`COMPLETADA`.
- `version_esperada` es **obligatoria en todo PUT**, también en el de solo veredicto: si falta o es null, 422 `VERSION_ESPERADA_INVALIDA`; si no coincide con la vigente, 409 `DICTAMEN_DESACTUALIZADO`. Antes de B-PR6 solo se exigía con alertamientos, para no romper el cuerpo legado; al retirarse este, el front (`cuerpoAjusteDictamen`, `frontend-diagnostico/lib/backoffice/ajusteDictamen.ts`) la envía siempre. Por eso no hay un caso «gana el último»: dos ajustes que leyeron la misma versión no se pisan, el segundo recibe 409.
- `confirmado` es **opcional** (el front ya no lo envía). Si viene, debe ser `true`; con `false` responde 422 `CONFIRMADO_REQUERIDO`.
- **Claves desconocidas**: coherente con `additionalProperties: false` del contrato, una clave que no está en `CuerpoOverrideDictamen` o en `AlertamientoAjuste` responde 422 `VALOR_INVALIDO` con `campo` señalado. El nombre de la clave solo se repite si parece un nombre de campo; si no, `campo` es el objeto que la trae, para no devolver lo digitado.
- Coherencia: `VIABLE` sin ningún alertamiento `BLOQUEA`; `BLOQUEADO` con al menos uno. Si no se cumple, 422.
- Si el vehículo ya tiene trámite, exige `confirmar_con_tramite` (409 si falta). El trámite no se revierte.
- **Motivo obligatorio** (10..500) en todo ajuste nuevo, de veredicto o de alertamientos (D9).
- Transición (D9): estaba previsto aceptar el cuerpo legado `{veredicto, confirmado}` sin motivo solo si no cambiaba el veredicto, hasta que la pantalla nueva del backoffice (F-09) llegara a DEV. **Ya se retiró (B-PR6)**: todo PUT sin motivo válido responde 422 `MOTIVO_REQUERIDO`. El front de la HU #14562 (F-09, F-PR14) dejó de enviar el cuerpo legado en la misma oleada, así que ambos llegan juntos a DEV.
- Texto de los alertamientos: código del catálogo, `AJUSTE_FLIT` o uno que ya estaba en la vigente, texto de 1 a 500, efecto `BLOQUEA`/`ADVIERTE` y fuente conocida; si no, 422 `ALERTAMIENTO_INVALIDO`. El largo del texto (y el del motivo) se cuenta en puntos de código, como `char_length` en PostgreSQL, no en unidades UTF-16. Un `AJUSTE_FLIT` (lo ve la compañía) no admite 6 o más dígitos seguidos. El motivo no lleva filtro de dígitos (P3 = b), porque nunca sale del backoffice.

Qué ve cada uno:
- **Compañía:** solo `ajustado_por_flit` y `ajustado_en`.
- **Backoffice:** `GET …/dictamen/versiones` (permiso `diagnostico.auditoria.ver` + política de operación FLIT, `RequiereOperacionFlit`: un SuperAdmin de otra compañía recibe 403), auditado en `lecturas_backoffice`. Es la única salida de `ajuste_motivo`.

Permisos: escritura solo FLIT (política `diagnostico.backoffice.operacion` + permiso `diagnostico.dictamen.override`).

**Política de recálculo (D7 = a):** un dictamen automático posterior (reproceso, llenado manual) **gana**; la marca «ajustado por FLIT» desaparece por construcción (la versión automática nace sin override) y el ajuste queda en la auditoría de backoffice como «superado por recálculo» (derivado al leer, sin columna).

**Integridad en la base (R7_AjusteDictamen).** Se reemplazan `ck_dictamenes_redactado_por` (admite `MANUAL`) y `ck_redaccion` (`COMPLETADA` si `LLM` o `MANUAL`), y se añaden `ck_dictamenes_version`, `ck_dictamenes_override`, `ck_dictamenes_ajuste`, `ck_dictamenes_ajuste_motivo` y `ck_dictamenes_manual`. Las filas de override que ya existen reciben `ajustado_desde_version = version - 1` en la misma migración (backfill exacto, porque toda versión con override sale de la vigente anterior). El `Down` se niega a correr si ya hay ajustes FLIT, para no borrar auditoría.

**D-3 (notificaciones).** Tabla nueva `diagnostico.notificaciones` por `(tenant_id, usuario_id)`:
- Se escribe **en la transacción del hecho** (ZIP listo o fallido, derivación terminada, lote terminado), idempotente por `(tenant_id, usuario_id, tipo, referencia_id)`. El único escritor (`EscritorNotificaciones`) no agrega la fila si ya existe en la base o si la unidad de trabajo ya tiene una con **esa misma clave**; comparar solo el lote descartaba avisos legítimos, como dos derivaciones masivas del mismo lote que terminan juntas. La excepción es `LOTE_TERMINADO`, que se compara por lote: su referencia es la fila de historial nueva de cada recálculo, y dos recálculos de la misma transición en una unidad de trabajo son un solo hecho. El índice único `ux_notificaciones_idempotencia` queda de respaldo para la carrera entre la comprobación y el INSERT.
- **Choque en `ux_notificaciones_idempotencia` (23505)**: lo provoca otro proceso que confirma la misma notificación en esa ventana, y se reproduce con una prueba real (`ConsumidorDerivacionTests.HU14563_menor7_…`). El reconciliador de la derivación lo trata como un reintento: revierte la transacción de esa empresa y la deja para la siguiente vuelta, igual que un conflicto de `xmin`. En la vuelta siguiente el escritor ya ve la fila y no duplica. En el consumidor la excepción sale a la SDK, que reentrega el mensaje. En el endpoint masivo no puede ocurrir, porque la operación nace en la misma transacción. El reconciliador recorre los ítems por lote y por id, así que el orden de bloqueo (cabecera → fila) es determinista.
- El ETag de la bandeja suma un hash de los nombres de los lotes de la página, porque el mensaje lleva ese nombre: si se renombra un lote, el `If-None-Match` anterior deja de dar 304.
- **D8 = b: el ajuste FLIT del dictamen no se notifica**; el cliente solo ve la marca al entrar al lote. No existe un tipo de notificación para el ajuste.
- Se consulta por **sondeo de 30 s con ETag/304** y se marca leída con POST.
- Se **purga físicamente a los 90 días** (excepción a R-18, ver «Excepciones a ADR-0072»).

**Decisión registrada:** la fila **no guarda texto ni PII**, solo `tipo`, ids y contadores en `datos`, con un CHECK de forma que impide texto libre. El título y el mensaje se arman al leer, con rótulos fijos y el nombre del lote obtenido por join, que respeta la visibilidad `PROPIOS`. Nunca hay placa, VIN, documento, nombre de persona ni archivo. La descarga vuelve a exigir `diagnostico.reportes.descargar`. La campana es `CampanaDiagnostico` y se monta por la ranura `headerActions` de `SuiteShell` desde `DiagnosticoShell`, sin tocar `@flit/shell`.

## Contrato y migraciones

Rutas reales (prefijo `/api/v1/diagnostico`, contrato `contracts/openapi/diagnostico.v1.yaml`, `info.version` 1.3.0). Las del backoffice cuelgan del grupo `/backoffice` con la empresa explícita en `?empresa=`.

| Método y ruta | Bloque | Permiso | Archivo |
|---------------|--------|---------|---------|
| `PUT /backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen` (ajuste FLIT) | B7 | política `diagnostico.backoffice.operacion` + `diagnostico.dictamen.override` | `Modulos/Backoffice/OverrideDictamenEndpoints.cs` |
| `GET /backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen/versiones` | B7 | `diagnostico.auditoria.ver` + `RequiereOperacionFlit` (política `diagnostico.backoffice.operacion`) | `Modulos/Backoffice/VersionesDictamenEndpoints.cs` |
| `GET /notificaciones?limite=20&antes={id}` (ETag débil con el hash de los nombres de los lotes de la página; con `If-None-Match` igual, 304 sin cuerpo) | B8 | `diagnostico.diagnosticos.ver` | `Modulos/Notificaciones/NotificacionesEndpoints.cs` |
| `POST /notificaciones/{nid}/leida` (204, idempotente; 404 si no es del usuario o su lote ya no es visible) | B8 | `diagnostico.diagnosticos.ver` | `Modulos/Notificaciones/NotificacionesEndpoints.cs` |
| `POST /notificaciones/leer-todas` (204) | B8 | `diagnostico.diagnosticos.ver` | `Modulos/Notificaciones/NotificacionesEndpoints.cs` |

La compañía lee el dictamen en `GET /diagnosticos/{id}/vehiculos/{vid}/dictamen` (`Modulos/Dictamen/DictamenEndpoints.cs`), que solo agrega `ajustado_por_flit` y `ajustado_en`.

Migraciones en `services/core-diagnostico/src/Flit.Diagnostico.Api/Persistence/Migrations/`, en el orden en que se aplican:

1. `20261113050619_R7_AjusteDictamen.cs`: columnas `ajustado_desde_version` y `ajuste_motivo`, backfill y CHECK de D-2; `Down` bloqueado si hay ajustes FLIT.
2. `20261113051720_R8_Notificaciones.cs`: tabla `notificaciones` con `fk_notificaciones_diagnosticos` (`CASCADE`), `ix_notificaciones_bandeja`, `ix_notificaciones_no_leidas`, `ix_notificaciones_diagnostico`, `ix_notificaciones_retencion` y `ux_notificaciones_idempotencia`, y CHECK `ck_notificaciones_tipo`, `ck_notificaciones_datos` y `ck_notificaciones_leida`.
3. `20261113054446_R1_VersionFilaTablero.cs`: `vehiculos.version_tablero`, `ck_vehiculos_version_tablero` e `ix_vehiculos_tablero`.
4. `20261113070402_R9_AjusteConTramite.cs`: `dictamenes.ajuste_con_tramite` (boolean, default false) y `ck_dictamenes_ajuste_con_tramite`. Registra que el vehículo ya tenía trámite cuando se confirmó el ajuste (AC23). Solo se expone en `GET …/dictamen/versiones`. El Down se bloquea si hay versiones marcadas.

La marca de tiempo de R1 es posterior a las de R7 y R8, así que se aplica al final. El rótulo R1 viene del bloque B1 de la HU, no del orden de aplicación.

## Excepciones a ADR-0072 (alcance: solo `diagnostico.notificaciones`)

Este ADR añade a «Excepciones por tabla» de ADR-0072 las siguientes, **acotadas a la tabla `diagnostico.notificaciones`**. Ninguna se extiende a otra tabla del schema; una tabla nueva que quiera lo mismo necesita su propio ADR.

1. **Purga física a los 90 días frente a R-18 («sin baja, retención ni purga»).** Es la primera del schema fuera del outbox del SDK. Justificación: la fila no es historia de negocio (la historia vive en `evidencias`, `derivacion_operaciones` e `historial_estado_diagnostico`) y no guarda datos personales salvo el seudónimo `usuario_id`. No reduce ni extiende la conservación de PII de la sección Ley 1581 de ADR-0072. R-18 sigue vigente para el resto del schema.
2. **`ON DELETE CASCADE` hacia `diagnosticos`**: hija de composición pura sin valor de auditoría, admitida por (c). Inerte en la práctica, porque `diagnosticos` no se borra.
3. **`referencia_id` sin FK ((d)5 / A9)**: es una referencia polimórfica según `tipo` (evidencia, operación de derivación o transición de historial). Alternativa descartada: tres FK tipadas nulas con un CHECK por tipo y tres índices más, en una tabla efímera. La integridad la garantiza la escritura en la transacción del hecho; una referencia huérfana solo produce una notificación que el join de lectura no muestra.
4. **`ix_notificaciones_retencion (creada_en)` no empieza por `tenant_id` (A11)**: lo usa exclusivamente la purga, que recorre todas las compañías. Los índices de lectura (`ix_notificaciones_bandeja`, `ux_notificaciones_idempotencia`) sí empiezan por `tenant_id`.
5. **Quinta lectura y escritura entre empresas** (`Modulos/Notificaciones/TrabajadorPurgaNotificaciones.cs`, clases `PurgaNotificaciones` y `TrabajadorPurgaNotificaciones`), en la lista blanca de `TomaEntreEmpresasSoloEnColaTests` junto a `ColaConsultas`, `ColaEvidencias`, `ColaRedaccion` y `ColaDerivacion`: SQL constante y parametrizado, sin filas devueltas.

La tabla de aplicabilidad de ADR-0072 ya tiene las filas de R1 (`vehiculos`), R7 (`dictamenes`) y R8 (`notificaciones`), y su sección «Excepciones por tabla» recoge estas cinco excepciones.

## Alternativas consideradas

### Decisión 1 — Cómo llegan al sondeo los cambios de fila

#### Opción 1A: versión de fila en `vehiculos` (elegida)

**Pros:**
- Semántica separada: la versión de la celda dice «cambió la celda» y la de la fila dice «cambió la fila».
- Un solo mecanismo cubre el dictamen **y** la derivación, así que `recargar()` deja de hacer falta.
- Reutiliza el `ExecuteUpdate` atómico de `SubirVersionTableroAsync` (`Modulos/Consultas/EscritorCeldas.cs:54`).
- Permite invalidar la huella del ZIP cuando cambia el dictamen.

**Cons:**
- Migración: 1 columna, 1 CHECK y 1 índice.
- Un UPDATE más en el override y en el cierre de derivación (con `xmin` del vehículo de por medio).
- Riesgo de olvidar un escritor.

**Esfuerzo:** M
**Riesgos:** un escritor nuevo de fila que no suba la versión. Mitigación: prueba de arquitectura que enumere los escritores de `DictamenVehiculo` y de `VehiculoDiagnostico.TramiteId`.

#### Opción 1B: versión del tablero en cada fila de `dictamenes`

**Pros:**
- Solo toca la tabla del dictamen, sin UPDATE del vehículo.

**Cons:**
- No cubre la derivación, así que el hueco sigue y `recargar()` se queda.
- Añade un join por sondeo.

**Esfuerzo:** M
**Riesgos:** dos mecanismos de frescura conviviendo.

#### Opción 1C: «tocar» una celda ancla (RUNT) sin DDL

**Pros:**
- Esfuerzo S, sin migración.

**Cons:**
- Falsea el significado de la versión de celda.
- Altera `HuellaEvidencia` de una celda que no cambió.
- Es deuda difícil de explicar y de probar.

**Esfuerzo:** S
**Riesgos:** regresiones silenciosas en evidencias y en las pruebas de reproceso.

### Decisión 2 — Versionado del dictamen ajustado por FLIT

#### Opción 2A: versión nueva por ajuste, la auditoría es la cadena de versiones (elegida)

**Pros:**
- Es el modelo que ya existe (`SiguienteVersion`, `ux_dictamen_vigente`, `ux_dictamen_version`, `analisis_override`).
- El original nunca se pierde.
- Todos los lectores (dictamen, PDF, Excel, ZIP, elegibilidad) siguen leyendo una sola fila `vigente`.
- No crea tabla nueva.

**Cons:**
- Un recálculo automático posterior descarta el ajuste. Es una política explícita, pero puede sorprender a soporte.
- Los eliminados se calculan por diferencia entre versiones.

**Esfuerzo:** M
**Riesgos:** pérdida percibida del ajuste. Mitigación: la auditoría de backoffice lo marca como «superado por recálculo» (D7). No hay notificación al cliente (D8).

#### Opción 2B: capa de ajustes que se compone al leer

**Pros:**
- El ajuste sobrevive a los recálculos.

**Cons:**
- La composición se reparte en 5 lectores.
- Un ajuste puede aplicarse sobre hechos nuevos que lo contradicen.
- Es más difícil de auditar y de probar.

**Esfuerzo:** L
**Riesgos:** dictamen incoherente con las fuentes.

#### Opción 2C: edición en sitio de la vigente + historial de diffs (clase b)

**Pros:**
- Menos filas en `dictamenes`.

**Cons:**
- Contradice el principio de ADR-0072/0077 («se versiona, no se edita»).
- El original deja de ser un dato y hay que reconstruirlo desde los diffs.

**Esfuerzo:** M
**Riesgos:** auditoría reconstruida y frágil.

### Decisión 3 — Notificaciones de la campana

#### Opción 3A: tabla por usuario escrita en la transacción del hecho + sondeo (elegida)

**Pros:**
- Transaccional e idempotente.
- Estado de leída por ítem.
- Extensible sin consultas nuevas.
- Retención simple.
- Cero dependencias nuevas.
- El patrón coincide con el outbox existente (`LoteTerminado.EncolarAsync`, llamado desde `Modulos/Tablero/RecalculadorEstado.cs:103`).

**Cons:**
- Escribe en 3 o 4 caminos de negocio.
- Necesita una excepción de retención a ADR-0072 («sin borrado»).
- Un trabajo de purga entre compañías.

**Esfuerzo:** M
**Riesgos:** que la purga toque datos de otra compañía por error. Mitigación: SQL crudo parametrizado y la lista blanca de `TomaEntreEmpresasSoloEnColaTests`, igual que el reconciliador de ADR-0084.

#### Opción 3B: notificaciones derivadas del estado + marca «visto hasta»

**Pros:**
- No duplica hechos.
- No escribe en caminos calientes.
- La retención sale sola.

**Cons:**
- Cada evento nuevo es una consulta nueva en el sondeo.
- El estado leído o descartado por ítem necesita tabla de todos modos.
- El orden y la visibilidad se arman cruzando 3 tablas.

**Esfuerzo:** M
**Riesgos:** el coste del sondeo crece con cada tipo de evento.

#### Opción 3C: canal in-app en `core-notificaciones` (campana de suite)

**Pros:**
- Una sola bandeja para toda la suite a futuro.

**Cons:**
- Contradice D2: la campana es de Diagnóstico y `SuiteShell` está fuera de alcance.
- Suma otro servicio, otra BD, contrato de bus y latencia.

**Esfuerzo:** L
**Riesgos:** alcance transversal sin decisión global.

## Tradeoff aceptado

- **D-1.** Se paga una migración y un UPDATE por cambio de fila a cambio de una regla única y verificable («quien escribe dictamen o trámite, sube la fila»). Esa regla cierra también el hueco de la derivación. Se descarta 1C porque ahorra la migración a costa de corromper el significado de la versión de celda, del que dependen las evidencias.
- **D-2.** Se acepta que un recálculo automático descarte el ajuste (D7 = a, decisión del PO) porque los hechos nuevos deben prevalecer sobre una corrección hecha con hechos viejos, y porque mantiene **un solo lector** (`vigente`) para cinco consumidores. 2B quedaría como evolución si soporte reporta pérdida de ajustes.
- **D-3.** Se aceptan escrituras en los caminos del ZIP, la derivación y el cierre de lote, más una excepción de retención, a cambio de notificaciones transaccionales y sin PII que no dependen de otro servicio. Si la suite decide una campana global, las filas se migran publicándolas (3C) sin cambiar el contrato del front.

## Consecuencias

### Lo que se gana
- El tablero refleja override, ajuste y derivación en ≤ 30 s sin recargar (CF-14). Se acaba el N+1 de los dictámenes (CF-13).
- Soporte corrige dictámenes con trazabilidad completa. La compañía sabe que FLIT intervino (D6), sin ver autor ni motivo.
- Avisos persistentes de tareas largas sin SSE ni SignalR (CF-27), sin documentos ni PII.

### Lo que se pierde
- Un ajuste FLIT no sobrevive a un recálculo automático (política explícita, D7).
- Hay un sondeo extra de 30 s por pestaña visible (barato con 304).
- `notificaciones` es la primera tabla de Diagnóstico con borrado físico.

### Efectos en reportes
- La marca «ajustado por FLIT» (con su fecha) se imprime también en el **PDF por vehículo** y en el **índice del ZIP**, y **entra en la huella del PDF** (`Modulos/Reportes/ContenidoReporteVehiculo.cs`, `ParaHuella`), para que un PDF anterior al ajuste no se reutilice. El PDF ya imprime el dictamen (`Modulos/Reportes/PdfVehiculo.cs:144-145`). El ZIP se regenera solo, porque su huella ya incluye la versión y el veredicto del dictamen vigente (`Modulos/Reportes/EmpaquetadorZip.cs:68-81`).

### Cambios operacionales
- Migraciones `R7_AjusteDictamen`, `R8_Notificaciones`, `R1_VersionFilaTablero` y `R9_AjusteConTramite` (archivos y orden en «Contrato y migraciones»).
- `ix_vehiculos_tablero` (R1) se crea **sin `CONCURRENTLY`**. EF corre cada migración en una transacción y `CREATE INDEX CONCURRENTLY` no puede ir dentro de una. Para usarlo habría que partir R1 y marcar el SQL con `suppressTransaction`, y eso le quita la atomicidad a la migración. El repo no tiene ese patrón: `core-api` (`20260908140000_HU12193_IndicesFuncionalesPlaca.cs`) también lo descarta por la misma razón. Además, el `ADD COLUMN` de R1 ya toma un `ACCESS EXCLUSIVE` breve sobre `vehiculos`, así que `CONCURRENTLY` solo ahorraría el bloqueo de escritura mientras se construye el índice. Con el volumen de `diagnostico.vehiculos` eso dura segundos. Mitigación: aplicar R1 en la ventana de despliegue, con los trabajadores de consultas detenidos. Si la tabla crece al orden de millones de filas, el índice se crea a mano con `CONCURRENTLY` antes de desplegar y la migración se reescribe con `IF NOT EXISTS` en una HU propia.
- Rollback: el `Down` de R7 y el de R9 se niegan a correr si ya hay auditoría (ajustes FLIT o ajustes con trámite). EF deshace cada migración en su propia transacción, así que un rollback que se detiene en R7 deja deshechas R9, R1 y R8 (probado en `EsquemaR7Tests`).
- Opciones `Diagnostico:Notificaciones:{RetencionDias=90, IntervaloPurga=24 h, TamanoLote=500}` (`Modulos/Notificaciones/OpcionesNotificaciones.cs`); la purga borra en bloques de `TamanoLote`.
- `info.version` del OpenAPI de Diagnóstico pasa a 1.3.0: B-PR6 vuelve `motivo` obligatorio en el cuerpo del PUT del backoffice, y el consumidor sale en el mismo despliegue.
- El evento `diagnostico.dictamen.emitido` gana `ajuste_alertamientos` (opcional, aditivo).

## ADRs relacionados

- [ADR-0072] — Aislamiento por tenant y clases (a)/(b). Este ADR añade cinco excepciones acotadas a `notificaciones` y la versión de fila.
- [ADR-0076] — Respuesta completa con PII cifrada. D1 = a se mantiene; las notificaciones no la extienden.
- [ADR-0077] — Dictamen por reglas. Se reutiliza el versionado y se añade `MANUAL` y el ajuste.
- [ADR-0084] — Derivación encolada. La revalidación en el consumidor absorbe el cambio de veredicto; el cierre del ítem sube la versión de fila.
- [ADR-0075] — Consultas por RabbitMQ (no cambia).

## Notas para agentes

- **Database Agent**: R1, R7 y R8 según `.claude/skills/db-schema-validator/checklist-validacion-schema.md` §A + ADR-0072.
  - R1: `vehiculos.version_tablero bigint NOT NULL DEFAULT 0`, sin backfill, con `ck_vehiculos_version_tablero (version_tablero >= 0)` e `ix_vehiculos_tablero (tenant_id, diagnostico_id, version_tablero)`.
  - R7: el cambio de CHECK va con DROP y ADD en la misma transacción. Precondición que falla con mensaje claro si hay overrides incoherentes, backfill de `ajustado_desde_version` entre las columnas y los CHECK, y guarda en el `Down`.
  - R8: `notificaciones` va con FK compuesta y `ON DELETE CASCADE`, `referencia_id` sin FK e `ix_notificaciones_retencion` sin `tenant_id` al frente, citando las excepciones 1 a 5 de este ADR.
  - `ajuste_motivo` con `@pii:low` en el COMMENT.
- **Backend Agent**:
  - `SubirVersionFilaAsync` con una prueba de arquitectura de escritores.
  - Los conteos se calculan con todo el lote, también en el delta.
  - PUT del dictamen: motivo obligatorio en todo PUT (D9); el cuerpo legado sin motivo ya se retiró (B-PR6).
  - Mapear `DbUpdateConcurrencyException` y el choque en `ux_dictamen_version`/`ux_dictamen_vigente` a 409, nunca a 500.
  - Marca «ajustado por FLIT» en PDF, índice del ZIP y huella del PDF.
  - INSERT de la notificación en la transacción del hecho.
  - La purga va en la lista blanca entre compañías.
  - Escenario de fuga por endpoint nuevo.
- **Frontend Agent**:
  - Sondeo de reposo de 30 s, pausado con la pestaña oculta.
  - Fusionar los campos de fila y los conteos.
  - Retirar `recargar()` tras derivar.
  - `CampanaDiagnostico` por `headerActions` sin tocar `@flit/shell`.
  - La campana y el panel del dictamen no muestran documentos.
- **QA Agent**:
  - Override sobre un lote terminado visible en ≤ 30 s.
  - 409/422 de B7.
  - Ítem de derivación en vuelo → `OMITIDO` si queda `BLOQUEADO`.
  - Idempotencia y aislamiento de notificaciones.
  - Purga con reloj simulado.
- **Security Agent**:
  - R-20 en `ConteosGrilla`, `LoteGrilla`, `DictamenVehiculo.ajustado_*` y `Notificacion`.
  - `motivo` y autor nunca llegan a la compañía.
  - `ajuste_motivo` es `@pii:low` (texto libre, sin filtro de dígitos por P3 = b, solo backoffice). El filtro de 6 o más dígitos seguidos aplica al texto de los alertamientos `AJUSTE_FLIT`, que sí ve la compañía. Validar el CHECK de `datos` de `notificaciones`.
  - Notificaciones sin PII (decisión registrada aquí).
- **Infra Agent**: sin servicios ni colas nuevas. Solo variables opcionales de retención.

## Referencias externas

- RFC 9110 §13.1.2 (`If-None-Match`/ETag) para el sondeo con 304.
- WCAG 2.2 SC 2.2.1 (Timing Adjustable): no aplica a este ADR; aplica a la redirección del Feature (decisión menor D4-2 del PO).
