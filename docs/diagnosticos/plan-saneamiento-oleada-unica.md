# Plan: saneamiento en Diagnóstico Vehicular, en una sola oleada

> Generado: 2026-11-14. Estado: **propuesto, pendiente de aprobación del usuario**. Nada creado en ADO ni en el código.
>
> Insumos:
> - Épicas #12678 y #12679 (FLIT - DIAGNOSTICO), leídas el 2026-11-14.
> - `docs/diagnosticos/saneamiento-en-diagnostico.md` y `docs/diagnosticos/especificacion-diagnostico-saneamiento-sistema-anterior.md`.
> - Cinco inventarios de código del 2026-11-14 sobre la rama `feature/AB-14563-adr-0086-ajuste-dictamen-notificaciones` (HEAD `df542a56c`). Las citas `archivo:línea` son de esa fecha: **reverificar antes de editar**.
> - Decisiones del usuario S1–S29 en `.claude/state/saneamiento/decisiones-usuario.md`.

## 0. Cómo leer este plan

- Las secciones 1 a 3 dicen **qué** se construye y por qué. Las secciones 4 a 8 son el **contrato técnico**: lo que ahí está escrito no se vuelve a decidir durante la implementación.
- La sección 9 es la **ejecución**: fases, carriles, archivos y pruebas por carril.
- La sección 12 lista lo que el plan **supone** y hay que comprobar en la Fase 0. Si algún supuesto falla, el carril afectado se detiene y reporta; no improvisa.
- Regla para los agentes: ante una duda que este plan no resuelva, se aplica la opción **más restrictiva** (menos permisos, menos datos expuestos, sin tocar el dictamen), se anota en `.claude/state/saneamiento/dudas-ejecucion.md` y se sigue. No se interrumpe la oleada por dudas de detalle.

---

## 1. Qué se construye

Un **circuito de gestión de saneamiento sin dinero** dentro de Diagnóstico Vehicular:

1. El usuario de la compañía marca un vehículo para sanear con **una bandera en un clic**, individual o en bloque.
2. FLIT ve la solicitud en una **bandeja** del backoffice, la toma, la gestiona por fuera del sistema y registra el resultado **por fuente** (SIMIT del vehículo, del propietario, del locatario, impuesto, SOAT, RTM, otro), con un **PDF opcional** de evidencia de pago por renglón.
3. FLIT cierra el saneamiento como **saneado** o **no procede**. Puede reabrirlo o anularlo.
4. El cliente sigue el estado en el tablero del lote, en una **pestaña nueva de vehículos** en el inicio y por la **campana**.
5. Un **panel de deuda por fuente**, exportable a PDF y Excel, muestra qué debe el vehículo y dónde.

Diagnóstico **no cobra, no liquida y no registra pagos estructurados**. El saneamiento **no cambia el dictamen**.

### 1.1 Decisiones cerradas

| # | Decisión | Resultado |
|---|---|---|
| S1 | Alcance | Circuito de gestión sin dinero. Solicitud en un clic, sin texto ni adjuntos del cliente. PDF opcional de evidencia por ítem saneado. Revierte D-08 = S2 |
| S2 | Quién marca | Cualquier usuario de la compañía con rol de Diagnóstico (`usuario_diagnostico`, `admin_diagnostico`). FLIT no marca |
| S3 | Cuándo aparece la bandera | En los dos modos (Simple y Con Análisis IA), con cualquier veredicto. El dictamen orienta, no condiciona |
| S4 | Panel de deuda | Motivos y advertencias del dictamen primero, luego deuda por fuente. Nunca vacío |
| S5 | Elegibilidad | Vehículo terminado: todas sus consultas `Completado` o `Na`. No espera al lote |
| S7 | Desmarcar | El cliente solo en `SOLICITADO`. FLIT (SuperAdmin) puede anular también en `EN_GESTION` |
| S8 | Masivo | Individual y «Sanear (N)» por selección. Sin «todo el lote» |
| S10 | Estados | `SOLICITADO` → `EN_GESTION` → `SANEADO` o `NO_PROCEDE`; `ANULADO`; reapertura por FLIT |
| S11 | Ítem saneado | Un renglón por fuente, con resultado, nota y PDF opcional. FLIT puede agregar «Otro» |
| S12 | Dónde se ve | Bandeja solo para FLIT. Cliente: pestaña «Vehículos» en el inicio con todas las placas y filtros. Etiqueta y filtro en el tablero |
| S13 | Permiso de gestión | Operación FLIT más permiso nuevo `diagnostico.saneamiento.gestionar` |
| S14 | Efecto de `SANEADO` | Ninguno sobre el dictamen. Para desbloquear, FLIT ajusta el dictamen o el admin reprocesa |
| S15 | Reproceso | No cambia el saneamiento. Los renglones son una foto al tomar |
| S16 | Crear trámite | Reglas intactas. Aviso si hay saneamiento abierto, sin bloquear |
| S17 | Total SIMIT | No se suma ni se muestra total. Comparendos con su valor tal como llega |
| S18 | Exportación | PDF y Excel por vehículo; un Excel para la selección. Sin enmascarado adicional |
| S19 | Avisos al cliente | Campana: tomado, cerrado, reabierto, anulado por FLIT. Sin correo |
| S19-bis | Avisos a FLIT | Insignia con el número de solicitudes sin tomar en el menú. Sin campana |
| S20–S23, S25 | Sistema anterior | No se portan: «Ver pagos», cobro, análisis libre como paso previo, candado manual, «finalizar diagnóstico», corte `id >= 211`, migración de datos |
| S22 | Rótulo | Se queda «Bloqueado» |
| S24 | Rama | Apilada sobre la del rediseño. PR cuando #14561 esté en `develop` |
| S26 | Unicidad | Un saneamiento no anulado por vehículo. Tras cierre, solo FLIT reabre |
| S27 | Reglas de cierre | Ver 3.3 |
| S28 | Evidencia | PDF, máximo 10 MB, reemplazable en `EN_GESTION` |
| S29 | Agrupación de avisos | Un aviso por acción de FLIT y por diagnóstico, con la cantidad |

### 1.2 Fuera de alcance

- Cobro, liquidación, pasarela, tarifas y registro estructurado de pagos.
- Correo y cualquier canal distinto de la campana.
- Total de deuda del SIMIT y total general del vehículo (frente C1 sigue abierto y no bloquea).
- Cambios al motor de reglas, al catálogo o al ajuste FLIT del dictamen.
- Evento de integración hacia otros productos (no se agrega nada al AsyncAPI).
- Migración de datos del sistema anterior.
- Borrado físico de evidencias reemplazadas (el almacén no tiene borrado; ver 6.4).

---

## 2. Qué cambia frente a lo documentado

| Documento | Qué decía | Qué queda |
|---|---|---|
| Plan v2, D-08 = S2 | Sanear es un panel informativo, sin solicitud ni gestión | **Revertida** por S1. ADR-0087 la reemplaza |
| Épica #12679, «Derivación a saneamiento» | Redirige a un módulo de saneamiento con la deuda | El circuito vive dentro de Diagnóstico. No hay módulo externo |
| Épica #12679, ajustes 2026-10 | Sanear aparece con Bloqueado o con deuda (Simple) | Aparece en cualquier vehículo terminado (S3) |
| Épica #12679, ajustes 2026-10 | Con Bloqueado no se ofrece Crear trámite | **Sigue igual** |
| Épica #12678 | Rótulo «Bloqueado / Requiere Saneamiento» | Se queda «Bloqueado» (S22) |
| ADR-0084:110 | Destino `SANEAMIENTO` de `derivaciones` sin diseño | No se usa. El saneamiento tiene tablas propias, no es una derivación |

Enmiendas a ADO: sección 10.

---

## 3. Reglas de negocio

### 3.1 Elegibilidad para solicitar

Un vehículo se puede marcar cuando se cumplen las dos:

1. Está **terminado**: `AvanceVehiculo.Terminado` es verdadero (hay al menos una celda y todas están en `Completado` o `Na`; `services/core-diagnostico/src/Flit.Diagnostico.Domain/Diagnosticos/AvanceVehiculo.cs:18`).
2. **No tiene** un saneamiento en estado distinto de `ANULADO`.

No dependen de la elegibilidad: el modo, el veredicto, el estado del lote, ni que el vehículo ya tenga trámite.

Motivos de no elegibilidad (códigos del contrato): `NO_TERMINADO`, `YA_SOLICITADO`.

### 3.2 Máquina de estados

```text
            (cliente marca)
  ─────────────────────────────► SOLICITADO
                                   │  │
        (cliente desmarca) ◄───────┘  │ (FLIT toma)
        (FLIT anula)                  ▼
            ANULADO ◄──(FLIT anula)── EN_GESTION ◄────────────┐
                                       │      │               │ (FLIT reabre,
                     (FLIT cierra)     │      │ (FLIT cierra) │  con motivo)
                                       ▼      ▼               │
                                   SANEADO   NO_PROCEDE ──────┘
```

| Transición | Quién | Condición | Motivo |
|---|---|---|---|
| (ninguno) → `SOLICITADO` | Usuario de la compañía con `saneamiento.solicitar` | Elegible (3.1) | No |
| `SOLICITADO` → `ANULADO` | El mismo tipo de usuario (cualquiera de la compañía con el permiso, no solo quien marcó) | — | No |
| `SOLICITADO` → `EN_GESTION` | FLIT con `saneamiento.gestionar` | — | No |
| `SOLICITADO` o `EN_GESTION` → `ANULADO` | FLIT con `saneamiento.gestionar` | — | Obligatorio |
| `EN_GESTION` → `SANEADO` | FLIT | Reglas de 3.3 | No |
| `EN_GESTION` → `NO_PROCEDE` | FLIT | — | Obligatorio |
| `SANEADO` o `NO_PROCEDE` → `EN_GESTION` | FLIT | — | Obligatorio |

Cualquier otra transición responde `409 TRANSICION_INVALIDA`. `ANULADO` es final; para volver a sanear, el cliente marca de nuevo y nace otro saneamiento.

Motivo: texto de 10 a 500 caracteres, sin saltos de línea al inicio o al final.

### 3.3 Renglones

**Generación.** Al pasar a `EN_GESTION` por primera vez, el sistema crea un renglón por cada fuente con hallazgo. La función es de dominio, pura y **no depende del modo ni del tipo de trámite** (por eso sirve en Simple):

| Fuente | Hay hallazgo cuando… | Código de hallazgo |
|---|---|---|
| `SIMIT_VEHICULO` | La consulta SIMIT del vehículo reporta comparendos, multas o acuerdo de pago con saldo, o no hay paz y salvo | `DEUDA` |
| `SIMIT_PROPIETARIO` | Lo mismo para el propietario | `DEUDA` |
| `SIMIT_LOCATARIO` | Lo mismo para el locatario | `DEUDA` |
| `SIMIT_LOCATARIO` | La consulta quedó `Na` por falta de documento del locatario y el vehículo tiene locatario | `SIN_VERIFICAR` |
| `IMPUESTO` | `TieneDeuda`, o `MontoTotal > 0`, o alguna vigencia sin pagar | `DEUDA` |
| `SOAT` | SOAT no vigente y el vehículo no está exento | `NO_VIGENTE` |
| `RTM` | RTM no vigente y ya exigible | `NO_VIGENTE` |

Los predicados son **los mismos** que ya usan las reglas `SIMIT_*`, `IMPUESTO`, `SOAT_NO_VIGENTE` y `RTM_NO_VIGENTE` del catálogo: se reutilizan `LectorHechosDictamen` y `HechosDictamen`, no se reescriben. Una fuente en `Na` por otro motivo no genera renglón.

Al reabrir no se regeneran renglones.

**Edición (solo en `EN_GESTION`, solo FLIT):**

- Cambiar el **resultado** de un renglón: `PENDIENTE`, `SANEADO`, `NO_PROCEDE`.
- Escribir una **nota** (0 a 500 caracteres).
- Cargar, reemplazar o quitar el **PDF de evidencia**.
- Agregar un renglón **`OTRO`** con descripción obligatoria (3 a 200 caracteres). Puede haber varios.
- Quitar un renglón (automático o manual). Queda en el historial.

**Cierre:**

- `SANEADO` exige: al menos un renglón, ninguno en `PENDIENTE` y al menos uno en `SANEADO`. Si no, `422 CIERRE_INCOMPLETO`.
- `NO_PROCEDE` exige solo el motivo. Los renglones quedan como estén.
- Un vehículo sin hallazgos no tiene renglones: solo se cierra como `NO_PROCEDE`, salvo que FLIT agregue un `OTRO`.

**Observación general de FLIT:** texto opcional (0 a 1000 caracteres), editable en `EN_GESTION`, visible para el cliente.

### 3.4 Relación con el resto del producto

- **Dictamen:** ninguna transición del saneamiento emite, ajusta ni recalcula el dictamen. Ninguna emisión de dictamen toca el saneamiento.
- **Reproceso:** si deja el vehículo no terminado, el saneamiento existente sigue como está.
- **Crear trámite:** `ElegibilidadDerivacion` no cambia. Si el vehículo tiene un saneamiento en `SOLICITADO` o `EN_GESTION`, el diálogo de confirmación de «Crear trámite» (individual y masivo) muestra un aviso: «Este vehículo tiene un saneamiento en curso». No bloquea.
- **Backoffice sobre otra compañía:** FLIT ve la etiqueta del saneamiento en la grilla del backoffice, con enlace a la gestión. No marca la bandera.

### 3.5 Qué ve cada quien

| Dato | Cliente | FLIT |
|---|---|---|
| Estado, fechas de solicitud, toma y cierre | Sí | Sí |
| Renglones: fuente, hallazgo, resultado, nota, evidencia | Sí | Sí |
| Observación general y motivo de cierre, reapertura o anulación | Sí | Sí |
| Quién solicitó (id de usuario) | No se muestra nombre: Diagnóstico no tiene directorio. Se muestra «Tú» si es el usuario actual; si no, «Otro usuario de la compañía» | Igual |
| Quién gestionó en FLIT | **No.** El cliente ve «FLIT» | Id del usuario en el historial |
| Historial completo de eventos | Versión reducida: evento, fecha, actor genérico (compañía o FLIT) | Completo |

---

## 4. Modelo de datos

Migración única **`<AAAAMMDDHHMMSS>_S1_Saneamiento`** en `services/core-diagnostico/src/Flit.Diagnostico.Api/Persistence/Migrations/`, posterior a `20261113070402_R9_AjusteConTramite`. La crea **solo el carril BE-A**. Todas las entidades implementan `IEntidadDeEmpresa` (filtro global de `TenantFilter.cs:35`). Ids `Guid.CreateVersion7()`. Concurrencia con `xmin` como en `DictamenVehiculo`.

### 4.1 `saneamientos`

| Columna | Tipo | Nulo | Nota |
|---|---|---|---|
| `id` | uuid | no | PK |
| `tenant_id` | uuid | no | |
| `diagnostico_id` | uuid | no | FK a `diagnosticos` |
| `vehiculo_id` | uuid | no | FK a la tabla de `VehiculoDiagnostico` |
| `estado` | varchar(16) | no | CHECK `ck_saneamientos_estado` con los 5 valores |
| `solicitado_por` | uuid | no | claim `sub` |
| `solicitado_en` | timestamptz | no | |
| `tomado_por` | uuid | sí | |
| `tomado_en` | timestamptz | sí | Primera toma; no cambia al reabrir |
| `cerrado_por` | uuid | sí | |
| `cerrado_en` | timestamptz | sí | Se limpia al reabrir |
| `motivo_cierre` | varchar(500) | sí | Solo `NO_PROCEDE` |
| `anulado_por` | uuid | sí | |
| `anulado_en` | timestamptz | sí | |
| `anulado_por_flit` | boolean | no | default false |
| `motivo_anulacion` | varchar(500) | sí | Obligatorio si `anulado_por_flit` |
| `observacion_flit` | varchar(1000) | sí | |

Índices:
- `ux_saneamiento_vigente`: único en (`tenant_id`, `vehiculo_id`) con filtro `estado <> 'ANULADO'`. Garantiza S26 y resuelve la carrera de dos clics simultáneos (el segundo recibe `409 YA_SOLICITADO`).
- `ix_saneamientos_bandeja`: (`estado`, `solicitado_en`) para la bandeja entre compañías.
- `ix_saneamientos_diagnostico`: (`tenant_id`, `diagnostico_id`).

### 4.2 `saneamiento_renglones`

| Columna | Tipo | Nulo | Nota |
|---|---|---|---|
| `id` | uuid | no | PK |
| `tenant_id` | uuid | no | |
| `saneamiento_id` | uuid | no | FK, borrado en cascada |
| `fuente` | varchar(24) | no | CHECK: `SIMIT_VEHICULO`, `SIMIT_PROPIETARIO`, `SIMIT_LOCATARIO`, `IMPUESTO`, `SOAT`, `RTM`, `OTRO` |
| `hallazgo` | varchar(16) | sí | `DEUDA`, `NO_VIGENTE`, `SIN_VERIFICAR`. Nulo en `OTRO` |
| `descripcion` | varchar(200) | sí | Obligatoria en `OTRO` (CHECK) |
| `automatico` | boolean | no | true si lo generó el sistema |
| `resultado` | varchar(12) | no | CHECK: `PENDIENTE`, `SANEADO`, `NO_PROCEDE`. Default `PENDIENTE` |
| `nota` | varchar(500) | sí | |
| `evidencia_referencia` | varchar(512) | sí | Referencia del almacén |
| `evidencia_nombre` | varchar(200) | sí | Nombre original, saneado |
| `evidencia_sha256` | char(64) | sí | |
| `evidencia_tamano` | bigint | sí | |
| `evidencia_subida_por` | uuid | sí | |
| `evidencia_subida_en` | timestamptz | sí | |

Índices: `ux_renglon_fuente` único en (`saneamiento_id`, `fuente`) con filtro `fuente <> 'OTRO'`. CHECK `ck_renglon_evidencia`: las seis columnas de evidencia son todas nulas o todas no nulas.

### 4.3 `saneamiento_historial`

Solo inserción. Es la auditoría del circuito (cubre R7 del sistema anterior).

| Columna | Tipo | Nulo | Nota |
|---|---|---|---|
| `id` | uuid | no | PK |
| `tenant_id` | uuid | no | |
| `saneamiento_id` | uuid | no | FK |
| `evento` | varchar(24) | no | CHECK con la lista de abajo |
| `estado_anterior` | varchar(16) | sí | |
| `estado_nuevo` | varchar(16) | sí | |
| `renglon_id` | uuid | sí | Sin FK (el renglón puede quitarse) |
| `fuente` | varchar(24) | sí | Copia, para que el historial se lea aunque el renglón no exista |
| `usuario_id` | uuid | no | |
| `es_flit` | boolean | no | true si actuó por operación FLIT |
| `motivo` | varchar(500) | sí | |
| `ocurrido_en` | timestamptz | no | |

Eventos: `SOLICITADO`, `DESMARCADO`, `TOMADO`, `RENGLON_AGREGADO`, `RENGLON_QUITADO`, `RENGLON_ACTUALIZADO`, `EVIDENCIA_CARGADA`, `EVIDENCIA_QUITADA`, `OBSERVACION_EDITADA`, `CERRADO_SANEADO`, `CERRADO_NO_PROCEDE`, `REABIERTO`, `ANULADO_FLIT`.

### 4.4 Columna desnormalizada en el vehículo

`saneamiento_estado varchar(16) NULL` en la tabla de `VehiculoDiagnostico`, con el estado del saneamiento no anulado (nulo si no hay). Se actualiza **en la misma transacción** que cada transición, junto con la marca de fila del tablero (`EscritorCeldas.MarcarFila` / `SubirVersionFilaAsync`, `EscritorCeldas.cs:20`) para que la grilla incremental lo recoja.

Motivo: la grilla del lote, la grilla de placas y sus filtros lo necesitan en cada fila, y evita un join por fila. Un único escritor (`EscritorSaneamiento`) hace las dos escrituras; ningún otro código toca la columna.

### 4.5 Notificaciones

En la misma migración se regenera `ck_notificaciones_tipo` con los tipos nuevos (se agregan a `Notificacion.Tipos`, `Notificacion.cs:11`):

| Tipo | Cuándo | `datos` |
|---|---|---|
| `SANEAMIENTO_TOMADO` | FLIT toma | `{"cantidad": n}` |
| `SANEAMIENTO_CERRADO` | FLIT cierra | `{"cantidad": n, "resultado": "SANEADO" \| "NO_PROCEDE"}` |
| `SANEAMIENTO_REABIERTO` | FLIT reabre | `{"cantidad": 1}` |
| `SANEAMIENTO_ANULADO` | FLIT anula | `{"cantidad": 1}` |

`datos` cumple `ck_notificaciones_datos` (`NotificacionConfig.cs:16`): claves en minúscula, valores en mayúscula o número hasta 100000, sin texto libre. Por eso **no lleva placa ni motivo**.

Destinatario: `solicitado_por`, en el tenant del lote. `DiagnosticoId` = el lote. `ReferenciaId` = id del saneamiento (si `cantidad` es 1) o del lote (si es mayor). Agrupación S29: en una acción masiva de FLIT se emite **un aviso por cada par (solicitante, diagnóstico)** con la cantidad.

### 4.6 Sin RLS

`core-diagnostico` no usa RLS de Postgres; el aislamiento es el filtro global de EF. Las tablas nuevas siguen ese patrón. Las lecturas entre compañías usan `db.DeLaEmpresa<T>()` o `IgnoreQueryFilters` **solo dentro de `Modulos/Backoffice`** (lo exige `IgnoreQueryFiltersSoloEnBackofficeTests`).

---

## 5. Permisos y capacidades

### 5.1 Permisos nuevos (`Plataforma/Permisos.cs`)

| Slug | Roles por defecto | Uso |
|---|---|---|
| `diagnostico.saneamiento.solicitar` | `usuario_diagnostico`, `admin_diagnostico` | Marcar y desmarcar |
| `diagnostico.saneamiento.gestionar` | Ninguno | Gestión FLIT. El SuperAdmin pasa siempre (`Autorizacion.cs:55`); una compañía FLIT lo recibe si el hub se lo asigna |

Se publican con el manifiesto (`RegistroManifiesto.cs:27`). Hay que actualizar el test del manifiesto.

### 5.2 Autorización por endpoint

| Endpoints | Política |
|---|---|
| Marcar, desmarcar, masivo | `RequirePermiso(SaneamientoSolicitar)` + filtro `SinBackoffice` (el mismo de `DerivacionEndpoints.cs:37`) |
| Leer saneamiento, panel de deuda, grilla de placas | `RequirePermiso(DiagnosticosVer)` + `AccesoLote.VisiblesAsync` |
| Descargar evidencia, PDF y Excel de deuda | `RequirePermiso(ReportesDescargar)` + `AccesoLote` |
| Todo `/backoffice/saneamientos…` | `PoliticaBackoffice` + `RequiereOperacionFlit()` + `RequirePermiso(SaneamientoGestionar)` |

### 5.3 Capacidades de sesión

`Plataforma/Capacidades.cs:12-47` y `frontend-diagnostico/lib/sesion.ts:4-23`:

| Capacidad | Regla |
|---|---|
| `sanear` | `usuario.Tiene(SaneamientoSolicitar)` |
| `gestionarSaneamiento` | `usuario.BackofficeOperacion && usuario.Tiene(SaneamientoGestionar)` |

En el front las dos son **opcionales** en el tipo (como `backofficeOperacion?`), para que un backend viejo no rompa la sesión.

---

## 6. Contrato de la API

`contracts/openapi/diagnostico.v1.yaml`: `info.version` **1.3.1 → 1.4.0** (cambio aditivo). Actualizar `OpenApiDiagnosticoContratoTests`. Todo en `snake_case`. Errores con el formato existente (`code`, `error`, `campo`).

Módulo nuevo `Modulos/Saneamiento/` (cliente) y archivos nuevos en `Modulos/Backoffice/` (FLIT). Se registran en `Modulos.MapModulosDiagnostico` (`Modulos.cs:182`) y `BackofficeEndpoints.cs:34`.

### 6.1 Cliente

| Método y ruta | Qué hace | Respuestas |
|---|---|---|
| `PUT /diagnosticos/{id}/vehiculos/{vid}/saneamiento` | Marca. Sin cuerpo. Idempotente si ya está `SOLICITADO` por la misma compañía | `201` con el saneamiento · `200` si ya estaba `SOLICITADO` · `409 VEHICULO_NO_TERMINADO` · `409 YA_SOLICITADO` (hay uno en otro estado) · `404` |
| `DELETE /diagnosticos/{id}/vehiculos/{vid}/saneamiento` | Desmarca | `204` · `409 SANEAMIENTO_EN_GESTION` (ya no está `SOLICITADO`) · `404 SIN_SANEAMIENTO` |
| `POST /saneamientos/solicitudes` | Masivo. Cuerpo `{"vehiculo_ids": [uuid…]}`, 1 a 500, de uno o varios lotes | `200 {"solicitados": n, "omitidos": [{"vehiculo_id", "motivo"}]}` con motivos `NO_TERMINADO`, `YA_SOLICITADO`, `NO_ENCONTRADO` · `400 LISTA_INVALIDA` |
| `GET /diagnosticos/{id}/vehiculos/{vid}/saneamiento` | Detalle | `200` (6.3) · `404 SIN_SANEAMIENTO` |
| `GET /diagnosticos/{id}/vehiculos/{vid}/saneamiento/renglones/{rid}/evidencia` | Descarga el PDF | `200 application/pdf` (stream, `Content-Disposition: attachment`) · `404` |
| `GET /diagnosticos/{id}/vehiculos/{vid}/deuda` | Panel de deuda | `200` (6.5) · `409 VEHICULO_NO_TERMINADO` |
| `GET /diagnosticos/{id}/vehiculos/{vid}/deuda/pdf` | PDF del panel | `200 application/pdf` |
| `GET /diagnosticos/{id}/vehiculos/{vid}/deuda/excel` | Excel del panel | `200` xlsx |
| `POST /vehiculos/deuda/excel` | Excel de varias placas. Cuerpo `{"vehiculo_ids": […]}`, 1 a 200 | `200` xlsx · `400 LISTA_INVALIDA` |
| `GET /vehiculos` | Grilla de placas de la compañía (6.6) | `200` |

El masivo es **síncrono y en una transacción**: son inserciones locales, sin llamadas externas. Los ids que el usuario no puede ver (por `AccesoLote`) se reportan como `NO_ENCONTRADO`, sin revelar que existen.

**Cambios en contratos existentes:**

- `GET /diagnosticos/{id}/grilla` (`EndpointsTablero.cs:31`): cada fila agrega
  `"saneamiento": {"estado": "SOLICITADO" | … | null, "elegible": bool, "motivo_no_elegible": "NO_TERMINADO" | "YA_SOLICITADO" | null}`.
- Grilla del backoffice: cada fila agrega `"saneamiento": {"estado", "id"}` (sin elegibilidad).
- `GET` de sesión: capacidades nuevas (5.3).

### 6.2 Backoffice (FLIT)

Dos grupos, porque la bandeja es entre compañías y el resto sigue el patrón de empresa explícita:

**Grupo A, sin `ConEmpresaExplicita`** (la empresa es un filtro opcional):

| Método y ruta | Qué hace |
|---|---|
| `GET /backoffice/saneamientos` | Bandeja. Filtros: `estado` (repetible), `empresa`, `placa`, `desde`, `hasta` (sobre `solicitado_en`), `pagina` (1), `tamano` (25, máx. 200). Orden: `solicitado_en` ascendente. Devuelve `total` |
| `GET /backoffice/saneamientos/resumen` | `{"solicitados": n}` para la insignia |
| `GET /backoffice/saneamientos/excel` | Excel de la bandeja con los mismos filtros (máx. 5000 filas; si hay más, `422 DEMASIADAS_FILAS`) |
| `POST /backoffice/saneamientos/tomar` | Toma varios. Cuerpo `{"items": [{"empresa": uuid, "id": uuid}]}`, 1 a 200. Responde `{"tomados": n, "omitidos": [{"id", "motivo"}]}` con `TRANSICION_INVALIDA` o `NO_ENCONTRADO` |

Auditoría de lectura del grupo A: se llama a `AuditoriaLecturaBackoffice.RegistrarAsync` (`AuditoriaLecturaBackoffice.cs:17`) **una vez por cada empresa distinta presente en la página devuelta** (o en el Excel). `resumen` no audita: solo devuelve un conteo.

Fila de la bandeja: `id`, `empresa` (uuid), `diagnostico_id`, `diagnostico_nombre`, `vehiculo_id`, `placa`, `modo`, `veredicto`, `estado`, `solicitado_en`, `tomado_en`, `cerrado_en`, `renglones_total`, `renglones_pendientes`.

**Grupo B, con `?empresa=`** (patrón `EmpresaExplicita.cs:15`; escritura con `EscrituraEntreEmpresas.cs:23`):

| Método y ruta | Qué hace | Errores propios |
|---|---|---|
| `GET /backoffice/saneamientos/{sid}` | Detalle completo, con historial completo | |
| `POST …/{sid}/tomar` | `SOLICITADO` → `EN_GESTION`; genera renglones | `409 TRANSICION_INVALIDA` |
| `PUT …/{sid}/observacion` | `{"observacion": string \| null, "version"}` | `409 NO_EDITABLE` |
| `POST …/{sid}/renglones` | Agrega `OTRO`: `{"descripcion"}` | `409 NO_EDITABLE` · `422` |
| `PUT …/{sid}/renglones/{rid}` | `{"resultado", "nota", "version"}` | `409 NO_EDITABLE` · `409 VERSION_DESACTUALIZADA` |
| `DELETE …/{sid}/renglones/{rid}` | Quita el renglón | `409 NO_EDITABLE` |
| `PUT …/{sid}/renglones/{rid}/evidencia` | Sube o reemplaza el PDF (multipart, campo `archivo`) | 6.4 |
| `DELETE …/{sid}/renglones/{rid}/evidencia` | Quita la evidencia | `409 NO_EDITABLE` |
| `GET …/{sid}/renglones/{rid}/evidencia` | Descarga | `404` |
| `POST …/{sid}/cerrar` | `{"resultado": "SANEADO" \| "NO_PROCEDE", "motivo"?, "version"}` | `422 CIERRE_INCOMPLETO` · `422 MOTIVO_REQUERIDO` |
| `POST …/{sid}/reabrir` | `{"motivo", "version"}` | `409 TRANSICION_INVALIDA` |
| `POST …/{sid}/anular` | `{"motivo", "version"}` | `409 TRANSICION_INVALIDA` |

`NO_EDITABLE` = el saneamiento no está en `EN_GESTION`. `version` es el `xmin` del saneamiento (o del renglón en su `PUT`), con el mismo mecanismo de concurrencia del ajuste del dictamen (`OverrideDictamenEndpoints.cs`).

### 6.3 Forma del saneamiento

```json
{
  "id": "…", "version": 123,
  "estado": "EN_GESTION",
  "solicitado_en": "…", "solicitado_por_mi": true,
  "tomado_en": "…", "cerrado_en": null,
  "motivo_cierre": null, "motivo_anulacion": null,
  "observacion_flit": null,
  "renglones": [
    { "id": "…", "version": 45, "fuente": "SIMIT_PROPIETARIO", "hallazgo": "DEUDA",
      "descripcion": null, "automatico": true, "resultado": "PENDIENTE", "nota": null,
      "evidencia": { "nombre": "pago.pdf", "tamano": 123456, "subida_en": "…" } }
  ],
  "historial": [
    { "evento": "SOLICITADO", "ocurrido_en": "…", "actor": "COMPANIA", "fuente": null, "motivo": null }
  ]
}
```

- `evidencia` es `null` si no hay. Nunca se expone la referencia del almacén ni el sha.
- Para el cliente, `actor` es `COMPANIA` o `FLIT`. La versión del backoffice agrega `usuario_id`.
- El cliente no recibe los eventos `RENGLON_ACTUALIZADO` ni `OBSERVACION_EDITADA` (ruido); sí los demás.

### 6.4 Evidencia en PDF

- Multipart leído como en `CargaEndpoints.cs:60-80` (`ReadFormAsync`, `DisableAntiforgery`).
- Límite: config `Diagnostico:Saneamiento:EvidenciaMaximoBytes`, por defecto **10 485 760** (10 MB). Si se pasa, `413 ARCHIVO_MUY_GRANDE`.
- Validación: `Content-Type` `application/pdf` **y** firma `%PDF` en los primeros bytes (reutilizar `FirmaPdf` de `FormatoCarga.cs:27`). Si falla, `422 ARCHIVO_NO_ES_PDF`. Archivo vacío o ausente: `400 ARCHIVO_REQUERIDO`.
- Nombre: se guarda el nombre original recortado a 200 caracteres, sin rutas ni caracteres de control. Al descargar, el nombre es `evidencia-{placa}-{fuente}.pdf`, nunca el original.
- Almacenamiento: `IFabricaAlmacen.Para(db)` → `GuardarAsync(empresaId, "saneamiento-evidencia", nombre, "application/pdf", contenido, ct)` (`AlmacenArchivos.cs:30`). Se guardan `Referencia`, `Sha256`, `Tamano`.
- Descarga: `AbrirAsync(referencia)` + `Results.Stream`, como `EvidenciasEndpoints.cs:55`.
- **Reemplazo y borrado:** el almacén no tiene borrado. Al reemplazar o quitar, el renglón deja de apuntar al archivo anterior, que queda huérfano. Es una deuda conocida y aceptada en esta oleada; se anota en el ADR.
- El PDF **no se abre ni se analiza** en el servidor: se guarda y se sirve como adjunto.

### 6.5 Panel de deuda

```json
{
  "vehiculo": { "id": "…", "placa": "ABC123" },
  "modo": "CON_ANALISIS_IA",
  "dictamen": { "veredicto": "BLOQUEADO", "catalogo_version": 2,
                "motivos": [ { "codigo": "SIMIT_PROPIETARIO", "efecto": "BLOQUEA", "texto": "…" } ] },
  "fuentes": [
    { "fuente": "SIMIT_PROPIETARIO", "estado": "CON_DEUDA", "consultado_en": "…",
      "persona": { "tipo_documento": "CC", "documento": "…", "nombre": "…" },
      "paz_y_salvo": false,
      "comparendos": [ { "numero": "…", "fecha": "…", "valor": "450000.00", "estado": "…" } ],
      "comparendos_truncados": false,
      "acuerdos_pago": [ { "saldo_pendiente": 300000 } ] },
    { "fuente": "IMPUESTO", "estado": "CON_DEUDA", "consultado_en": "…",
      "monto_total": 1250000,
      "vigencias": [ { "anio": 2025, "valor": 1250000, "pagada": false } ] },
    { "fuente": "SOAT", "estado": "NO_VIGENTE", "vence": "…" },
    { "fuente": "RTM", "estado": "SIN_HALLAZGO" },
    { "fuente": "SIMIT_LOCATARIO", "estado": "NO_APLICA", "motivo_na": "SIN_DOCUMENTO_LOCATARIO" }
  ],
  "saneamiento": { "estado": "EN_GESTION" }
}
```

- `dictamen` es `null` en modo Simple o si no hay dictamen vigente.
- `estado` por fuente: `CON_DEUDA`, `NO_VIGENTE`, `SIN_VERIFICAR`, `SIN_HALLAZGO`, `NO_APLICA`.
- El `valor` de cada comparendo va **como texto, tal como llega** (S17). No hay `total` del SIMIT ni total general. El impuesto sí trae `monto_total` numérico.
- Los comparendos se listan con el mismo tope de 100 del detalle (`DetalleCelda.cs:45`) y la bandera `comparendos_truncados`.
- Datos personales: los mismos que ya entrega el detalle de la celda a ese usuario. Se reutiliza la proyección existente (`SimitVisible.cs:23`, `DetalleCelda.cs`), no se lee el crudo.

**PDF** (`Modulos/Reportes/PdfDeuda.cs`): mismo armazón que `PdfVehiculo.cs:125-134` (`FlitLetterhead`, `TablaFlit`, `FuentesFlit`). Secciones: encabezado con placa y fecha de generación; dictamen (si hay) con veredicto y versión del catálogo; una tabla por fuente con su fecha de consulta; estado del saneamiento con sus renglones (sin el contenido de las evidencias). Nombre: `deuda-{placa}.pdf`.

**Excel** (`Modulos/Reportes/ExcelDeuda.cs`, con `TabularWorkbookWriter.cs:88`): hojas `Resumen`, `SIMIT`, `Impuesto`, `SOAT y RTM`, `Saneamiento`. En el Excel de varias placas, las mismas hojas con una columna `Placa` al inicio. Los valores del SIMIT van como **texto**; los del impuesto, como número. La protección contra fórmulas del escritor (`PareceFormula`) se mantiene.

### 6.6 Grilla de placas (`GET /vehiculos`)

Filtros (todos opcionales, combinables):

| Parámetro | Valores |
|---|---|
| `placa` | Texto; coincide por prefijo sobre el identificador normalizado |
| `estado` | Repetible. Valores de `VehiculoDiagnostico.EstadoConsultas` |
| `veredicto` | Repetible: `VIABLE`, `BLOQUEADO`, `SIN_DICTAMEN` |
| `saneamiento` | `CON`, `SIN` |
| `saneamiento_estado` | Repetible: los 4 estados no anulados. Implica `CON` |
| `modo` | `SIMPLE`, `CON_ANALISIS_IA` |
| `con_tramite` | `true`, `false` |
| `desde`, `hasta` | Sobre la fecha de creación del diagnóstico |
| `pagina`, `tamano` | 1; 25, máximo 100 |

Orden: fecha de creación del diagnóstico descendente, luego placa. Devuelve `total`.

Fila: `vehiculo_id`, `placa`, `diagnostico_id`, `diagnostico_nombre`, `diagnostico_creado_en`, `modo`, `estado`, `veredicto`, `saneamiento {estado, elegible, motivo_no_elegible}`, `tramite {id, numero} | null`.

Visibilidad: se parte de `AccesoLote.VisiblesAsync` (`AccesoLote.cs:35-58`) sobre las cabeceras y se une con los vehículos. Un usuario con visibilidad «propios» solo ve placas de sus lotes.

Incluye vehículos **en proceso** y **terminados** (S12). No hay refresco incremental: la pantalla recarga al cambiar filtros y con un botón «Actualizar».

---

## 7. Pantallas

El **ui-agent** entrega la especificación visual en la Fase 1 (`.claude/state/saneamiento/spec-ui.md`) sobre este inventario. Todas con los 4 estados (`UiStateBoundary`), WCAG 2.1 AA, 360 px y tema oscuro. Textos en español, sin tecnicismos.

### 7.1 Etiquetas de estado (comunes)

| Estado | Texto | Tono (`StatusBadge`) |
|---|---|---|
| `SOLICITADO` | Saneamiento solicitado | info |
| `EN_GESTION` | En saneamiento | warning |
| `SANEADO` | Saneado | success |
| `NO_PROCEDE` | Saneamiento no procede | neutral |
| sin saneamiento o `ANULADO` | (sin etiqueta) | — |

Fuentes: «SIMIT del vehículo», «SIMIT del propietario», «SIMIT del locatario», «Impuesto vehicular», «SOAT», «Revisión técnico-mecánica», «Otro».

### 7.2 PS-1 Tablero del lote (`components/tablero/`)

- **Bandera por fila**, en `accionVehiculo` (`TableroLote.tsx:245-265`), antes de «Crear trámite». Componente nuevo `BotonSanear`. Patrón del «prioritario» de Trámites (`frontend/components/operacion/TramitesTable.tsx:1146-1162`): botón con `aria-pressed`, un clic, sin confirmación, actualización optimista y reversión si falla.
  - Sin saneamiento y elegible: bandera apagada, «Solicitar saneamiento».
  - `SOLICITADO`: bandera encendida, «Saneamiento solicitado — clic para quitar».
  - `EN_GESTION`, `SANEADO`, `NO_PROCEDE`: bandera encendida y **no interactiva** para desmarcar; el clic abre el panel PS-2. Texto según el estado.
  - No elegible: deshabilitada, con el motivo por `aria-describedby` («Faltan consultas por terminar»).
  - Sin la capacidad `sanear`: la bandera se ve solo si hay saneamiento, como indicador, y abre PS-2.
- **Etiqueta de estado** junto al veredicto de la fila.
- **Filtros** (`lib/tablero/filtrosTablero.ts:15`): se agregan «Con saneamiento» y «Sin saneamiento».
- **Selección múltiple:** la barra pasa a tener dos acciones, «Crear trámites (N)» y «Sanear (N)». Cambios:
  - La casilla por fila se habilita si el vehículo es elegible para **alguna** de las dos acciones (`TableroLote.tsx:143-148`, `396-408`).
  - Cada botón cuenta solo los seleccionados elegibles para su acción. `BarraSeleccionDerivacion` se generaliza a `BarraSeleccionTablero` (props por acción); `BarraSeleccion` del kit no cambia (`packages/ui/src/BarraSeleccion.tsx:14-23`).
  - La barra aparece si el usuario tiene `derivar` **o** `sanear`.
  - Tras «Sanear (N)»: mensaje «Se solicitó el saneamiento de X vehículos. Y se omitieron» con el detalle de motivos.
- **Aviso en «Crear trámite»** (3.4), en `BotonCrearTramite.tsx` y en la confirmación masiva.

### 7.3 PS-2 Panel «Deuda y saneamiento» (`PanelLateral`)

Componente nuevo `PanelDeudaSaneamiento`. Se abre desde: la bandera (si hay saneamiento), una acción de fila «Ver deuda», el `InlineAlert` «Vehículo bloqueado» de `PanelDictamen.tsx:262` (botón «Ver deuda y sanear») y la grilla de placas.

Secciones:
1. **Encabezado:** placa, etiqueta de estado, botón de bandera (misma lógica de PS-1).
2. **Dictamen:** veredicto y motivos (bloqueos primero, advertencias después). Oculta en Simple.
3. **Deuda por fuente:** una `TarjetaPlegable` por fuente con su estado. SIMIT: tabla de comparendos con valor textual, sin total, y aviso si está truncada. Impuesto: vigencias y monto. Si ninguna fuente tiene hallazgo: «No encontramos deudas ni pendientes en las fuentes consultadas».
4. **Saneamiento** (si existe): estado, fechas, observación de FLIT, motivo si aplica, tabla de renglones (fuente, resultado, nota, botón «Descargar evidencia») e historial reducido.
   - Texto fijo cuando el estado es `SANEADO` y el veredicto sigue `BLOQUEADO`: «El saneamiento está registrado, pero el dictamen no cambia solo. Se actualiza cuando se vuelvan a consultar las fuentes o FLIT lo ajuste.»
5. **Pie:** «Descargar PDF» y «Descargar Excel» (capacidad `descargarReportes`; `useDescarga`).

### 7.4 PS-3 Pestaña «Vehículos» en el inicio

- `app/(app)/page.tsx` pasa de una vista a dos pestañas con `Pestanas` del kit: **«Diagnósticos»** (el `ListadoDiagnosticos` actual, sin cambios) y **«Vehículos»** (nueva). La pestaña activa va en la URL (`?vista=vehiculos`). Las tarjetas Express/Masivo quedan encima de las pestañas.
- Componentes nuevos en `components/vehiculos/`: `ListadoVehiculos`, `FiltrosVehiculos`. Filtros en la URL, como `filtrosDesdeUrl` del listado actual.
- Tabla con las columnas de 6.6, paginación en servidor, casillas de selección y la barra con «Sanear (N)» y «Descargar deuda (N)» (Excel de varias placas).
- Acciones por fila: bandera, «Abrir diagnóstico» (navega al tablero), «Ver deuda» (PS-2).
- A 360 px la tabla se convierte en tarjetas (patrón del listado actual).

### 7.5 PS-4 Bandeja «Saneamientos» (backoffice)

- Ruta `app/(app)/backoffice/saneamientos/page.tsx`. Entrada nueva «Saneamientos» en `NAV_BACKOFFICE` (`DiagnosticoShell.tsx:62-65`), visible solo con `gestionarSaneamiento`.
- **Insignia** con el número de `SOLICITADO` (de `/resumen`). Requiere agregar `badge?: number` a `NavItem` (`packages/shell/src/nav.ts:18-39`) y pintarlo en el dock. Se refresca al entrar al backoffice y cada 60 s mientras la pestaña esté visible. Con 0 no se pinta. Más de 99 se muestra «99+».
- Tabla: empresa, diagnóstico, placa, modo, veredicto, estado, solicitado, tomado, cerrado, renglones pendientes. Filtros: estado (por defecto `SOLICITADO`), empresa (con `SelectorEmpresaBackoffice`), placa, fechas.
- Selección múltiple con «Tomar (N)» (solo filas `SOLICITADO`). «Descargar Excel» de la bandeja.
- Clic en fila: va a PS-5.

### 7.6 PS-5 Gestión de un saneamiento (backoffice)

- Ruta `app/(app)/backoffice/saneamientos/[sid]/page.tsx?empresa=`.
- Encabezado: empresa, diagnóstico (enlace al lote del backoffice), placa, estado, veredicto.
- Columna izquierda: el panel de deuda de PS-2 en modo lectura (mismas secciones 2 y 3), para que el operador vea qué hay que sanear.
- Columna derecha:
  - `SOLICITADO`: botón «Tomar».
  - `EN_GESTION`: tabla de renglones editable. Por renglón: `SelectorSegmentado` de resultado, nota, `ZonaArrastre` para el PDF (con nombre y tamaño, «Reemplazar», «Quitar», «Ver»), «Quitar renglón». Botón «Agregar otro concepto». Campo de observación general. Los cambios de renglón se guardan **uno a uno** al salir del campo, con indicador de guardado.
  - Botones de cierre: «Cerrar como saneado» (deshabilitado con explicación si no cumple 3.3) y «Cerrar como no procede» (modal con motivo).
  - `SANEADO` / `NO_PROCEDE`: solo lectura y «Reabrir» (modal con motivo).
  - «Anular» (modal con motivo) en `SOLICITADO` y `EN_GESTION`.
- Historial completo al pie.
- Conflicto de versión (`409 VERSION_DESACTUALIZADA`): `NotificacionPersistente` «Otra persona cambió este saneamiento» con «Recargar».

### 7.7 Otras superficies

- **Grilla del backoffice** (`GrillaBackoffice`): etiqueta de estado por fila con enlace a PS-5.
- **Campana** (`lib/notificaciones/mensajes.ts:35`): cuatro `case` nuevos. Textos:
  - Tomado: «FLIT empezó el saneamiento de {n} vehículo(s)».
  - Cerrado saneado: «{n} vehículo(s) quedaron saneados». No procede: «El saneamiento de {n} vehículo(s) no procede».
  - Reabierto: «FLIT reabrió el saneamiento de un vehículo». Anulado: «FLIT anuló el saneamiento de un vehículo».
  - La navegación sigue siendo al lote (`CampanaDiagnostico.tsx:122`), con el filtro «Con saneamiento» aplicado por query.

### 7.8 Capa de datos del front

Tipos y llamadas a mano en `snake_case` (no hay cliente generado): `lib/saneamiento/api.ts`, `lib/saneamiento/estados.ts` (etiquetas y tonos), `lib/vehiculos/listado.ts`, `lib/backoffice/saneamientos.ts`. Se extienden `lib/tablero/api.ts` (campo `saneamiento` de la fila) y `lib/sesion.ts`. Errores por `code` con `ApiError` (`lib/api.ts:31`).

---

## 8. Backend: estructura del código

```text
services/core-diagnostico/src/
  Flit.Diagnostico.Domain/Saneamiento/
    EstadoSaneamiento.cs            estados, resultados, fuentes, hallazgos (constantes)
    TransicionesSaneamiento.cs      tabla de 3.2, pura
    ElegibilidadSaneamiento.cs      3.1, pura
    HallazgosSaneamiento.cs         3.3 generación, pura, sobre HechosDictamen
    ReglasCierre.cs                 3.3 cierre, pura
  Flit.Diagnostico.Api/Modulos/Saneamiento/
    Saneamiento.cs, RenglonSaneamiento.cs, EventoSaneamiento.cs   entidades
    EscritorSaneamiento.cs          único punto de escritura: transición + historial
                                    + columna del vehículo + marca de fila + avisos
    SaneamientoEndpoints.cs         6.1 (marcar, desmarcar, masivo, detalle, evidencia)
    SaneamientoDtos.cs
    DeudaEndpoints.cs, ConsultaDeuda.cs, DeudaDtos.cs            6.5
    VehiculosEndpoints.cs, ListadoVehiculos.cs                   6.6
  Flit.Diagnostico.Api/Modulos/Backoffice/
    SaneamientosBackofficeEndpoints.cs   6.2 grupos A y B
    BandejaSaneamientos.cs               consulta entre compañías
    EvidenciaSaneamiento.cs              validación y almacenamiento del PDF
  Flit.Diagnostico.Api/Modulos/Reportes/
    PdfDeuda.cs, ExcelDeuda.cs, ExcelBandejaSaneamientos.cs
  Flit.Diagnostico.Api/Persistence/Configuraciones/
    SaneamientoConfig.cs, RenglonSaneamientoConfig.cs, EventoSaneamientoConfig.cs
```

Archivos existentes que se tocan: `Permisos.cs`, `Capacidades.cs`, `Modulos.cs`, `BackofficeEndpoints.cs`, `VehiculoDiagnostico.cs` y su config, `ConsultaGrilla.cs` y DTO de fila, grilla del backoffice, `Notificacion.cs`, `EscritorNotificaciones.cs` (método de agrupación), `diagnostico.v1.yaml`.

Reglas de implementación:

- **Toda escritura pasa por `EscritorSaneamiento`**, en una sola transacción.
- El dominio (`Flit.Diagnostico.Domain/Saneamiento`) no referencia EF ni HTTP.
- Las consultas entre compañías viven en `Modulos/Backoffice`.
- No se toca `EmisorDictamen`, `EvaluadorDictamen`, `CatalogoReglasDictamen` ni `ElegibilidadDerivacion`.
- Logs estructurados sin datos personales: ids, estado y evento, nunca placa con documento ni nombres.

---

## 9. Ejecución

### 9.1 Antes de empezar (gates)

| Gate | Quién |
|---|---|
| Aprobar este plan | Usuario |
| Crear Feature y HUs en ADO (sección 10) y pasarlas a Active | Usuario confirma con «sí» |
| ADR-0087 en `Propuesto` | architecture-agent lo redacta; lo acepta el LT |

### 9.2 Fase 0: preparación (hilo principal, ~20 min)

1. BD de prueba 5433 arriba (`.claude/rules/pruebas-locales.md`).
2. Rama `feature/AB-<HU-BE>-saneamiento-diagnostico` desde `feature/AB-14563-adr-0086-ajuste-dictamen-notificaciones`.
3. Worktrees por carril en `D:\Cursor\FLIT\wt-saneamiento-*`, **con `pnpm install` hecho antes de lanzar agentes** y `ConnectionStrings__Core` exportado (lección de la oleada #14561).
4. Un explore-agent verifica la lista de la sección 12 y deja el brief en `.claude/state/saneamiento/brief-fase0.md`. Si algo no coincide, se corrige este plan antes de lanzar carriles.

### 9.3 Fase 1: diseño (en paralelo, sin código de producto)

| Agente | Entrega |
|---|---|
| architecture-agent | `docs/decisions/ADR-0087-diagnostico-saneamiento-circuito-de-gestion.md` en Propuesto. Revierte D-08. Las alternativas son las ya evaluadas (S1 a/b/c). Incluye la deuda de evidencias huérfanas y la bandeja sin empresa explícita |
| ui-agent (modo especificación) | `.claude/state/saneamiento/spec-ui.md`: PS-1 a PS-5, icono de la bandera (del juego `IconoDx`), insignia del dock, comportamiento a 360 px |
| database-agent | Revisa la sección 4 con `db-schema-validator` y entrega el DDL final en `.claude/state/saneamiento/schema.md` |
| security-agent (consulta previa) | Revisa 5, 6.2 y 6.4: bandeja entre compañías, subida de archivos, datos personales en exportaciones. Hallazgos antes de codificar |

### 9.4 Fase 2: implementación

Concurrencia máxima: **2 carriles .NET y 2 de frontend** a la vez; Vitest con `--maxWorkers=2`; `dotnet` con `-m:2 -nodeReuse:false`. TDD: cada carril guarda el log en rojo y en verde en `.claude/state/saneamiento/evidencias/`. Compilar una vez y correr con `--no-build --no-restore --filter`. **Cada carril corre solo su lista cerrada de pruebas**; no amplía por búsqueda.

#### Backend

| Carril | Contenido | Depende de | Pruebas (lista cerrada) |
|---|---|---|---|
| **BE-A** | Dominio completo (8). Entidades, configs y **la migración** (incluye columna del vehículo y CHECK de notificaciones). `EscritorSaneamiento`. Permisos y capacidades. Endpoints del cliente: marcar, desmarcar, masivo, detalle. Campo `saneamiento` en la grilla del lote | Fase 1 | `TransicionesSaneamientoTests`, `ElegibilidadSaneamientoTests`, `HallazgosSaneamientoTests`, `ReglasCierreTests`, `SaneamientoEndpointsTests`, `SaneamientoMasivoTests`, `GrillaSaneamientoTests`, `CapacidadesTests`, test del manifiesto de permisos |
| **BE-C** | Panel de deuda (`ConsultaDeuda`, endpoint), `PdfDeuda`, `ExcelDeuda`, Excel de varias placas. Grilla de placas (`GET /vehiculos`) | Fase 1. La columna `saneamiento_estado` llega con BE-A: BE-C empieza por deuda y exportaciones y hace la grilla de placas al final, tras rebase | `ConsultaDeudaTests`, `PdfDeudaTests`, `ExcelDeudaTests`, `ListadoVehiculosTests` |
| **BE-B** | Backoffice: bandeja, resumen, Excel, tomar (uno y varios), renglones, evidencia, observación, cerrar, reabrir, anular. Notificaciones agrupadas. Etiqueta en la grilla del backoffice. OpenAPI 1.4.0 | BE-A integrado | `BandejaSaneamientosTests`, `GestionSaneamientoTests`, `EvidenciaSaneamientoTests`, `NotificacionesSaneamientoTests`, `AislamientoSaneamientoTests`, `OpenApiDiagnosticoContratoTests`, `IgnoreQueryFiltersSoloEnBackofficeTests` |

Orden: **BE-A ‖ BE-C**, luego **BE-B** (con BE-C terminando la grilla de placas en paralelo).

Casos que las pruebas deben cubrir sí o sí:

- Cada transición válida e inválida de 3.2, con el actor correcto y el incorrecto (403).
- Dos marcas simultáneas del mismo vehículo: una gana, la otra `409`.
- Usuario de la compañía A no ve, marca ni descarga nada de la compañía B (404, no 403).
- Visibilidad «propios»: no ve placas de lotes ajenos en `GET /vehiculos` ni en el masivo.
- Admin de compañía cliente con una llamada directa a `/backoffice/saneamientos…`: 403.
- Evidencia: no PDF (422), más de 10 MB (413), vacía (400), fuera de `EN_GESTION` (409), descarga por el cliente.
- Cierre `SANEADO` con renglón pendiente o sin renglones: 422.
- El saneamiento no cambia al reemitir el dictamen ni al reprocesar; el dictamen no cambia al cerrar.
- Generación de renglones en modo Simple (sin dictamen).
- Notificaciones: agrupación por solicitante y diagnóstico; `datos` cumple el CHECK.
- Auditoría de lectura: una fila por empresa en la bandeja.

#### Frontend

| Carril | Contenido | Depende de | Pruebas (lista cerrada) |
|---|---|---|---|
| **FE-0** | `badge` en `NavItem` y en el dock (`packages/shell`). Capa de datos completa (7.8) con los tipos del contrato de la sección 6. `lib/saneamiento/estados.ts`. Capacidades en `lib/sesion.ts` | Fase 1 | `packages/shell/src/__tests__/` (insignia), `lib/saneamiento/__tests__/` |
| **FE-A** | PS-1: `BotonSanear`, etiqueta, filtros, `BarraSeleccionTablero`, aviso en «Crear trámite» | FE-0 | `BotonSanear.test.tsx`, `BarraSeleccionTablero.test.tsx`, `TableroLote.test.tsx` (casos nuevos), `filtrosTablero.test.ts` |
| **FE-B** | PS-2: `PanelDeudaSaneamiento`, descargas, CTA en `PanelDictamen` | FE-0 | `PanelDeudaSaneamiento.test.tsx`, `PanelDictamen.test.tsx` (caso nuevo) |
| **FE-C** | PS-3: pestañas del inicio, `ListadoVehiculos`, `FiltrosVehiculos` | FE-0; reutiliza `BotonSanear` de FE-A y el panel de FE-B (se integra al final) | `ListadoVehiculos.test.tsx`, `FiltrosVehiculos.test.tsx`, test de la página de inicio |
| **FE-D** | PS-4 y PS-5, entrada de menú con insignia, etiqueta en `GrillaBackoffice`, campana | FE-0; reutiliza la sección de deuda de FE-B | `BandejaSaneamientos.test.tsx`, `GestionSaneamiento.test.tsx`, `mensajes.test.ts`, `DiagnosticoShell.test.tsx` |

Orden: **FE-0**, luego **FE-A ‖ FE-B**, luego **FE-C ‖ FE-D**. El front trabaja contra el contrato de la sección 6 con respuestas simuladas; la integración real se prueba en la Fase 3.

Si un carril supera 35 archivos o 3 módulos, se parte antes de lanzarlo.

### 9.5 Fase 3: integración y cierre

| Paso | Quién | Salida |
|---|---|---|
| Integración de carriles en la rama y prueba de punta a punta en local (`scripts/dev/levantar-diagnostico-local.sh`) | hilo principal | Recorrido: marcar → tomar → renglón con PDF → cerrar → descarga del cliente |
| Lint y formato **solo del diff** | backend-agent, frontend-agent | |
| Evidencias PASO 6 por AC en `Custom.Evidences` | skill `dev-tester` | Un bloque por AC |
| Auditoría visual (modo auditoría) | ui-agent | Veredicto sobre PS-1 a PS-5 |
| Seguridad: SAST, SCA, secretos, Ley 1581 | security-agent | Foco en subida de archivos, entre compañías y exportaciones |
| Revisión formal | code-review-agent | 7 dimensiones |
| E2E Playwright locales y revisión a 360 px y accesibilidad | qa-agent | Specs nuevos: `saneamiento-cliente.spec.ts`, `saneamiento-backoffice.spec.ts`, `vehiculos.spec.ts` |
| Aceptación contra el requerimiento | po-agent (modo aceptación) | Contraste con S1–S29, no solo con los AC |

Suite completa local: solo si el usuario la pide. Lo normal es dejarla al CI.

### 9.6 Fase 4: PR y merge (gates humanos)

1. Push y PR a `develop` **cuando #14561 esté mergeado** (S24). Si el rediseño cambia en revisión, se trae `develop` a esta rama antes del PR.
2. **Tamaño:** superará las 800 líneas. Propuesta: **dos PR apilados**, backend y frontend. La decisión se toma al abrir el PR, con el diff real.
3. Reviewer humano real, CI verde, cero hilos abiertos.
4. Merge por el LT. Luego `Resolved` con evidencia de merge.

---

## 10. Azure DevOps

Proyecto **FLIT - DIAGNOSTICO**. Se crea tras aprobar el plan y con confirmación explícita.

| Ítem | Contenido |
|---|---|
| **Feature** «[DIAGNOSTICO] - Saneamiento de vehículos: solicitud, gestión FLIT y panel de deuda» | Padre: épica #12679. Relacionado: #12678, #12881. Criterios funcionales = sección 1 y 3 |
| **HU BACKEND** | Secciones 3, 4, 5, 6 y 8. AC en Gherkin por regla de 3.1–3.3, por endpoint y por los casos de 9.4 |
| **HU FRONTEND** | Sección 7. AC por pantalla PS-1 a PS-5, estados, accesibilidad y 360 px |
| **Comentario en la épica #12679** | «Ajustes 2026-11»: el saneamiento vive en Diagnóstico como circuito de gestión; la bandera es libre en ambos modos; reemplaza el punto del panel informativo. Prevalece sobre el texto anterior |
| **Comentario en la épica #12678** | El rótulo queda «Bloqueado»; el saneamiento no modifica el dictamen |

Pendientes de proceso, los mismos de #14561:

- El proyecto no tiene tag `DOR` y el PAT no puede crearlo. Activar sin tag requiere la misma excepción explícita del usuario.
- Sprint: el siguiente al activo, o la raíz si se repite la excepción D12.
- Story Points: propongo **8 y 8** con el desglose real en la descripción (estimación real: backend ~30, frontend ~35), igual que en #14561. Si se prefiere respetar el tope de 8 de verdad, son unas 9 HUs; se decide al crear.

---

## 11. Riesgos

| # | Riesgo | Mitigación |
|---|---|---|
| R1 | La rama base (#14561) cambia en revisión | Traer `develop` antes del PR; los carriles tocan pocos archivos del rediseño (lista en 8 y 7) |
| R2 | Bandeja entre compañías: fuga de datos | Solo operación FLIT + permiso; consultas solo en `Modulos/Backoffice`; `AislamientoSaneamientoTests`; auditoría por empresa |
| R3 | Subida de archivos | Firma `%PDF`, tipo, tope, nombre saneado, descarga como adjunto, sin procesar el contenido. Revisión de seguridad antes y después |
| R4 | La bandera libre llena la bandeja de FLIT | Tope de 500 por acción, sin «todo el lote», cierre `NO_PROCEDE`, bandeja ordenada por antigüedad, Excel |
| R5 | El cliente ve «Saneado» y «Bloqueado» a la vez | Texto fijo en el panel (7.3) |
| R6 | Evidencias huérfanas en el almacén | Aceptado y anotado en el ADR |
| R7 | `HallazgosSaneamiento` diverge de las reglas del dictamen | Reutiliza los mismos lectores de hechos; prueba cruzada: con los mismos hechos, toda regla SIMIT/SOAT/RTM/IMPUESTO que dispara tiene su renglón |
| R8 | Insignia en `@flit/shell` afecta a otros productos | Campo opcional; sin `badge` el dock se pinta igual. Prueba del shell |
| R9 | PR muy grande | Dos PR apilados (9.6) |
| R10 | Datos personales en PDF y Excel que salen del sistema | Mismo permiso y mismos datos que ya entrega el producto (S18); revisión Ley 1581 del security-agent |

---

## 12. Supuestos por verificar en la Fase 0

Si alguno falla, se corrige el plan antes de lanzar carriles.

| # | Supuesto | Dónde mirar |
|---|---|---|
| V1 | `HechosDictamen` expone, por persona del SIMIT, lo necesario para «hay deuda» (comparendos, paz y salvo, acuerdos), y para SOAT, RTM e impuesto, sin depender del tipo de trámite | `Domain/Dictamen/HechosDictamen.cs`, `Modulos/Dictamen/LectorHechosDictamen.cs:110` |
| V2 | Los hechos se pueden leer para un vehículo en modo Simple (sin pasar por `EmisorDictamen`) | `LectorHechosDictamen` |
| V3 | Valores exactos de `VehiculoDiagnostico.EstadoConsultas` y del modo del diagnóstico, para los filtros de 6.6 | `Modulos/Vehiculos/VehiculoDiagnostico.cs:73` |
| V4 | El mecanismo de `version` del ajuste del dictamen (campo en el cuerpo o cabecera) para replicarlo | `Modulos/Backoffice/OverrideDictamenEndpoints.cs` |
| V5 | De dónde obtiene el front el nombre de las empresas para la bandeja | `components/backoffice/SelectorEmpresaBackoffice.tsx` |
| V6 | El filtro `SinBackoffice` es reutilizable fuera de `DerivacionEndpoints` | `Modulos/Derivacion/DerivacionEndpoints.cs:37` |
| V7 | `AuditoriaLecturaBackoffice.RegistrarAsync` se puede invocar desde un endpoint, no solo desde el filtro | `Modulos/Comun/AuditoriaLecturaBackoffice.cs:17` |
| V8 | `EscritorNotificaciones.AgregarAsync` admite un `diagnosticoId` y `referenciaId` distintos por aviso, como requiere la agrupación | `Modulos/Notificaciones/EscritorNotificaciones.cs:28` |
| V9 | El dock de `@flit/shell` permite pintar un contador junto al ítem sin rediseño | `packages/shell/src/dock/Dock.tsx:135` |
| V10 | Qué toca el reproceso además de celdas y dictamen, para confirmar que no pisa la columna nueva | `Modulos/Consultas/EndpointsReproceso.cs`, `Reproceso.cs` |
| V11 | Nombre real de la tabla de `VehiculoDiagnostico` y su configuración EF | `Persistence/Configuraciones/` |
| V12 | ADR-0087 sigue libre | `docs/decisions/` |

---

## 13. Definición de terminado

- Todos los AC de las dos HUs con evidencia TDD (rojo y verde) publicada.
- Recorrido de punta a punta probado en local con los tres actores: usuario de compañía, admin de compañía y SuperAdmin FLIT.
- OpenAPI 1.4.0 coherente con el código; prueba de contrato en verde.
- Seguridad, revisión de código y auditoría visual sin bloqueantes abiertos.
- ADR-0087 en Propuesto; `docs/diagnosticos/saneamiento-en-diagnostico.md` actualizado para reflejar el circuito (hoy describe D-08).
- Épicas #12678 y #12679 con su comentario de ajustes.
- Sin `TODO`, `TBD` ni `XXX`. Sin datos personales en logs, pruebas ni comentarios de ADO.
