# ADR-0076: El detalle de cada fuente del Diagnóstico muestra la respuesta completa del rpc, con datos personales, guardada cifrada en reposo

**Fecha**: 2026-10-27
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Decisión de producto del usuario del
2026-10-27 (HU #14087, AC8), tomada tras probar con fuentes reales. Requiere además el aval del PO y de jurídico (ver
«Gates antes del merge»).
**Tags**: arquitectura, backend, frontend, seguridad, Habeas Data, Ley 1581, diagnostico, consultas, RabbitMQ
**Épica / HU**: #12881 · HU #14087 (AC8)
**Supersedes (parcial)**:
- [ADR-0075] (Propuesto), decisión **U1 = b solo en la vuelta**: el resultado que publica core-consultas deja de ir
  minimizado y pasa a ser el `*Response` completo. La ida (`*Request` mínimo) **no cambia**.
- [ADR-0075], **corrección C3** del security-agent: `MinimizadorRespuesta` deja de recortar la vuelta.
- Regla **R-13** del plan v2 (`docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md`), en la parte «nunca el
  payload crudo del proveedor» y «documentos de persona enmascarados» **para el detalle de la celda**. R-13 se conserva
  en lo demás (quién ve el detalle, según `visibilidad_usuario`).

**Enmienda** [ADR-0072] (Propuesto) en el control «Enmascarado en las respuestas (…), también en el detalle de celda
(R-13)»: el bloque `respuesta_fuente` del detalle es la excepción (ver «Qué cambia en otros ADR y reglas»).
**Se mantiene sin cambios:** R-20 (el cliente no ve el origen manual), R-18 en lo que toca a `payload`, la ida de U1 = b,
el contrato de `payload` y lo que muestran la grilla, el listado, el export y el PDF.
**Estado de la implementación**: en curso en `feature/AB-12881-diagnostico-d0-d2` por otros agentes, sin PR. Este ADR
documenta la decisión; no la implementa.

## Contexto

Con ADR-0075, core-consultas proyecta cada `*Response` a una lista permitida por rpc (`MinimizadorRespuesta`, C3) y
core-diagnostico guarda en `consultas.payload` solo un resultado normalizado (R-13: «nunca el payload crudo del
proveedor»). El detalle de la celda (`obtenerDetalleCelda`) muestra ese normalizado, con documentos enmascarados.

Al probar con fuentes reales, el usuario concluyó que ese detalle no alcanza para que el analista entienda el
resultado: faltan el histórico de propietarios, los acreedores de la prenda, los demandantes de una medida cautelar, los
datos de contacto y el resto de lo que devuelve la fuente. La HU #14087 (AC8) pide que el detalle de cada fuente
muestre **la respuesta completa del rpc, incluidos los datos personales** (nombres, direcciones, teléfonos, correos,
histórico de propietarios, acreedores y demandantes).

Restricciones:

- core-diagnostico no tiene adaptadores de proveedor y no llama a las fuentes por gRPC (ADR-0075, acta §3.5): lo que
  muestre tiene que llegar por el resultado del bus.
- core-consultas no guarda los resultados de diagnóstico; un claim-check exigiría una tabla nueva y acoplaría el cierre
  de la celda a consultas (descartado en ADR-0075, Q2).
- Cualquier usuario de servicio del vhost `flit` puede atar una cola a `flit.consultas` (M4 sin aplicar).
- Ley 1581 de 2012: finalidad, libertad, acceso y circulación restringida, seguridad y confidencialidad (art. 4); el
  dato de **terceros** (propietarios anteriores, acreedores, demandantes) no viene de una relación con el cliente que
  pide el diagnóstico.

## Decisión

**Opción A: la respuesta completa del rpc viaja por el bus y core-diagnostico la guarda cifrada en reposo, aparte del
`payload`, y la expone solo en el detalle de la celda.**

1. **Vuelta.** core-consultas publica en `consultas.resultado.diagnostico` el `*Response` **completo**, en proto-JSON con
   los nombres del `.proto` y los valores por defecto (`JsonFormatter` con `PreserveProtoFieldNames` y
   `FormatDefaultValues`). La consulta al proveedor sigue con `incluir_respuesta_cruda = false`: el campo
   `respuesta_cruda` (cuerpo tal como lo entrega el proveedor) **no se pide ni viaja**.
2. **Ida.** Sin cambios: el `*Request` lleva solo placa o VIN y el documento cuando la fuente lo exige (U1 = b de
   ADR-0075, ida).
3. **Persistencia.** core-diagnostico guarda esa respuesta **cifrada con ASP.NET DataProtection** (el mismo anillo de
   llaves de `diagnostico.data_protection_keys`, protegido con certificado fuera de la base, con un *purpose* propio) en
   **columnas nuevas nullable**: una en la consulta de vehículo y otra en la de persona, comentadas `@pii:high`.
   `consultas.payload` **no cambia** (sigue siendo el normalizado) y sigue alimentando reglas, veredicto, grilla, export
   y PDF.
4. **Exposición.** `obtenerDetalleCelda` devuelve la respuesta descifrada como `respuesta_fuente`. Es el **único** lugar
   donde sale. No entra en la grilla, el listado, el export, el PDF, el prompt del LLM, eventos ni logs.
5. **R-20.** El detalle no entrega el origen manual. Cuando no hay respuesta (celda manual, consulta anterior a esta
   decisión, error, copia sin respuesta), el detalle muestra **un único mensaje**, igual para todos los casos.
6. **Acceso.** El mismo de R-13: permiso `diagnostico.diagnosticos.ver` con `visibilidad_usuario`, filtro por empresa
   (ADR-0072) y lectura del backoffice auditada en `lecturas_backoffice` (ADR-0073).

## Alternativas consideradas

### Opción A: respuesta completa con PII, cifrada en reposo (elegida por el usuario)

**Pros:**
- Cumple el AC8 al pie de la letra: el analista ve todo lo que entregó la fuente.
- No cambia `payload` ni las reglas: el riesgo funcional queda en una lectura nueva y aislada.
- Reutiliza el cifrado de documentos que ya está conectado (HU #13751), sin dependencia nueva.
- Las columnas separadas y nullable permiten **suprimir o caducar** la respuesta sin tocar FK ni historiales de solo
  inserción (vía que ADR-0072 no tenía para el documento).
- Elimina el mantenimiento de la lista permitida por rpc en `MinimizadorRespuesta`.

**Cons:**
- **Rompe el principio de minimización** que sostenía U1 = b y C3: entran a diagnóstico datos que ninguna regla usa.
- Trae datos de **terceros** (propietarios anteriores, acreedores, demandantes) que no tienen relación con el cliente.
- El bus pasa a llevar nombres, direcciones, teléfonos y correos **en claro** (hasta 24 h en las colas, 7 días en la
  DLQ y 24 h en `consultas.outbox`), legibles por cualquier usuario de servicio del vhost mientras M4 no se aplique.
- Mensajes más grandes en el broker y filas más pesadas en `consultas`.
- El detalle queda atado a la forma del proto: un campo nuevo aparece en pantalla sin revisión.

**Esfuerzo:** M
**Riesgos:**
- Hallazgo de Habeas Data por finalidad, minimización y datos de terceros; puede bloquear la liberación a clientes.
- Inferir el origen manual (R-20) por la ausencia de respuesta. Mitigado con el mensaje único (Decisión 5).
- Fuga por logs o por el manejador de errores si se serializa la respuesta.

### Opción B: respuesta completa sin PII de personas naturales

**Pros:**
- El analista ve el vehículo, las limitaciones, las prendas y los estados completos, sin nombres ni contacto de
  personas naturales.
- Mantiene la minimización en lo que más pesa frente a la Ley 1581 (datos de terceros identificables).
- Reutiliza la guarda por `Descriptor` de C3, cambiando la lista permitida por una lista de campos PII a quitar.

**Cons:**
- **No cumple el AC8** tal como lo pidió el usuario (nombres, histórico de propietarios, acreedores, demandantes).
- Distinguir persona natural de jurídica por campo no es confiable: un acreedor puede ser una u otra y el proto no
  siempre trae el tipo de documento junto al nombre. Habría que clasificar por registro, no por campo.
- Mantiene el costo de clasificar cada campo nuevo del proto.
- Las claves `Campo` con texto libre (`valor_json`, `valor_texto`) pueden llevar PII embebida que el filtro no ve.

**Esfuerzo:** M
**Riesgos:** filtro incompleto que deja pasar PII creyendo que no la hay; insatisfacción de producto.

### Opción C: mantener la minimización (ADR-0075 U1 = b, C3 y R-13 sin cambios)

**Pros:**
- Cero cambio: la revisión de Habeas Data de ADR-0075 sigue valiendo.
- Menor exposición en el bus y en la base.
- Lo que se ve es exactamente lo que se dictamina.

**Cons:**
- **No cumple el AC8**: el usuario ya comprobó con fuentes reales que el detalle no alcanza.
- Cada campo que se quiera ver exige ampliar la lista permitida y su prueba, rpc por rpc.

**Esfuerzo:** S
**Riesgos:** el analista consulta la fuente por fuera de FLIT para completar el dato, con menos control que dentro.

**Descartada además:** pedir la respuesta a core-consultas bajo demanda al abrir el detalle (gRPC). core-consultas no
guarda los resultados de diagnóstico, así que reconsultaría al proveedor (costo por consulta y un dato distinto del
dictaminado) o necesitaría una tabla nueva, y diagnóstico volvería a depender de consultas en línea (ADR-0075, Q2).

## Tradeoff aceptado

Se elige la **opción A** porque es la única que cumple el AC8 y la decisión del usuario, y porque aísla el riesgo: la
respuesta completa vive en columnas propias, cifradas y separadas del `payload`, y sale por un solo endpoint con el
mismo control de acceso que ya tiene el detalle. A cambio se acepta:

- renunciar a la minimización de la vuelta (U1 = b y C3) y tratar datos de terceros, **condicionado** al aval de
  jurídico sobre la base de legitimación y al aval del PO sobre la finalidad (ver «Gates antes del merge»);
- ampliar el riesgo aceptado del bus de ADR-0075 de «identificadores y resultado minimizado» a «respuesta completa con
  nombres, direcciones y contacto», con la misma retención (24 h colas, 7 días DLQ, 24 h outbox);
- que el detalle refleje la forma del proto sin curaduría.

Si jurídico o el security-agent no avalan la opción A, la salida natural es la **opción B** (sin PII de personas
naturales) o el **cifrado de campo del bus** (opción 3 de ADR-0075) como v2 del contrato, que quita el dato en claro del
broker sin cambiar la persistencia de esta decisión.

## Qué cambia en otros ADR y reglas

| Origen | Antes | Con este ADR |
|---|---|---|
| ADR-0075, U1 = b (vuelta) | `*Response` proyectado a la lista permitida, sin nombres ni direcciones | `*Response` completo, con PII, en claro en el bus. La ida no cambia |
| ADR-0075, C3 | `MinimizadorRespuesta` recorta la vuelta; la guarda por `Descriptor` falla si un campo no está clasificado | Ya no recorta. Se recomienda conservar la guarda reorientada a **inventario de campos PII** (sirve para logs y para el registro de tratamiento); lo decide el backend-agent |
| ADR-0075, «Registro del riesgo aceptado U1» | Vuelta: solo los campos de la tabla por rpc | Vuelta: el `*Response` completo salvo `respuesta_cruda`. El resto del registro (dónde, cuánto, quién) se mantiene |
| ADR-0072, control de enmascarado | Documentos «****» + 4 últimos, también en el detalle de celda | Se mantiene en la grilla, el listado y el resto del detalle. **Excepción:** `respuesta_fuente` va sin enmascarar (ver duda abierta 4) |
| ADR-0072, R-2 | El documento no va en eventos del outbox (salvo la excepción de ADR-0075) | Sin cambio de alcance: la excepción de ADR-0075 ya cubre `consultas.resultado.diagnostico`; ahora lleva más datos |
| Plan v2, R-13 | Detalle = normalizado, documentos enmascarados, nunca el payload crudo | Detalle = normalizado **más** `respuesta_fuente`. Sigue sin pedirse el cuerpo crudo del proveedor (`incluir_respuesta_cruda = false`) |
| Plan v2, R-20 | El cliente no ve el origen manual | **Sin cambio.** Refuerzo: mensaje único ante cualquier ausencia de respuesta |
| Plan v2, R-18 | No se borra nada | Sin cambio para `payload`. Para la respuesta completa queda abierta una retención propia (duda abierta 3) |

## Consecuencias

### Lo que se gana
- El analista entiende el resultado de cada fuente sin salir de FLIT.
- Las reglas, el veredicto, el export y el PDF no cambian: el riesgo funcional es bajo.
- Una vía técnica de supresión o caducidad de la respuesta (poner las columnas en `NULL`) sin romper historiales ni FK.

### Lo que se pierde
- La minimización de la vuelta como control compensatorio del riesgo U1 de ADR-0075.
- La garantía de que diagnóstico solo guarda lo que dictamina.
- La revisión del security-agent sobre ADR-0075 deja de cubrir la vuelta: hay que repetirla.

### Obligaciones de Ley 1581 / Habeas Data

Son condición para liberar a clientes; ninguna se da por cumplida con este ADR.

1. **Finalidad (art. 4 b).** Definir por escrito la finalidad del tratamiento de la respuesta completa (p. ej. «permitir
   al analista del cliente verificar el resultado de la fuente para el diagnóstico de ese vehículo») y que el uso no la
   exceda: nada de reutilizarla en otros diagnósticos, reportes, volumetría ni entrenamiento o prompt del LLM.
2. **Libertad y autorización (art. 4 c, art. 9 y art. 10).** Jurídico define la base de legitimación para los datos de
   **terceros**: autorización del titular, o alguna excepción del art. 10 (p. ej. dato de naturaleza pública en
   registros como el RUNT o procesos judiciales). Define también el rol de FLIT frente al cliente (responsable o
   encargado) y si el contrato o los términos del producto tienen que cambiar.
3. **Minimización.** La opción A la reduce de forma deliberada. Lo que queda: la ida sigue mínima, no se pide
   `respuesta_cruda`, y la respuesta completa no sale del detalle de la celda.
4. **Retención y borrado.** `payload` sigue bajo R-18 (indefinido). Para la respuesta completa se propone un plazo
   propio con caducidad que ponga las columnas en `NULL` (duda abierta 3). La supresión pedida por un titular se atiende
   igual: borrar la respuesta de las consultas donde aparece. Sin esa vía, el derecho de supresión de terceros no tiene
   salida.
5. **Acceso y circulación restringida (art. 4 f).** Solo quien puede ver ese diagnóstico según `visibilidad_usuario`,
   dentro de su empresa (ADR-0072). El backoffice FLIT, con lectura auditada (ADR-0073). La vista de la empresa padre
   sobre sus hijas (D8) queda abierta (duda abierta 5).
6. **Seguridad en reposo (art. 4 g).** DataProtection con *purpose* propio y el anillo protegido con certificado fuera
   de la base (fail-closed). Quien lea la base no lee la respuesta. La caducidad o el borrado criptográfico de ADR-0072
   aplican igual.
7. **PII en claro sobre el bus interno.** La respuesta completa viaja **en claro** por RabbitMQ y queda en las colas
   (24 h), la DLQ (7 días) y `consultas.outbox` (24 h). Es una ampliación del riesgo aceptado de U1 de ADR-0075 y la
   acepta el Líder Técnico al pasar este ADR a Aceptado. Se recomienda aplicar **M4** (lectura por exchange por servicio)
   antes de liberar, o el cifrado de campo como v2 del contrato si el security-agent o jurídico lo exigen.
8. **Logs sin PII.** Ni la respuesta, ni fragmentos, ni el JSON en excepciones, ni en el manejador de errores, ni en
   trazas de EF. Tampoco en el frontend (consola, telemetría, mensajes de error).
9. **Export, PDF y evidencias no cambian.** Siguen saliendo del normalizado. `respuesta_fuente` no entra al PDF de
   evidencia, al ZIP, al export de la grilla ni a `diagnostico.lote.terminado`.
10. **Respuesta HTTP.** El detalle se sirve con `Cache-Control: no-store` y la UI no la guarda en `localStorage` ni en
    cachés persistentes.

### Riesgo de inferir el origen manual (R-20)

Si solo las celdas consultadas tienen respuesta, la ausencia delata el llenado manual. Mitigación: el detalle muestra
**el mismo mensaje** ante cualquier ausencia (manual, consulta anterior a esta decisión, error definitivo, dato copiado
sin respuesta) y la API devuelve `respuesta_fuente` ausente o `null` sin un motivo. Ningún otro campo del detalle (fecha,
proveedor, `desde_cache`) puede contradecirlo. Riesgo residual: un usuario que sabe que la consulta es reciente y vio
una respuesta en otra celda del mismo lote puede sospechar; se acepta.

### Datos de consultas anteriores

Las consultas cerradas antes de esta decisión no tienen respuesta completa (el resultado llegó minimizado y no se
guardó). **No hay backfill posible** sin reconsultar al proveedor, y reconsultar tiene costo y daría un dato distinto del
dictaminado. Esas celdas muestran el mensaje único hasta que se reprocesen. Las celdas que se copian por dato del mismo
día deben copiar también la respuesta de la consulta de origen (o mostrar el mensaje único); lo define el backend-agent.

### Gates antes del merge

1. **security-agent**: re-revisión de Habeas Data sobre la vuelta completa, el cifrado de las columnas, logs y el
   endpoint de detalle (incluye la re-revisión pendiente de ADR-0075).
2. **Habeas Data**: registro del tratamiento nuevo (finalidad, base de legitimación, retención) en el inventario de la
   compañía.
3. **PO**: aval de la finalidad, del mensaje único y de las dudas abiertas 3 a 5.
4. **Jurídico**: aval de la base de legitimación para datos de terceros y del rol de FLIT.
5. **Líder Técnico**: paso de este ADR a Aceptado y aceptación de la ampliación del riesgo del bus.

### Cambios operacionales
- **Broker.** Mensajes de resultado más grandes. Revisar el tamaño máximo de mensaje y el volumen de la DLQ.
- **Base.** Filas de `consultas` más pesadas; las columnas nuevas no se indexan.
- **Inventario del broker.** La revisión periódica de usuarios y bindings de `flit.consultas` de ADR-0075 gana
  importancia: ahora cualquier binding ajeno ve nombres y contacto.
- **Sin cambios.** Configuración de colas, TTL, DLQ, reconciliador, ida y simulación.

## ADRs relacionados

- [ADR-0075] — consultas del diagnóstico por RabbitMQ. Este ADR supersede parcialmente U1 = b (vuelta) y C3, y amplía
  el «Registro del riesgo aceptado U1».
- [ADR-0072] — aislamiento por empresa y retención. Se enmienda el control de enmascarado para `respuesta_fuente`; el
  cifrado en aplicación y el borrado criptográfico aplican a las columnas nuevas.
- [ADR-0073] — acceso del backoffice. Las lecturas del detalle desde el backoffice se auditan antes de leer.
- [ADR-0064] — outbox y bandeja. Sin cambios.

## Notas para agentes

- **Backend Agent.** core-consultas: publicar el `*Response` completo con `incluir_respuesta_cruda = false`; decidir el
  destino de `MinimizadorRespuesta` (recomendado: conservar la guarda por `Descriptor` como inventario de campos PII).
  core-diagnostico: cifrar con un `IDataProtector` de *purpose* propio, nunca registrar la respuesta en logs ni en
  excepciones, devolver `respuesta_fuente` solo en `obtenerDetalleCelda` y sin motivo de ausencia (R-20). No tocar
  `payload` ni los mapeos.
- **Database Agent.** Dos columnas nullable (consulta y persona), `@pii:high`, sin índice, sin backfill. Comentario de
  columna que cite ADR-0076. Fila de aplicabilidad de ADR-0072 actualizada en el mismo PR.
- **Frontend Agent.** El bloque «Respuesta de la fuente» solo en el detalle; mensaje único ante la ausencia; nada en
  `localStorage`, consola ni telemetría.
- **QA Agent.** Pruebas de: respuesta cifrada en base (no legible en claro), detalle con y sin respuesta (mismo mensaje
  para manual, antigua y error), 403/404 sin visibilidad, export y PDF sin la respuesta, logs sin PII.
- **Security Agent.** Re-revisión de Habeas Data (gate 1). Decidir si M4 o el cifrado de campo del bus son condición
  de liberación.
- **Infra Agent.** Tamaño máximo de mensaje del broker y volumen de la DLQ; inventario de usuarios y bindings.

## Dudas abiertas (PO / jurídico)

1. **Base de legitimación** para los datos de terceros (propietarios anteriores, acreedores, demandantes): autorización
   o excepción del art. 10. ¿Cambian el contrato o los términos con el cliente? (jurídico)
2. **Rol de FLIT** frente al cliente del diagnóstico: responsable o encargado del tratamiento. (jurídico)
3. **Retención de la respuesta completa**: ¿plazo propio con caducidad a `NULL` (p. ej. al cerrar el diagnóstico más N
   días) o indefinida como `payload` (R-18)? (PO + jurídico)
4. **Enmascarado dentro de `respuesta_fuente`**: ¿los números de documento van completos o enmascarados como en el
   resto del detalle? (PO + jurídico)
5. **Empresa padre (D8)**: ¿ve `respuesta_fuente` de los diagnósticos de sus hijas? (PO + jurídico)
6. **Bus**: ¿basta con M4, o se exige el cifrado de campo antes de liberar? (security-agent + LT)

## Referencias externas

- Ley 1581 de 2012 (Colombia), arts. 4 (principios b, c, f, g, h), 9 (autorización) y 10 (casos sin autorización).
- Decreto 1074 de 2015, capítulo 25 (compila el Decreto 1377 de 2013): finalidad, temporalidad y supresión.
- Protocol Buffers — ProtoJSON format: https://protobuf.dev/programming-guides/json/
- ASP.NET Core Data Protection — purpose strings:
  https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/purpose-strings
