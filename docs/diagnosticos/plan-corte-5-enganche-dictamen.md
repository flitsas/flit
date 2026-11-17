# Plan del corte 5 — enganche del dictamen al vehículo terminado

Fecha: 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan.

El catálogo, el evaluador, el lector, el redactor y las tablas ya existen. `diagnostico.dictamenes` está vacía. Este corte escribe la primera fila cuando un vehículo de un lote Con Análisis IA termina, y una versión nueva si vuelve a terminar después de un reproceso. No publica un endpoint, no emite un evento y no toca el PDF.

## Qué ya está

| Pieza | Dónde | Qué hace hoy |
| --- | --- | --- |
| `AvanceVehiculo.Terminado` | `Domain/Diagnosticos/AvanceVehiculo.cs` | Puro. Terminado si hay celdas y todas están en `COMPLETADO` o `NA`. Una en `ERROR`, `PENDIENTE` o `EN_CURSO` no termina. Sin celdas, no termina. |
| Lecturas | `ConsultaGrilla`, `EmisionPdf`, `EmpaquetadorZip` | Solo preguntan si está terminado. No escriben dictamen. |
| Cierre de celda | `PublicadorCeldas.CerrarAsync` | Lo usan el consumidor de resultados, el reconciliador, la reutilización del mismo día y el cierre en `NA`. Exige la transacción de quien llama. Aplica el resultado, y si la RUNT libera celdas las cierra en la misma transacción. |
| Llenado manual | `LlenadoManual.LlenarAsync` | Marca `COMPLETADO` por su cuenta, sin `EscritorCeldas`. Misma transacción y luego `RecalculadorEstado`. |
| Lector | `LectorHechosDictamen.Leer` | Recibe lecturas ya armadas. No descifra. |
| Evaluador | `EvaluadorDictamen.Evaluar` | Familia TRASPASO. Devuelve `VIABLE` o `BLOQUEADO` y las reglas, en orden del catálogo. |
| Redactor | `ServicioRedactor.RedactarAsync` | Explica el veredicto. Si está suspendido, sin proveedor o el modelo falla, devuelve plantilla con `PLANTILLA` / `PENDIENTE`. Un fallo técnico no se reintenta aquí. |
| Interruptor | `configuracion_empresa.redaccion_ia_suspendida` | Lo edita el backoffice. Nadie lo lee al redactar. No viaja en la copia de la cabecera. |
| Tabla | `DictamenVehiculo` | Clase (a). Sin `HasData`. `version` es la versión de negocio. `Xmin` es el token. Índice único parcial `(tenant_id, vehiculo_id) WHERE vigente`. |

## Disparo

Se emite solo en el flanco de subida: el vehículo no estaba terminado antes de cerrar esta celda y sí lo está después.

La foto se toma al entrar al cierre, antes de que este mismo cierre publique celdas anidadas. El «antes» usa el estado anterior de esta celda y el estado actual de las hermanas. El «después» cambia solo esta celda. Así, si la RUNT libera al SIMIT y el SIMIT se cierra adentro, emite el cierre interno —el que completa el conjunto— y el externo no emite otra fila.

`AvanceVehiculo` sigue puro. No se le agrega persistencia.

Entra en dos sitios, dentro de la transacción que ya cerró la celda:

1. Al final de `PublicadorCeldas.CerrarAsync`, después de los efectos y de las celdas liberadas.
2. En `LlenadoManual.LlenarAsync`, después de dejar la celda en `COMPLETADO` y antes del commit.

No entra en la grilla, ni en el PDF, ni en el ZIP.

## Cuándo no hay fila

- El lote es `SIMPLE`, o `tipo_tramite_codigo` es null.
- El código no está en `CatalogoReglasDictamen.TiposTramite`. No se llama al evaluador: lanzaría, y este corte no puede fallar el cierre de la celda por un trámite fuera de la familia.
- El flanco no sube (sigue en curso, queda en error, o ya estaba terminado).
- Reproceso que devuelve una celda a `PENDIENTE`. La fila vigente anterior se queda. La nueva nace cuando el vehículo vuelve a terminar.

Un lote Con Análisis IA de la familia TRASPASO que termina sí escribe fila, aunque ninguna regla dispare. El veredicto es `VIABLE` y `alertamientos` es `[]`.

## Lecturas

`Hoy` es la fecha en hora Colombia (`VigenciaDelDia.OffsetColombia`) del `TimeProvider` del cierre. El tipo de trámite sale de la cabecera.

| Campo de `PedidoLectura` | Celda | Cómo se arma |
| --- | --- | --- |
| `RuntDetalle` | `Fuente.Runt` | `LectorDetalleRunt.Leer` sobre `respuesta_fuente_cifrada`. Sin cifra legible, la lectura va null y el estado de la celda se conserva. |
| `RuntNormalizado` | la misma | `VehiculoRunt` del payload normalizado, si la celda está `COMPLETADO`. |
| `SimitVehiculo` | `Fuente.SimitVehiculo` | `SimitVisible.Completar` sobre el detalle ya proyectado y la respuesta del proveedor. |
| `RuntPropietario` | `Fuente.RuntPropietario` | `LicenciasConductor.Completar` por persona, como `DetalleCelda.Personas`. |
| `SimitPropietario` | `Fuente.SimitPropietario` | `SimitVisible.Completar` por persona. |
| `SimitLocatario` | `Fuente.SimitLocatario` | Igual. No es obligatoria. |
| `RuntLocatario` | `Fuente.RuntLocatario` | Igual. No es obligatoria. |
| `Impuesto` | `Fuente.ImpuestoDepartamental` | `ResultadoImpuestoDetalle`. Si no hay celda, el campo va null. |

El armador no consulta proveedores y no marca fuentes obligatorias. Una celda ausente no se inventa: el lector ya la trata como sin resultado.

## Fila que se guarda

Misma transacción que el cierre. `tenant_id` es el de la celda. `id` es uuid v7 de la aplicación. `catalogo_version` es `CatalogoReglasDictamen.Version` (1). `creado_en` es el reloj del cierre. `analisis_override` queda false. `usuario_override_id` y `override_en` quedan null. `redaccion_lease_hasta` queda null.

| Situación | Versión |
| --- | --- |
| No hay fila vigente de ese vehículo | `version = 1`, `vigente = true` |
| Ya hay una vigente y el flanco vuelve a subir | La anterior pasa a `vigente = false` y la nueva usa `version + 1`, `vigente = true` |

Las dos escrituras van juntas. El índice parcial no admite dos vigentes.

`alertamientos` es un arreglo JSON. Un objeto por regla de `DictamenEvaluado.Reglas`, en ese orden. Claves exactas: `codigo`, `texto`, `efecto`, `fuente`. Sin monto, sin documentos y sin nombres. `efecto` y `fuente` salen de la regla evaluada.

| `estado_redaccion` | `texto` de cada objeto |
| --- | --- |
| `PENDIENTE` | `plantilla_texto` de `CatalogoReglasDictamen.Filas` para ese tipo y ese código. El párrafo único de `PlantillaRedaccion` no se copia en cada ítem: la marca de pendiente es `estado_redaccion`. |
| `COMPLETADA` | El `ResultadoRedaccion.Texto` del modelo, el mismo en todos los ítems. El redactor no parte el texto por código. Quien lo muestre (corte 7) lo muestra una vez. |

El veredicto guardado es el del evaluador. El redactor no lo cambia.

## Redactor

Se llama una vez, en esa misma transacción, después de evaluar.

El pedido lleva el veredicto, las reglas (`codigo`, `fuente`, `efecto`, `monto_pesos`) y el bien. El bien sale del detalle RUNT: placa, clase, marca, línea, modelo y tipo de servicio. Si no hay detalle, la placa y el servicio salen del vehículo; el resto va null. Sin placa, se usa `identificador_norm`. No viajan documentos ni nombres. `RedaccionSuspendida` es el valor vivo de `configuracion_empresa` de esa empresa. Si no hay fila de configuración, se toma como false.

| Respuesta | Fila |
| --- | --- |
| `LLM` / `COMPLETADA` | Así se guarda. `redaccion_intentos` es `ResultadoRedaccion.Intentos`. `redaccion_disponible_desde` queda null. |
| `PLANTILLA` / `PENDIENTE` por suspensión, sin proveedor o por contradicción agotada | `redaccion_disponible_desde` queda null. El corte 6 no debe reintentar un proveedor apagado ni un texto que ya se descartó. |
| `PLANTILLA` / `PENDIENTE` por fallo técnico (`Timeout`, `Http5xx`, `RateLimit`, `Red`) | `redaccion_intentos` es el conteo devuelto. `redaccion_disponible_desde` es el reloj más `ReintentarEn`, o el reloj si el proveedor no dijo espera. El índice de la cola no se crea aquí. |

El peor caso con llave es el timeout ya configurado (30 s) por cada intento de contradicción. Se acepta dentro de la transacción del cierre porque este corte no tiene trabajador. La cola que saque esa espera de la transacción es el corte 6.

Si armar el pedido o evaluar lanza, el cierre de la celda falla y quien lo reintenta (el bus, en el consumidor) vuelve a entrar. No se traga la excepción: un vehículo terminado sin fila no tendría otro disparo.

## Qué queda fuera

- El trabajador de la cola, el índice `ix_redaccion_pendiente` y la invalidación del PDF. Corte 6.
- `GET`, `PUT`, el evento `diagnostico.dictamen.emitido` y la pantalla. Corte 7.
- Marcar las fuentes obligatorias en el alta. Corte 8, después de R-17.
- Un intérprete de `condicion`.
- Activar la épica #12678.
- Otra migración. La tabla del corte 4 alcanza.
- Cambiar `EvaluadorDictamen`, `LectorHechosDictamen`, `ServicioRedactor` o el catálogo. El enganche los llama.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/EmisorDictamen.cs` | Flanco, armado del pedido, alertamientos, versión y la llamada al redactor |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Consultas/Bus/PublicadorCeldas.cs` | Llama al emisor al cerrar |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Consultas/LlenadoManual.cs` | Llama al emisor al llenar |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Dictamen/EngancheDictamenTests.cs` | El comportamiento de este corte |

`PublicadorCeldas` recibe el emisor por constructor. Los tests de cierre que no terminan un Con Análisis IA siguen sin fila. Si alguno ya termina un vehículo de esa familia, la aserción nueva es que existe una fila: no se apaga el emisor para conservar el test viejo.

## Pruebas

Primero `EngancheDictamenTests`, en rojo, antes de `EmisorDictamen`. Falla porque el tipo todavía no existe. Una sola corrida. Afirma:

- No emite si el vehículo ya estaba terminado, si no tiene celdas, o si después del cierre queda una celda fuera de `COMPLETADO` y `NA`.
- Emite cuando el cierre es el que lo termina. El estado anterior de esta celda y el de las hermanas se combinan como dice «Disparo». Un cierre anidado no se simula con base: la función pura del flanco basta.
- `SIMPLE`, tipo null o un código fuera de la familia no arman pedido.
- Con lecturas ya construidas, el pedido trae el tipo, `Hoy` en fecha Colombia y la celda que corresponde. Una celda RUNT `COMPLETADO` sin detalle queda con lectura null.
- `alertamientos` tiene un objeto por regla, con esas cuatro claves. En `PENDIENTE` el texto es la plantilla del catálogo. En `COMPLETADA` es el texto del modelo. Sin reglas, el arreglo es `[]` y el veredicto guardado es `VIABLE`.
- Sin vigente, la versión es 1. Con vigente, la anterior deja de serlo y la nueva suma uno. Los dos resultados salen de la función; el test no abre Postgres.
- El pedido al redactor lleva `RedaccionSuspendida` tal como se le pasa, y el bien no incluye documentos ni nombres. Un fallo técnico deja `redaccion_disponible_desde`; la suspensión lo deja null.

Después, el emisor y las dos llamadas. No se reescriben los tests para que ignoren una fila.

No se levanta un Postgres nuevo. La corrida es el proyecto de tests de Diagnóstico, con filtro `EngancheDictamenTests`, después de un build de ese proyecto y `dotnet test --no-build`. Si el enganche toca el cierre, se suma el filtro de las clases de test de `PublicadorCeldas` y de `LlenadoManual` que ya existan.

El formato, con severidad error, solo sobre los `.cs` del diff.

## Listo cuando

- Un vehículo Con Análisis IA de la familia TRASPASO escribe una fila al pasar a terminado, por el bus o por llenado manual.
- Un segundo cierre, con el vehículo ya terminado, no escribe otra.
- Tras un reproceso, la siguiente vez que termina agrega versión y deja una sola vigente.
- `dictamenes` no gana endpoint, evento ni índice de cola.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.

## Registro de la corrida (local, 2026-11-03)

Sin Azure DevOps y sin historia nueva. El emisor escribe en la transacción de `PublicadorCeldas.CerrarAsync` y de `LlenadoManual.LlenarAsync`. No hay endpoint, evento ni índice de cola.

Rojo, antes del emisor: `dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~EngancheDictamenTests" -p:NuGetAudit=false`. Exit code 1. `CS0246`: `LecturasParaDictamen` no existe.

Verde, con el mismo filtro más `LlenadoManualTests`: 21 correctas, 0 con error, 0 omitidas (7 de enganche y 14 de llenado manual). La corrida de cierre usó el Postgres de `127.0.0.1:5432` con `ConnectionStrings__Core` (`Username=postgres`). Los lotes de llenado son `SIMPLE`: el enganche no tumba el cierre y no escribe fila. No existe una clase de test de `PublicadorCeldas`. El formato, con severidad error, corrió sobre los `.cs` del diff. El restore literal se detiene por NU1903 en `Microsoft.DotNet.ILCompiler` 10.0.8, ajeno a este corte; la corrida usó `-p:NuGetAudit=false` solo en el comando, sin cambiar el csproj.
