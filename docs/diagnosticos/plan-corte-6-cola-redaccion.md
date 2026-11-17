# Plan del corte 6 — cola de redacción

Fecha: 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan.

El cierre ya escribe `diagnostico.dictamenes` y, dentro de esa misma transacción, espera al modelo. El peor caso es el timeout configurado (30 s) por cada intento de contradicción. Este corte saca esa espera de la transacción: el cierre deja la plantilla y la cola redacta después. También crea el índice que el corte 4 aplazó e invalida el PDF vigente cuando el texto pasa a `COMPLETADA`.

## Qué ya está

| Pieza | Dónde | Qué hace hoy |
| --- | --- | --- |
| Fila | `DictamenVehiculo` | `redaccion_intentos`, `redaccion_disponible_desde`, `redaccion_lease_hasta`. Sin índice de cola. |
| Cierre | `EmisorDictamen.EmitirAsync` | Evalúa y llama a `ServicioRedactor.RedactarAsync` antes de soltar la transacción de la celda. |
| Espera | `EmisorDictamen.DisponibleDesde` | Solo un fallo técnico (`Timeout`, `Http5xx`, `RateLimit`, `Red`) deja fecha. Suspensión, sin proveedor o contradicción agotada dejan null. |
| Tope de estados | `ck_dictamenes_estado_redaccion` | Solo `PENDIENTE` o `COMPLETADA`. No hay `ERROR` ni `GENERANDO`. |
| Redactor | `ServicioRedactor` | No cambia el veredicto. Un fallo técnico no se reintenta adentro. |
| Cola de referencia | `ColaEvidencias` + `TrabajadorEvidencias` | `FOR UPDATE SKIP LOCKED`, lease, bandera `Habilitado`. El SQL crudo solo vive en `ColaConsultas.cs` y `ColaEvidencias.cs` (`TomaEntreEmpresasSoloEnColaTests`). |
| PDF | `ServicioEvidencias` | Invalidar es `vigente=false`. La siguiente descarga regenera. La huella no mira el dictamen: `ParaHuella` no tiene claves de dictamen y el PDF no dibuja el párrafo. |

## Disparo del cierre

`EmitirAsync` sigue evaluando en la transacción de la celda. Deja de llamar a `RedactarAsync`.

La fila nace siempre `PLANTILLA` / `PENDIENTE`, con el texto de `CatalogoReglasDictamen.Filas` en cada alertamiento, `redaccion_intentos = 0` y `redaccion_lease_hasta` null. El veredicto sigue siendo el del evaluador.

| Situación al cerrar | `redaccion_disponible_desde` |
| --- | --- |
| `configuracion_empresa.redaccion_ia_suspendida` es true, o no hay fila de configuración y se toma false, o `IProveedorRedactor.Configurado` es false | null. La cola no la toma. |
| Hay proveedor y no está suspendida | El reloj del cierre. La cola puede tomarla enseguida. |

`DisponibleDesde` se queda: la usa la cola después de un fallo técnico, no el cierre.

## Toma

Una transacción corta, el patrón de `ColaEvidencias`. SQL crudo solo en `ColaRedaccion.cs`. `TomaEntreEmpresasSoloEnColaTests` suma ese archivo a la lista permitida. No usa `IgnoreQueryFilters`.

Entra una fila si todo esto se cumple:

- `estado_redaccion = 'PENDIENTE'`
- `vigente`
- `redaccion_disponible_desde` no es null y ya llegó
- `redaccion_lease_hasta` es null o ya venció

`ORDER BY redaccion_disponible_desde, id LIMIT 1 FOR UPDATE SKIP LOCKED`. La tomada pasa a tener `redaccion_lease_hasta = ahora + lease` y `redaccion_intentos` suma uno. Sigue en `PENDIENTE`: el CHECK no admite otro estado. Devuelve id, `tenant_id`, intentos, el lease y nada del texto.

Dos trabajadores no toman la misma. Una fila con lease vigente no se toma. Una con el lease vencido sí: el proceso que la tenía murió.

Después de la toma, el trabajo abre un `DiagnosticoDb` con `EmpresaDelTrabajo` de esa empresa, como `EmpaquetadorZip`. De ahí en adelante el filtro por empresa manda.

## Qué hace el trabajador con la fila

No vuelve a evaluar. El veredicto guardado no se toca. El pedido al redactor se rearma así:

- Veredicto: el de la fila.
- Reglas: `codigo`, `efecto` y `fuente` de `alertamientos`, en ese orden. `monto_pesos` solo en `IMPUESTO`, leído de la celda de impuesto si está `COMPLETADO`; si no, null.
- Bien: `EmisorDictamen.BienDe` con el detalle RUNT y el vehículo de ahora. Sin documentos ni nombres.
- `RedaccionSuspendida`: el valor vivo de `configuracion_empresa`. Si no hay fila, false.

| Al tomar | Qué pasa |
| --- | --- |
| Suspendida, o el proveedor ya no está configurado | No llama al modelo. `redaccion_disponible_desde` queda null y el lease también. Sigue `PENDIENTE`. |
| `LLM` / `COMPLETADA` | El texto del modelo reemplaza el de cada alertamiento. `redactado_por = LLM`, `estado_redaccion = COMPLETADA`, disponible y lease quedan null. |
| Fallo técnico | Sigue `PENDIENTE` con la plantilla. `redaccion_disponible_desde` es `DisponibleDesde`. El lease queda null. |
| Contradicción agotada, `NoConfigurado` o `Ilegible` | Sigue `PENDIENTE` con la plantilla. Disponible y lease quedan null. No se reintenta. |
| `redaccion_intentos` pasa de `MaxReintentos` | Igual que el fallo técnico, pero disponible queda null. No hay estado `ERROR`. |

El `UPDATE` de cierre exige el mismo `redaccion_lease_hasta` que devolvió la toma. Si otro proceso ya la recuperó, el texto nuevo se descarta.

## Índice

Una migración, generada por EF, solo con el índice. No se edita `D6_TablasDictamen`. Nombre sugerido del comando: `D6_ColaRedaccion`.

```sql
CREATE INDEX ix_redaccion_pendiente ON diagnostico.dictamenes (redaccion_disponible_desde)
  WHERE estado_redaccion = 'PENDIENTE' AND vigente;
```

No empieza por `tenant_id`. Es la tercera lectura entre empresas, junto al reconciliador de consultas y `ix_evidencias_cola`. Antes de la migración, ADR-0072 (sigue en `Propuesto`) deja de decir que la del ZIP es la última y nombra este índice, con el mismo tradeoff: el trabajador reclama filas de todas las empresas y después fija la empresa. ADR-0077 anota que el índice ya existe. `db-schema-validator` tiene que ver esa excepción en el ADR; si no, A11 lo bloquea.

## PDF

Cuando la fila pasa a `COMPLETADA`, en esa misma transacción de la empresa, el PDF vigente de ese vehículo (`tipo = PDF` y `vigente`) pasa a `vigente = false`. No se borra. No se genera otro PDF en el trabajador. No se toca el Excel ni el ZIP.

`ContenidoReporteVehiculo.ParaHuella` suma, si hay dictamen vigente, `dictamen/veredicto`, `dictamen/estado` y `dictamen/texto` (los textos de `alertamientos`, en orden, sin documentos ni nombres). Sin dictamen no agrega claves: la huella de los reportes que ya existen no cambia. El PDF no dibuja el párrafo. La próxima descarga, al no haber vigente, genera otra evidencia; la huella nueva ya no coincide con la de la plantilla.

## Qué queda fuera

- `GET`, `PUT`, el evento `diagnostico.dictamen.emitido` y la pantalla. Corte 7.
- Dibujar el párrafo del dictamen en el PDF.
- Regenerar el ZIP que ya se entregó.
- Marcar las fuentes obligatorias en el alta. Corte 8.
- Un intérprete de `condicion`.
- Activar la épica #12678.
- Cambiar `EvaluadorDictamen`, `LectorHechosDictamen`, el catálogo o el prompt del redactor.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/EmisorDictamen.cs` | El cierre deja la plantilla y la fecha de toma. No llama al modelo. |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Trabajos/ColaRedaccion.cs` | La toma entre empresas |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Trabajos/RedactorEnCola.cs` | Rearma el pedido, llama al redactor, guarda y invalida el PDF |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Trabajos/TrabajadorRedaccion.cs` | `BackgroundService`, el mismo ciclo que `TrabajadorEvidencias` |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Trabajos/OpcionesRedaccion.cs` | `Diagnostico:Redaccion`: `Habilitado` default false, `Intervalo`, `DuracionLease` mínimo 30 s, `MaxReintentos` 0–5 default 2 |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Reportes/ContenidoReporteVehiculo.cs` | Las tres claves de huella |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Persistence/Configuraciones/DictamenVehiculoConfig.cs` | El índice parcial |
| `docs/decisions/ADR-0072-diagnostico-aislamiento-por-tenant.md` | La excepción del índice |
| `docs/decisions/ADR-0077-diagnostico-dictamen-por-reglas.md` | El índice ya no está aplazado |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Aislamiento/TomaEntreEmpresasSoloEnColaTests.cs` | Permite `ColaRedaccion.cs` |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Dictamen/ColaRedaccionTests.cs` | Cierre sin modelo, toma y reintento |

`Modulos.cs` registra el trabajador solo si `Diagnostico:Redaccion:Habilitado` es true. No mira el entorno del host.

## Pruebas

Primero, en `ColaRedaccionTests`, lo que no abre Postgres. Falla porque el tipo todavía no existe. Afirma:

- El cierre con proveedor y sin suspensión deja `PENDIENTE`, `PLANTILLA`, intentos 0 y `redaccion_disponible_desde` igual al reloj. No llama a `RedactarAsync`.
- Suspensión o proveedor apagado dejan `redaccion_disponible_desde` null.
- No es tomable una fila sin fecha, con fecha futura, con lease vigente, no vigente o ya `COMPLETADA`.
- Sí es tomable una `PENDIENTE` vigente con fecha vencida y lease null o vencido.
- Un fallo técnico deja otra fecha y el lease en null. Pasado `MaxReintentos`, la fecha queda null.
- Suspensión al tomar, contradicción agotada o proveedor apagado dejan la fecha en null.
- `COMPLETADA` copia el texto del modelo en cada alertamiento, no cambia el veredicto y marca el PDF vigente de ese vehículo como no vigente. Sin PDF, no falla.

Después, el índice, la cola y el trabajador. La toma con `SKIP LOCKED` usa el mismo Postgres de `ConnectionStrings__Core` que `ColaEvidenciasTests`. No se levanta un servidor nuevo. Si no hay Postgres, en local se omite y en CI falla, igual que el resto de la suite.

`EngancheDictamenTests` sigue en verde: el flanco, el pedido, los alertamientos y `DisponibleDesde` no cambian de contrato. La corrida suma `ColaRedaccionTests`, `EngancheDictamenTests` y `TomaEntreEmpresasSoloEnColaTests`.

El formato, con severidad error, solo sobre los `.cs` del diff. `db-schema-validator` sobre la migración del índice.

## Listo cuando

- Cerrar la celda que termina un vehículo de la familia no espera al modelo.
- Una fila con proveedor queda tomable; una suspendida o sin proveedor, no.
- El trabajador completa el texto fuera de la transacción del cierre, o deja la próxima fecha si el fallo es técnico.
- Un segundo proceso no pisa una toma cuyo lease sigue vigente.
- `ix_redaccion_pendiente` existe, ADR-0072 lo nombra y el validador no lo bloquea.
- Al completar, el PDF vigente de ese vehículo deja de serlo. El Excel y el ZIP no se tocan.
- No hay endpoint ni evento nuevos.
- El registro de la corrida queda en este documento, en local, sin Azure DevOps.

## Registro de la corrida

2026-11-04, local, sin Azure DevOps. Postgres `127.0.0.1:5432` (`ConnectionStrings__Core`).

```
dotnet test tests/Flit.Diagnostico.Tests --nologo --filter "FullyQualifiedName~ColaRedaccionTests|FullyQualifiedName~EngancheDictamenTests|FullyQualifiedName~TomaEntreEmpresasSoloEnColaTests|FullyQualifiedName~ArquitecturaReportesTests|FullyQualifiedName~Huella_cambia"
```

Correctas. Con error: 0. Superado: 28. Omitido: 0. Total: 28. Duración: 40 s.

`dotnet format` con `--severity error` sobre los `.cs` del corte. Sin cambios.

### db-schema-validator — `20261104041025_D6_ColaRedaccion`

Migración generada por EF. No se editó `D6_TablasDictamen`. Además del índice, EF emitió el `ALTER` del comentario de `redaccion_intentos` (el texto ya no dice que el índice no existe). `Up` y `Down` revierten las dos cosas.

| Ítem | Resultado | Nota |
| --- | --- | --- |
| A11 | PASS | `ix_redaccion_pendiente (redaccion_disponible_desde)` no empieza por `tenant_id`. La excepción está en ADR-0072: tercera lectura entre empresas, con el reconciliador de consultas y `ix_evidencias_cola`. |
| A12 | PASS | Prefijo `ix_`. |
| A17 | PASS | `Up` crea el índice; `Down` lo borra. |
| A18 | PASS | No duplica un índice existente. |
| B10 | PASS | El SQL de la toma está solo en `ColaRedaccion.cs`. Los literales `PENDIENTE` y `vigente` son constantes del predicado del índice; `ahora` y `lease` van como parámetros. |
| B11 | PASS | `ColaRedaccion` no usa `IgnoreQueryFilters`. |
| Resto de §A y §B | NA | No hay tabla nueva ni repositorio nuevo. |

Veredicto: OK. A11 no bloquea: la excepción está nombrada en ADR-0072, que sigue en Propuesto.
