# Plan del corte 7 — consulta, override y evento

Fecha: 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan.

El cierre ya deja la fila y la cola redacta después. Nadie puede leer el dictamen, nadie puede reemplazar el veredicto y no sale `diagnostico.dictamen.emitido`. Este corte publica la lectura para la compañía, el override del backoffice FLIT y el evento de cada versión nueva. También deja el panel en el tablero.

## Qué ya está

| Pieza | Dónde | Qué hace hoy |
| --- | --- | --- |
| Fila | `DictamenVehiculo` | Veredicto, alertamientos, versión, vigente, redacción y los tres campos de override. Ningún endpoint los lee. |
| Nacimiento | `EmisorDictamen.EmitirAsync` | Evalúa, deja plantilla y no publica evento. `SiguienteVersion` apaga la vigente y suma uno. |
| Cola | `RedactorEnCola` | Cambia el texto de la fila tomada. No crea versión. Invalida el PDF solo al pasar a `COMPLETADA`. |
| Huella | `ContenidoReporteVehiculo.ParaHuella` | Si hay dictamen vigente, mete `dictamen/veredicto`, `dictamen/estado` y `dictamen/texto`. La descarga compara esa huella y, si no coincide, genera otro PDF. |
| Permiso | `Permisos.DictamenOverride` | `diagnostico.dictamen.override` ya está en el manifiesto. La sesión lo expone como `corregirDictamen`. Nadie lo exige. |
| Evento de referencia | `LoteTerminado.EncolarAsync` | Outbox del mismo contexto, sin guardar. El contrato vive en `contracts/asyncapi/diagnostico-events.v1.yaml`. Hoy solo está `diagnostico.lote.terminado`. |
| Rutas de vehículo | PDF y detalle de celda | Cuelgan de `/diagnosticos/{id}/vehiculos/{vid}/…`, con `diagnostico.diagnosticos.ver` y `AccesoLote`. |
| Escritura de otra empresa | `EscrituraEntreEmpresas` | Solo el módulo Backoffice. `PUT` de llenado manual es el patrón: `/backoffice/…?empresa=`, política de backoffice y `GuardarDeLaEmpresaAsync`. |
| Pantalla | `TableroLote` y `GrillaBackoffice` | Grilla de semáforos, PDF y, en backoffice, llenado. No hay panel de dictamen. `corregirDictamen` no pinta ningún botón. |

## Rutas

El plan v2 escribe `GET /vehiculos/{vid}/dictamen` y el `PUT` en la misma ruta del producto. El código ya no resuelve un vehículo sin su lote, y una escritura de otra empresa solo vive en Backoffice. Este corte usa las rutas que el resto del servicio ya usa.

| Quién | Método y ruta | Permiso |
| --- | --- | --- |
| Compañía del token | `GET /api/v1/diagnostico/diagnosticos/{id}/vehiculos/{vid}/dictamen` | `diagnostico.diagnosticos.ver` |
| Backoffice FLIT | `GET /api/v1/diagnostico/backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen?empresa=` | `diagnostico.backoffice.ver` + `tenant_type=FLIT` |
| Backoffice FLIT | `PUT /api/v1/diagnostico/backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen?empresa=` | los dos anteriores y `diagnostico.dictamen.override` |

No hay `PUT` en la ruta del producto. El admin de la compañía no tiene el permiso. Un `PUT` ahí responde 404.

La lectura de la compañía pasa por `AccesoLote`, igual que la grilla. Otra compañía, un lote que no le toca o un vehículo de otro lote responden 404 `NO_ENCONTRADO`. El backoffice lee y escribe con `EmpresaExplicita`: sin `empresa`, 400 `EMPRESA_REQUERIDA`; la lectura queda auditada antes de tocar la fila.

## Qué devuelve el GET

Una sola vigente. Si el vehículo se ve y no tiene fila vigente, 404 `SIN_DICTAMEN`. El panel lo trata como vacío, no como fallo.

El DTO de la compañía, en snake_case:

| Campo | Origen |
| --- | --- |
| `version` | `DictamenVehiculo.Version` |
| `veredicto` | `VIABLE` o `BLOQUEADO` |
| `estado_redaccion` | `PENDIENTE` o `COMPLETADA` |
| `alertamientos` | El arreglo guardado, en ese orden: `codigo`, `texto`, `efecto`, `fuente` |
| `catalogo_version` | La de la fila |
| `creado_en` | La de la fila |

No salen `analisis_override`, `usuario_override_id`, `override_en`, intentos, lease, `redaccion_disponible_desde`, `tenant_id` ni `xmin`.

El GET del backoffice suma tres campos: `analisis_override`, `usuario_override_id` (uuid, sin nombre) y `override_en`. El resto es el mismo. Sigue sin lease, intentos ni texto de cola.

## Override

El cuerpo es `{ "veredicto": "VIABLE" | "BLOQUEADO", "confirmado": true }`.

| Cuerpo o estado | Respuesta |
| --- | --- |
| `confirmado` distinto de true | 422 `CONFIRMADO_REQUERIDO`. No se escribe. |
| `veredicto` fuera de los dos valores | 422 `VEREDICTO_INVALIDO`. |
| No hay fila vigente | 404 `SIN_DICTAMEN`. |
| El veredicto pedido es el de la vigente | 409 `VEREDICTO_SIN_CAMBIO`. No nace otra versión. |
| Dos overrides a la vez chocan en `ux_dictamen_version` o `ux_dictamen_vigente` | 409 `DICTAMEN_EN_CONFLICTO`. |

No vuelve a evaluar. No llama a `RedactarAsync`. El veredicto nuevo es el del cuerpo. Las reglas de `alertamientos` se conservan (`codigo`, `efecto`, `fuente`, en el mismo orden). El texto de cada una vuelve a la plantilla de `CatalogoReglasDictamen.Filas` para el `tipo_tramite_codigo` de la cabecera. Si esa fila del catálogo no está, se conserva el texto guardado.

La fila nueva sale de `SiguienteVersion`: la vigente pasa a `vigente=false` y la nueva suma uno. Lleva `analisis_override=true`, `usuario_override_id` del `sub` y `override_en` del reloj inyectado. `redactado_por=PLANTILLA`, `estado_redaccion=PENDIENTE`, intentos 0, lease null. `redaccion_disponible_desde` es `EmisorDictamen.DisponibleAlCerrar` con la suspensión viva y `IProveedorRedactor.Configurado`: la cola explica el veredicto nuevo. `catalogo_version` se copia.

La respuesta es el DTO de backoffice de la fila nueva.

El `PUT` no genera PDF, no invalida a mano y no toca el Excel ni el ZIP. La descarga siguiente arma la huella con el veredicto y los textos nuevos; si no coinciden con el PDF vigente, `ServicioEvidencias` lo reemplaza. Cuando la cola pase esa versión a `COMPLETADA`, sigue valiendo la invalidación del corte 6.

## Evento

`diagnostico.dictamen.emitido`, exchange `flit.diagnostico`, mismo sobre que `LoteTerminado`. Se encola en la transacción de quien escribe y no se guarda aparte. Si esa transacción revierte, el evento también.

Sale cuando nace una versión: el cierre de `EmitirAsync` y el override. No sale cuando la cola solo cambia el texto de una versión que ya existía.

`data`, sin propiedades de más:

| Campo | Qué es |
| --- | --- |
| `diagnosticoId` | Lote |
| `vehiculoId` | Vehículo |
| `dictamenId` | Id de la versión que nace |
| `version` | Versión de negocio |
| `veredicto` | `VIABLE` o `BLOQUEADO` |
| `catalogoVersion` | Entero de la fila |
| `modo` | Código del modo de la cabecera |
| `tipoTramiteCodigo` | El de la cabecera |
| `estadoRedaccion` | El de nacimiento, casi siempre `PENDIENTE` |
| `override` | true solo en el `PUT` |

Sin placa, sin VIN, sin documentos, sin nombres y sin el texto de los alertamientos. La empresa va en el sobre. La deduplicación del consumidor es `(vehiculoId, version)`.

## Pantalla

Un panel en el tablero del lote y en la grilla del backoffice. No es una ruta nueva y no entra en el poll de la grilla: al abrirlo pide el GET. La grilla incremental no cambia.

Estados, en los dos sitios: vacío (`SIN_DICTAMEN`), cargando, error (con reintentar) y lleno. En lleno se ven el veredicto, cada alertamiento en el orden del arreglo (código, efecto, fuente y texto) y, si `estado_redaccion` es `PENDIENTE`, el texto «redacción IA pendiente».

La compañía no ve botón de override ni los tres campos de override. En el backoffice, con `corregirDictamen`, el panel ofrece los dos veredictos y una confirmación explícita. Solo esa confirmación manda `confirmado: true`. Sin la capacidad, el botón no se pinta. El foco vuelve al botón que abrió el panel al cerrarlo. El veredicto no se comunica solo con color.

## Qué queda fuera

- Dibujar el párrafo del dictamen en el PDF.
- Regenerar un ZIP ya entregado.
- Marcar las fuentes obligatorias en el alta. Corte 8.
- Un intérprete de `condicion`.
- Activar la épica #12678.
- Cambiar `EvaluadorDictamen`, `LectorHechosDictamen`, el catálogo o el prompt del redactor.
- Una migración. Las columnas del corte 4 alcanzan.
- Volver a emitir el evento cuando la cola completa el texto.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/DictamenEmitido.cs` | Arma el `data` y lo encola, al estilo de `LoteTerminado` |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/EmisorDictamen.cs` | Al agregar la fila, encola el evento. Sigue sin llamar al modelo |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/ConsultaDictamen.cs` | El DTO de la compañía y el de backoffice, y la lectura de la vigente |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/DictamenEndpoints.cs` | El GET de la compañía |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Backoffice/OverrideDictamenEndpoints.cs` | GET y PUT con empresa explícita |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Modulos.cs` | Mapea las dos rutas |
| `contracts/openapi/diagnostico.v1.yaml` | Las tres operaciones y los dos esquemas |
| `contracts/asyncapi/diagnostico-events.v1.yaml` | El canal y el mensaje |
| `contracts/fixtures/bus/dictamen-emitido.v1.json` | Un ejemplo sin datos personales |
| `frontend-diagnostico/components/tablero/PanelDictamen.tsx` | El panel, en el lote y en el backoffice |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Dictamen/ConsultaDictamenTests.cs` | GET, PUT, evento y el DTO sin override |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Contratos/DictamenEmitidoContratoTests.cs` | El payload contra el AsyncAPI |
| `frontend-diagnostico/components/tablero/__tests__/PanelDictamen.test.tsx` | Vacío, carga, error, lleno y el override |

`RedactorEnCola` no gana outbox.

## Pruebas

Primero, sin Postgres, lo que todavía no existe. Afirma:

- El DTO de la compañía no tiene propiedades de override, lease, intentos ni tenant. El de backoffice suma las tres de override y ninguna más.
- El evento de un cierre lleva `override=false` y el de un reemplazo `override=true`. El JSON no tiene placa, VIN, documento ni el texto del alertamiento.
- `confirmado` distinto de true no arma fila. El mismo veredicto no suma versión. Un veredicto distinto apaga la vigente, suma uno, vuelve los textos a la plantilla y deja `PENDIENTE` con la fecha de `DisponibleAlCerrar`.
- `EmitirAsync` y el `PUT` no contienen `RedactarAsync`.

Después, el mismo Postgres de `ConnectionStrings__Core`. Si no hay, en local se omite y en CI falla.

- La compañía lee su vigente y recibe 404 `SIN_DICTAMEN` si no hay fila. Otra compañía recibe 404 `NO_ENCONTRADO`. El JSON de la compañía no trae las claves de override.
- El backoffice sin `empresa` recibe 400. Con `empresa`, lee las tres claves de override. Un usuario de compañía en esa ruta recibe 403.
- Un `PUT` sin `confirmado: true` no inserta. Uno con el otro veredicto deja una sola vigente, la anterior conservada, y un mensaje en la outbox de esa transacción. El admin de la compañía recibe 403.
- `EngancheDictamenTests` y `ColaRedaccionTests` siguen en verde. La cola no publica otro evento al completar.

El formato, con severidad error, solo sobre los `.cs` y `.tsx` del diff. No hay migración: `db-schema-validator` no aplica.

## Listo cuando

- La compañía ve el veredicto y los alertamientos del vehículo, y en `PENDIENTE` lee «redacción IA pendiente».
- El JSON de la compañía no trae override, lease ni intentos.
- Solo el backoffice FLIT, con `confirmado: true`, reemplaza el veredicto. Nace otra versión, la anterior queda guardada y la cola puede redactar el veredicto nuevo.
- Cada versión nueva publica `diagnostico.dictamen.emitido` sin datos personales. Completar el texto no publica otro.
- El panel tiene vacío, carga, error y lleno. Sin `corregirDictamen` no hay botón de override.
- No hay migración, no se dibuja el párrafo en el PDF y no se activa la épica #12678.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.

## Registro de la corrida

Fecha: 2026-11-03. Local, sin Azure DevOps y sin activar la épica #12678. No hay historia nueva.

Corrida sin Postgres en esta máquina (`ConnectionStrings__Core` no alcanzable): las pruebas de integración se omitieron, como dice el plan para local. En CI no se omiten.

- `dotnet test` filtro `ConsultaDictamenTests|DictamenEmitidoContratoTests|EngancheDictamenTests`: 13 correctas, 3 omitidas (lectura de compañía, lectura de backoffice y PUT).
- `dotnet test` filtro `ColaRedaccionTests|EngancheDictamenTests|EscrituraEntreEmpresasSoloEnBackofficeTests|IgnoreQueryFiltersSoloEnBackofficeTests`: 21 correctas, 5 omitidas (las de cola que abren Postgres, incluida la que afirma que completar el texto no publica otro evento).
- Vitest `PanelDictamen.test.tsx`: 6 correctas. En la misma sesión, `TableroLote`, `GrillaSemaforos` y `GrillaBackoffice` quedaron en verde (101 correctas; el caso de foco del panel se ajustó y volvió a pasar).
- ESLint, severidad error, sobre los `.tsx` y `lib/dictamen/api.ts` del corte: sin hallazgos. El proyecto .NET compiló con el estilo de error ya activo. No hay migración.
