# Plan — Respuesta cruda de SIMIT en Diagnóstico

> Generado: 2026-10-29. Estado: **borrador para revisión**. Sin código y sin HUs en ADO.
> Alcance: solo las consultas SIMIT que Diagnóstico pide por el bus (`ConsultarMultas`). RUNT, RUES, avalúo, impuesto, Trámites y Comparendos no cambian.

## 1. Objetivo

Que la respuesta que `core-consultas` devuelve a Diagnóstico por SIMIT de vehículo, propietario y locatario **siempre** traiga el cuerpo del proveedor, y que el detalle lo muestre **organizado**, con el mismo renderizador que hoy usa el conductor (RUNT persona): tablas campo/valor, listas como tablas de columnas y objetos anidados plegables.

El semáforo y el listado resumido de comparendos se quedan como están. Este ajuste no cambia qué multa cuenta como pendiente ni el bloqueo de Trámites.

## 2. Cómo se ve hoy y cómo debe verse

Hoy hay dos formas de pintar una consulta en `frontend-diagnostico`:

| Pantalla | Dato que pinta | Componente | Qué hace con el JSON |
|---|---|---|---|
| Vehículo (RUNT y, si hay `respuesta_fuente`, SIMIT del vehículo) | Proto completo del rpc (`respuesta_fuente`) | `RespuestaFuente` | Tablas en español. **Oculta** `respuesta_cruda`, el proveedor, la caché y la auditoría. |
| Conductor (RUNT propietario y locatario) | JSON del proveedor (`respuesta_proveedor`) | `RespuestaProveedor` | Mismas tablas, **sin ocultar claves**. Una sección por persona. Si no hay crudo, aviso y el resultado resumido. |

SIMIT no entra en el segundo modo:

- La solicitud sale con `OpcionesConsulta()` vacío (`ConstructoresSolicitud.cs`, `SolicitudesProto.Multas`). No pide `incluir_respuesta_cruda`.
- `verifik_simit`, `kyverum_fines` y `flit_fines` deserializan el HTTP y no llenan `RawPayload`. Aunque se pidiera el crudo, no habría cuerpo que adjuntar.
- El detalle de persona solo arma `respuesta_proveedor` para RUNT persona y RUES. `FUENTES_RESPUESTA_PROVEEDOR` es `{RUNT_PROPIETARIO, RUNT_LOCATARIO}`.
- La celda del vehículo no expone `respuesta_proveedor`. Aunque el crudo viajara dentro de `respuesta_fuente`, `RespuestaFuente` lo ocultaría.

**Decisión de pantalla:** las tres celdas SIMIT usan `RespuestaProveedor` (el modo del conductor), no el modo filtrado del vehículo. El modo del vehículo escondería justo el cuerpo que se quiere ver.

| Celda | Dónde se pinta |
|---|---|
| `SIMIT_VEHICULO` | Un bloque a nivel de celda, título «Respuesta del proveedor». |
| `SIMIT_PROPIETARIO` y `SIMIT_LOCATARIO` | Un bloque por persona, con rol y documento enmascarado, igual que el conductor. |

Consultas anteriores al cambio, reutilizadas sin crudo o llenadas a mano: el aviso que ya existe («Esta consulta no tiene la respuesta completa del proveedor. Para verla, vuelve a consultar.») y, debajo, el resultado resumido. No se reprocesa solo por desplegar.

## 3. Qué viaja en la respuesta

El rpc sigue siendo `ConsultasService/ConsultarMultas`. El mensaje de resultado del bus sigue siendo `consultas.resultado.diagnostico` con el `ConsultarMultasResponse` completo.

Dentro de `resultado.respuesta_cruda` (proto `RespuestaCruda`):

| Campo | Valor |
|---|---|
| `proveedor` | Clave del adaptador que respondió (`verifik_simit`, `kyverum_fines`, `flit_fines`). |
| `tipo_sujeto` | `vehicle` si la consulta salió por placa; `person` si salió por documento. |
| `clave_sujeto` | Placa o número de documento consultado. |
| `json` | Cuerpo HTTP del proveedor, tal cual, también cuando Verifik responde 404. |

`core-consultas` ya copia `RawPayload` al proto si `incluir_respuesta_cruda` es verdadero (`ConsultarMultasCore` → `ResultadoConsultaMapper.ToProto`). El hueco está en pedir la opción y en que los tres adaptadores llenen `RawPayload`.

### 3.1 Cuerpos de ejemplo (Verifik SIMIT)

Son la forma real que devuelve Verifik. Documento, nombre y placas del ejemplar original se sustituyeron por datos de prueba (`9999900016`, `TST201`, `TST202`). El agente usa estos JSON como fixture de `RawPayload` y como dato de la pantalla. No se commitea el ejemplar con identificadores reales.

El de persona trae una multa con `estadoComparendo` nulo y `estadoCartera` = `Cobro coactivo`, `comparendos` vacío y `cantMultasPagar` = `0`, mientras `totalMultasPagar` = `1`. El de vehículo trae `multas` vacío, `paramEntradaPlaca` = true y `plate` en lugar de `documentNumber`. Los dos JSON de vehículo entregados eran el mismo cuerpo; queda uno.

Persona (`tipo_sujeto` = `person`):

```json
{
  "data": {
    "acuerdosPago": [],
    "cancelada": false,
    "cantAcuerdosPagar": "0",
    "cantMultasPagar": "0",
    "comparendos": [],
    "cursos": [],
    "documentNumber": "9999900016",
    "documentType": "CC",
    "fechaDesdeCancelSuspen": null,
    "fechaHastaSuspencion": null,
    "mensajeValidacionPagoPlaca": null,
    "multas": [
      {
        "cisa": "N",
        "comparendo": false,
        "comparendoElectronico": false,
        "comparendosElectronicos": "N",
        "consecutivoComparendo": null,
        "contieneInfraccionesEmbriaguez": false,
        "contieneInfraccionesHG": false,
        "departamento": "Antioquia",
        "estadoCartera": "Cobro coactivo",
        "estadoComparendo": null,
        "estadoPago": null,
        "fechaCoactivo": "15/03/2017 00:00:00",
        "fechaComparendo": "31/07/2016 00:00:00",
        "fechaHasta": "07/08/2016 00:00:00",
        "fechaNotificacion": null,
        "fechaResolucion": "07/08/2016 00:00:00",
        "idDepartamento": "119",
        "idEstadoCartera": "12",
        "idEstadoComparendo": "0",
        "idOrganismoTransito": "589",
        "idResolucion": "72318935",
        "idTipoResolucion": "1",
        "impreso": false,
        "infracciones": [
          {
            "codigoInfraccion": "C24",
            "descripcionInfraccion": "Conducir motocicleta sin observar las normas establecidas en el presente código.",
            "valorInfraccion": "344728"
          }
        ],
        "infractor": {
          "apellido": "AP**** FI***",
          "idTipoDocumento": "1",
          "nombre": "NO*** FI***",
          "numeroDocumento": "9999900016",
          "tipoDocumento": "Cédula"
        },
        "liquidacionCoactivoParam18": false,
        "liquidarIncumpApParam135": true,
        "nroCoactivo": "070",
        "numeroComparendo": "513",
        "numeroResolucion": "9485",
        "organismoTransito": "Cisneros",
        "permiteSuscribirAp": "N",
        "placa": "TST201",
        "polca": "N",
        "porcentajeDescuentoCapital": "0",
        "porcentajeDescuentoIntereses": "0",
        "poseeCurso": null,
        "proyeccion": [],
        "referenciaPago": null,
        "renuencia": false,
        "tutor": null,
        "valor": "344728",
        "valorDescuentoCapital": "0",
        "valorDescuentoIntereses": null,
        "valorDescuentoProntoPago": null,
        "valorGestion": "0",
        "valorIntereses": "577387.1454518614",
        "valorPagar": "922115"
      }
    ],
    "organismoTransitoCancelSuspen": null,
    "paramEntradaPlaca": false,
    "pazSalvo": false,
    "personasMismoDocumento": [],
    "resoluciones": [],
    "suspendida": false,
    "totalAcuerdosPagar": "0",
    "totalAp": "0",
    "totalGeneral": "922115",
    "totalMultas": "922115",
    "totalMultasPagar": "1"
  },
  "signature": {
    "dateTime": "October 29, 2026 2:07 PM",
    "message": "Certified by Verifik.co"
  },
  "id": "EJEMPLO-PERSONA"
}
```

Vehículo (`tipo_sujeto` = `vehicle`):

```json
{
  "data": {
    "acuerdosPago": [],
    "cancelada": false,
    "cantAcuerdosPagar": "0",
    "cantMultasPagar": "0",
    "comparendos": [],
    "cursos": [],
    "fechaDesdeCancelSuspen": null,
    "fechaHastaSuspencion": null,
    "mensajeValidacionPagoPlaca": "¿Está seguro que desea pagar?",
    "multas": [],
    "organismoTransitoCancelSuspen": null,
    "paramEntradaPlaca": true,
    "pazSalvo": false,
    "personasMismoDocumento": [],
    "plate": "TST202",
    "resoluciones": [],
    "suspendida": false,
    "totalAcuerdosPagar": "0",
    "totalAp": "0",
    "totalGeneral": "0",
    "totalMultas": "0",
    "totalMultasPagar": "0"
  },
  "signature": {
    "dateTime": "October 29, 2026 2:08 PM",
    "message": "Certified by Verifik.co"
  },
  "id": "EJEMPLO-VEHICULO"
}
```

Qué debe conservar el implementador al copiarlos a fixtures:

- Raíz con `data`, `signature` (`dateTime`, `message`) e `id`.
- `data.multas[]` con `infracciones[]` e `infractor` anidados. En placa, `multas` puede ir vacío.
- Consulta por persona: `documentType` y `documentNumber`. Consulta por placa: `plate` y `paramEntradaPlaca` = true. No aparecen las dos claves a la vez en estos ejemplos.
- Montos y contadores como texto (`"922115"`, `"0"`), intereses como texto decimal, fechas `dd/MM/yyyy HH:mm:ss`, booleanos y `null`.
- Listas vacías (`acuerdosPago`, `comparendos`, `cursos`, `resoluciones`, `proyeccion`) se pintan como lista vacía, no se omiten.

El payload normalizado de la celda no cambia: `semaforo`, `cantidad_comparendos` y `comparendos[]` (`numero`, `fecha`, `valor`, `estado`, `organismo`). El cuerpo completo se guarda cifrado, como ya se guarda `respuesta_fuente` (celda en el vehículo, por persona en propietario y locatario).

## 4. Datos que envía Diagnóstico (sin cambiar la clave de consulta)

| Fuente del bus | Solicitud | Cambio en `opciones` |
|---|---|---|
| `simit_vehiculo` | `placa`. Si hay propietario del RUNT, también su documento. `tipo_persona` no se envía. | `incluir_respuesta_cruda = true` |
| `simit_propietario` | Un trabajo por propietario: `documento` y `tipo_persona` (`JURIDICA` si es NIT, sin dígito de verificación; si no, `NATURAL`). | Igual |
| `simit_locatario` | Igual que propietario, por cada locatario. | Igual |

Avalúo, impuesto, RUNT y RUES no tocan su constructor. `ConRespuestaCruda()` ya existe y hoy solo lo usan conductor y RUES.

## 5. Trabajo por capa

### T1 — Adaptadores de multas (`Flit.Modules.Consultas`)

`VerifikSimitConsultationProvider`, `KyverumFinesConsultationProvider` y `FlitFinesConsultationProvider` leen el cuerpo como texto y lo adjuntan en `ConsultationResult.RawPayload`, con el mismo patrón que conductor y RUES.

- El 404 de Verifik («sin registros») también guarda el cuerpo. Sin eso no se ve por qué el semáforo quedó verde.
- `Overall`, `Checks` y `HydratedFields` quedan iguales. El filtro de comparendos «Pendiente» no se toca.
- `RawPayload` ya tiene `[JsonIgnore]`. Trámites no lo publica por su API.

### T2 — `core-consultas`

- La simulación de `ConsultarMultas` respeta `incluir_respuesta_cruda` y devuelve los cuerpos de la sección 3.1 (persona con multa en cobro coactivo y vehículo sin multas), más un 404 de Verifik. Placas y documentos de prueba, sin datos reales.
- Prueba de que una consulta de multas con crudo no se sirve desde caché (mismo criterio que el AC2 de la HU #14153). Si hoy no hay caché de multas, el test lo deja fijado.

### T3 — `core-diagnostico`

- `SolicitudesProto.Multas` usa `ConRespuestaCruda()` para las tres fuentes SIMIT.
- `DetalleCeldaDto` gana `respuesta_proveedor` a nivel de celda, con la misma regla que ya tiene la persona: el texto de `resultado.respuesta_cruda.json` parseado como objeto; `null` si falta, no es JSON o no es un objeto; el log no lleva el contenido.
- El armado por persona reutiliza `DetalleCelda.RespuestaProveedor`. Hoy descarta el crudo de SIMIT porque el front no lo pide; al pedirlo, el mismo método lo entrega.
- Reutilizar una consulta del mismo día copia el crudo. Un llenado manual deja `respuesta_proveedor` vacío.
- OpenAPI `contracts/openapi/diagnostico.v1.yaml` alineado con el DTO.

### T4 — `frontend-diagnostico`

- `SIMIT_PROPIETARIO` y `SIMIT_LOCATARIO` entran en `FUENTES_RESPUESTA_PROVEEDOR`. Cada persona pinta `RespuestaProveedor`.
- `SIMIT_VEHICULO` pinta `respuesta_proveedor` de la celda con el mismo componente. Si falta, aviso y `RespuestaFuente` del resultado resumido (el comportamiento actual cuando no hay respuesta completa).
- Etiquetas en español para las claves conocidas de Verifik, Kyverum y la fuente interna. Una clave nueva se muestra con su nombre original, como en el conductor. No se ocultan identificadores ni campos técnicos.
- Estados vacío, carga, error y lleno del detalle se mantienen. WCAG 2.1 AA: el bloque tiene nombre accesible y las tablas tienen encabezados.
- RUNT, RUES y el resto de fuentes no cambian de rama.

### T5 — Datos personales

El cuerpo de SIMIT trae nombre, apellido y documento del infractor. Viaja en claro en la outbox y en las colas, igual que el crudo de RUNT persona (hallazgo H2 de la HU #14153), y se cifra al guardarse.

Antes del merge: enmienda del ADR-0076 en estado **Propuesto** y revisión del security-agent solo sobre el delta. Los avales de PO y jurídico siguen siendo de una persona, como en #14153.

## 6. Contrato que se fija antes de implementar

`DetalleCeldaDto.respuesta_proveedor`: JSON opcional, misma semántica que `PersonaDetalleDto.respuesta_proveedor`.

- Presente y objeto: el front lo pinta con `RespuestaProveedor`.
- Ausente o `null`: aviso y resultado resumido.
- Solo se llena en `SIMIT_VEHICULO`. En las celdas de persona sigue viviendo en `personas[].respuesta_proveedor`.

## 7. Pruebas

| Capa | Qué demuestra |
|---|---|
| Adaptadores | El `RawPayload` es el cuerpo HTTP, incluido el 404 de Verifik. El semáforo y los comparendos normalizados no cambian. |
| Diagnóstico | Las tres fuentes SIMIT piden `incluir_respuesta_cruda`. Avalúo, impuesto, RUNT y RUES no. El detalle del vehículo expone `respuesta_proveedor`. La reutilización copia el crudo. El llenado manual no. |
| Front | Vehículo, propietario y locatario renderizan tablas con el JSON de prueba. Sin crudo, se ve el aviso. Otra fuente no entra en ese modo. |

Verificación en el navegador del detalle de las tres celdas (con crudo y sin crudo) antes de dar el front por listo.

## 8. Fuera de alcance

- Cambiar el filtro que solo cuenta un comparendo si `estadoComparendo` o `estadoCartera` es «Pendiente» (`VerifikSimitResultMapper`). Si el crudo muestra que ahí se pierden multas, es otro work item: el mismo adaptador alimenta el semáforo de Trámites.
- Activar `kyverum_fines` en modo real. Mientras esté en mock, un NIT sigue sin multas reales; el crudo del mock sí debe verse.
- SmartCloud y `FuentesComparendosService` (producto Comparendos).
- Reprocesar celdas históricas como parte del despliegue. Quien quiera ver el cuerpo vuelve a consultar esa celda.

## 9. Registro

Dos historias nuevas bajo la Feature de Diagnóstico que ya cubre el detalle de consultas, en el sprint siguiente al activo, con tag `DOR` antes de pasarlas a Active:

| HU | SP | Cubre |
|---|---|---|
| `[BACKEND]` Respuesta cruda de SIMIT en el bus de Diagnóstico | 3 | T1, T2, T3 |
| `[FRONTEND]` Detalle SIMIT con la respuesta del proveedor | 2 | T4 |

No se amplían las HU #14153 y #14154: ya tienen evidencias de RUNT persona y RUES. La activación en Azure DevOps pide confirmación explícita antes de escribir código.

## 10. Roles al implementar

| Rol | Qué entrega |
|---|---|
| UX / UI | El detalle SIMIT usa el mismo orden visual que el conductor: sección con título, tablas y aviso si falta el crudo. Vacío, carga, error y lleno. |
| Dev backend | T1–T3, sin tocar el semáforo ni otras fuentes. |
| Dev frontend | T4, acotado a las tres fuentes SIMIT. |
| Test | Unitarios de los criterios de la sección 7, en la misma sesión que el código. |
| QA | Casos de prueba cuando las HU estén activas. Este plan no los sustituye. |
| Validación formal | Code review y security-agent sobre el delta de datos personales, antes del merge. |
