# Plan — Respuesta íntegra del proveedor en SIMIT (vehículo, propietario y locatario)

> Generado: 2026-10-29. Rama `feature/AB-12881-diagnostico-d0-d2`. Estado: **borrador para revisión** (sin HUs en ADO, sin código).
> Continúa el patrón de las HUs #14153 (backend) y #14154 (frontend), que hicieron lo mismo para RUNT persona y RUES.

## 1. Pedido

Aplicar a las consultas SIMIT de Diagnóstico el mismo cambio que se hizo en RUNT persona y RUES: que el detalle muestre **todo el
JSON de respuesta del proveedor**, sin quitar ningún dato (decisión literal del usuario del 2026-10-28, incluidos identificadores
internos y campos técnicos).

Fuentes afectadas (rpc `ConsultarMultas`):

| Celda | Fuente del bus | Cómo consulta | Proveedor que la atiende |
|---|---|---|---|
| `SIMIT_VEHICULO` | `simit_vehiculo` | Por placa (Verifik recibe solo la placa) | `verifik_simit` |
| `SIMIT_PROPIETARIO` | `simit_propietario` | Por documento, una consulta por persona | `verifik_simit` (natural) · `kyverum_fines` (NIT) · `flit_fines` (compañía con fuente interna) |
| `SIMIT_LOCATARIO` | `simit_locatario` | Igual que propietario | Igual que propietario |

## 2. Diagnóstico del caso reportado

Caso: un vehículo cuyo propietario (persona natural, cédula) **tiene multas en el SIMIT**, pero Diagnóstico no muestra ninguna.
La placa no se versiona en este documento.

### 2.1 Lo que quedó guardado (base local, 2026-10-27 18:35)

| Celda | Proveedor | Intento | Resultado guardado |
|---|---|---|---|
| SIMIT_VEHICULO | Verifik | `SUCCESS`, sin caché, disparo automático | Semáforo **VERDE**, 0 comparendos |
| SIMIT_PROPIETARIO | Verifik | `SUCCESS`, sin caché, disparo automático | Semáforo **VERDE**, 0 comparendos |

El token de Verifik estaba vigente a esa hora (venció el 2026-10-28 07:52). **No es un problema de credenciales**: Verifik
respondió y el sistema interpretó la respuesta como «sin multas».

### 2.2 Caminos del código que convierten multas reales en «sin multas»

| # | Mecanismo | Dónde | Probabilidad |
|---|---|---|---|
| 1 | **Filtro de pendientes.** Solo cuenta un comparendo si `estadoComparendo` es exactamente «Pendiente» o `estadoCartera` es exactamente «Pendiente de pago». Cualquier otro estado (acuerdo de pago, cobro coactivo con otra redacción, «Pendiente Curso», resoluciones con otro texto de cartera, estado nulo) se descarta sin aviso y el semáforo queda verde | `services/core-api/src/Flit.Modules.Consultas/Contracts/VerifikSimitResultMapper.cs:54-68` | **Alta** |
| 2 | **404 = sin registros.** Un 404 de Verifik se traduce a verde «Sin registros SIMIT» | `services/core-api/src/Flit.Modules.Consultas/Providers/VerifikSimitConsultationProvider.cs:74-75` | Media |
| 3 | **SIMIT del vehículo va solo por placa** (ajuste del 2026-10-27): trae los comparendos asociados a la placa, no las multas personales del propietario | `services/core-consultas/src/Flit.Consultas.Api/Grpc/ConsultasGrpcService.cs:122-127` | Explica la celda del vehículo, no la del propietario |
| 4 | **Campos descartados por el adaptador.** De cada multa solo quedan número, fecha, valor, organismo, estado y descripción. Se pierden placa, infractor, departamento, fecha de resolución, código de infracción, cursos, `totalMultasPagar` y `cantMultasPagar` | `VerifikSimitResultMapper.cs:32-81`, `VerifikSimitResponse.cs:37-124` | Afecta lo que se ve, no el conteo |

Otros caminos verificados que hoy **no** aplican al caso (persona natural, compañía con fuente externa):
`kyverum_fines` arranca en mock por defecto (un NIT siempre sale sin multas mientras no se active el modo real) y `flit_fines` en
mock o con 404 también da vacío.

### 2.3 Por qué no se puede confirmar hoy

**Ningún proveedor de multas guarda la respuesta sin procesar** (`RawPayload`: 0 apariciones en `VerifikSimitConsultationProvider`,
`KyverumFinesConsultationProvider` y `FlitFinesConsultationProvider`) y Diagnóstico manda `OpcionesConsulta()` vacío
(`services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Consultas/Bus/ConstructoresSolicitud.cs:39-53`). Con este cambio,
la respuesta completa de Verifik (incluida la del 404) quedará visible y se sabrá cuál de los mecanismos actuó.

## 3. Estado actual del camino (hechos verificados)

| Tramo | Hoy | Qué falta |
|---|---|---|
| Solicitud en core-diagnostico | `SolicitudesProto.Multas` arma el request con `OpcionesConsulta()` vacío para vehículo y persona (`ConstructoresSolicitud.cs:39-53`, `:113-132`, `:248-264`). `ConRespuestaCruda()` ya existe (`:245`) y no se usa en SIMIT | Pedir la respuesta del proveedor en las tres fuentes SIMIT. Avalúo (`:59`) no cambia |
| gRPC de core-consultas | `ConsultarMultasCore` ya propaga `IncluirRespuestaCruda` hasta `ResultadoConsultaMapper.ToProto` (`ConsultasGrpcService.cs:197-205, 231-232`) | Nada |
| Proveedores (core-api, compartidos con Trámites) | Deserializan directo al DTO con `ReadFromJsonAsync`; no llenan `RawPayload` | Leer el cuerpo como texto y adjuntar `RawPayload`, igual que conductor y RUES |
| Caché | No hay caché de multas en core-consultas; solo se lee `DesdeCache` en `EjecutorTrabajo.cs:136` (confianza media) | Confirmarlo con una prueba, como en el AC2 de #14153 |
| Simulación | `Multas(clave)` no recibe ni devuelve la respuesta del proveedor (`SimuladorConsultas.cs:67-69, 129-152`); no hay fixtures de SIMIT | Aceptar la petición y devolver fixtures ficticios |
| Guardado | La respuesta de la fuente ya se guarda cifrada: a nivel de celda en el vehículo y por persona en propietario y locatario | Nada (el crudo viaja dentro del `*Response`) |
| Detalle (API) | `respuesta_proveedor` solo existe en `PersonaDetalleDto`; la celda del vehículo expone `respuesta_fuente` pero no `respuesta_proveedor` (`DetalleCeldaDto.cs:37, 63-64`; `DetalleCelda.cs:73-91, 145-160`) | `respuesta_proveedor` a nivel de celda para `SIMIT_VEHICULO`, reutilizando `DetalleCelda.RespuestaProveedor(...)` |
| Front | `FUENTES_RESPUESTA_PROVEEDOR = {RUNT_PROPIETARIO, RUNT_LOCATARIO}`; solo hay rama por persona (`frontend-diagnostico/lib/tablero/detalle.ts:108-110`, `components/tablero/DetalleCelda.tsx:195, 242`) | Sumar SIMIT_PROPIETARIO y SIMIT_LOCATARIO; rama de celda para SIMIT_VEHICULO |

## 4. Plan de implementación

| # | Tramo | Agente | Trabajo | Criterio de aceptación |
|---|---|---|---|---|
| T1 | Proveedores de multas (core-api `Flit.Modules.Consultas`) | backend-agent | `verifik_simit`, `kyverum_fines` y `flit_fines` llenan `RawPayload` con el cuerpo HTTP tal cual, **también en el 404** de Verifik (para ver qué respondió) | Crudo byte a byte igual al cuerpo; mismo `Overall`, `Checks` y `HydratedFields` que hoy; Trámites no cambia (el `[JsonIgnore]` de `ConsultationResult.RawPayload`, commit `71077958a`, ya impide que salga por su API) |
| T2 | core-consultas | backend-agent | Simulación de multas: acepta `incluir_respuesta_cruda` y devuelve fixtures ficticios con la forma de Verifik SIMIT (multa pendiente, pagada, «Pendiente Curso», cobro coactivo, resolución con `estadoComparendo` nulo). Prueba de que con crudo no hay caché | Fixtures con datos en el rango 99999…, sin PII real |
| T3 | core-diagnostico | backend-agent | `SolicitudesProto.Multas` pide la respuesta del proveedor para `simit_vehiculo`, `simit_propietario` y `simit_locatario`. `DetalleCeldaDto` gana `respuesta_proveedor` a nivel de celda (mismo tratamiento: objeto JSON, `null` si falta o no es JSON, log sin contenido). OpenAPI `contracts/openapi/diagnostico.v1.yaml` | Las demás fuentes sin cambios; reutilización del mismo día copia el crudo; llenado manual lo deja vacío |
| T4 | frontend-diagnostico | frontend-agent | `SIMIT_PROPIETARIO` y `SIMIT_LOCATARIO` entran al modo íntegro por persona; `SIMIT_VEHICULO` lo pinta a nivel de celda con el mismo componente `RespuestaProveedor`. Mismo aviso cuando falta la respuesta | Sin ocultar claves; tablas con la unión de columnas; WCAG 2.1 AA; las demás fuentes sin cambios |
| T5 | Datos personales | architecture-agent + security-agent | Ampliar la enmienda de ADR-0076 (Propuesto): el SIMIT trae datos del infractor (nombre, apellido, documento). Revisión de seguridad solo del delta nuevo | Veredicto del security-agent; los avales de PO y jurídico siguen pendientes, como en #14153 |
| T6 | Prueba | Orquestador | Suite local en real (avalúo simulado), caso reportado: **reprocesar** la celda SIMIT para que vuelva a consultar; revisar la respuesta completa de Verifik y confirmar cuál mecanismo del §2.2 actuó | Respuesta del proveedor visible en las tres celdas |

**Estimación:** 5 SP (backend 3, frontend 2), unos 45 minutos con backend y front en paralelo sobre un contrato fijado de
antemano, y una sola regresión completa al final.

**Contrato que se fijaría antes de lanzar los agentes:** `DetalleCeldaDto.respuesta_proveedor` (`JsonElement?`, opcional al
final), con la misma semántica que `PersonaDetalleDto.respuesta_proveedor`.

## 5. Fuera de alcance (decidir después de ver la respuesta completa)

- **El filtro de «pendientes»** (§2.2, mecanismo 1). Si se confirma que descarta multas reales, cambiarlo altera el semáforo
  de Diagnóstico **y el bloqueo de Trámites**, que usa el mismo adaptador. Es otra decisión, con su propio work item.
- **`kyverum_fines` en mock** para personas con NIT: mientras siga así, un NIT siempre sale sin multas.
- **SmartCloud** (consulta directa al SIMIT, hoy solo en `FuentesComparendosService` del producto Comparendos) como alternativa a
  Verifik en `ConsultarMultas`.

## 6. Decisiones abiertas

| # | Decisión | Opciones | Recomendación |
|---|---|---|---|
| 1 | Registro en ADO | a) dos HUs nuevas bajo la Feature #13720 (backend 3 SP, frontend 2 SP), creadas y activadas con «sí»; b) ampliar #14153 y #14154 (siguen en Active, sin merge) | **a**: no obliga a rehacer las evidencias ya publicadas |
| 2 | Crudo en el 404 de Verifik | a) sí, guardarlo; b) no, solo en 200 | **a**: es la única forma de ver por qué Verifik dijo «sin registros» |
| 3 | Gates de datos personales | Se suman a los de #14153: H2 (crudo en claro en outbox, colas y DLQ) para el Líder Técnico, avales de PO y jurídico | Tratarlos juntos antes del merge |
