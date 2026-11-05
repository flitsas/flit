# Plan del corte 3 — lector de hechos del dictamen

Fecha: 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica #12678, sin historia nueva. Este corte no se implementa hasta confirmar el plan.

El evaluador del corte 2 ya decide el veredicto si alguien le entrega `HechosDictamen`. Este corte construye esos hechos desde las lecturas que el producto ya tiene. No descifra, no consulta proveedores y no guarda el dictamen.

## Qué entra

Una función pura en la API de Diagnóstico:

```text
LectorHechosDictamen.Leer(PedidoLectura) → HechosDictamen
```

`PedidoLectura` trae el tipo de trámite, la fecha de corte `Hoy` y, por cada celda, su `EstadoCelda` más la lectura ya armada. El enganche del corte 5 será quien descifre el RUNT, llame a `SimitVisible` y a `LicenciasConductor.Completar`, y pase aquí el resultado.

| Celda | Lectura que entra | De dónde sale hoy |
| --- | --- | --- |
| RUNT del vehículo | `DetalleRuntReporte` y, si está, `VehiculoRunt` | `LectorDetalleRunt` y el RUNT normalizado de la misma celda |
| SIMIT del vehículo | `ResultadoSimitDetalle` ya completado | `SimitVisible.Completar` |
| RUNT del propietario | lista de `ResultadoRuntPersonaDetalle` ya completada | `LicenciasConductor.Completar` |
| SIMIT del propietario | lista de `ResultadoSimitDetalle` ya completada | `SimitVisible.Completar` por persona |
| SIMIT del locatario | lista de `ResultadoSimitDetalle` | igual que el propietario; no es fuente obligatoria |
| RUNT del locatario | lista de `ResultadoRuntPersonaDetalle` | igual que el propietario; no es fuente obligatoria |
| Impuesto | `ResultadoImpuestoDetalle` o ausencia de celda | `ResultadoImpuesto.MontoTotal` proyectado al detalle |

`Hoy` y el código de trámite los pone quien llama. El lector no los busca en la celda.

## Estado de la celda

| `EstadoCelda` | Lectura | `EstadoFuenteDictamen` | Hechos de esa fuente |
| --- | --- | --- | --- |
| `Completado` | presente | `Disponible` | los de la tabla de abajo |
| `Completado` | null | `SinResultado` | null |
| `Error` | se ignora | `Error` | null |
| `Na` | se ignora | `NoAplica` | null |
| `Pendiente` o `EnCurso` | se ignora | `SinResultado` | null |

El vehículo terminado no tiene celdas pendientes. Si aun así llegan, cuentan como sin resultado y `FUENTE_PENDIENTE` puede disparar. El lector no lanza por ese estado.

El RUNT del vehículo está disponible solo si `DetalleRuntReporte` vino. Si el cifrado no se pudo leer, el detalle es null y la fuente queda en sin resultado aunque el payload normalizado exista. Así un SOAT vacío no bloquea por un fallo de lectura.

## RUNT del vehículo

Con la fuente disponible:

| Hecho | Lectura |
| --- | --- |
| `EstadosSoat` | `DetalleRuntReporte.Soat[].Estado`, en el orden de la lista. Lista vacía si no hay pólizas |
| `EstadosRtm` | `DetalleRuntReporte.Rtm[].Estado`, igual |
| `FechaMatricula` | `dd/MM/yyyy`. Si no trae ese formato, null. La RTM entonces sí se evalúa |
| `EstadoVehiculo` | `General.EstadoVehiculo`, tal cual |
| `TienePrenda` | `General.Prendas` es `SI`, sin importar mayúsculas |
| `TieneGravamenes` | `General.TieneGravamenes` es `SI`, sin importar mayúsculas |
| `Limitaciones` | cantidad de `Limitaciones` |
| `GarantiasMobiliarias` | cantidad de `GarantiasMobiliarias` |
| `Infracciones` | cantidad de `Infracciones` |
| `EsLeasing` | algún propietario de `VehiculoRunt` tiene `TipoPropiedad.Leasing`. Sin vehículo normalizado, false |
| `HayDocumentoLocatario` | algún locatario de `VehiculoRunt` trae número de documento con texto. Sin vehículo normalizado, false |

No se copian placa, documentos ni nombres. Esos campos no están en `HechosRuntVehiculo`.

## SIMIT

Cada lista de personas se resume en un solo `HechosSimit`. Una lista vacía con la celda completada es sin resultado.

| Hecho | Lectura |
| --- | --- |
| `CantidadComparendos` | suma de `CantidadComparendos` |
| `PazYSalvo` | false si alguna persona está en false. true si todas las que informan están en true. null si ninguna informa |
| `Suspendida` | true si alguna está suspendida |
| `Cancelada` | true si alguna está cancelada |
| `Acuerdos` | un `AcuerdoPago` por cada acuerdo de cada persona |

El saldo es `AcuerdoSimitDetalle.Pendiente`, en pesos enteros. Se aceptan dígitos y un separador decimal (punto o coma). Si el texto no se puede leer, el saldo no es cero: entra como 1, para que el acuerdo no cuente como al día. El evaluador no se entera del texto.

`SIMIT_PROPIETARIO` y `SIMIT_LOCATARIO` usan la misma lectura. En leasing, el evaluador ya ignora al propietario; el lector igual le entrega los hechos si la celda llegó.

## Licencia

Se resume la lista de personas de la celda en una `LicenciaConductor`.

- El estado que viaja es el peor que aparezca: suspendida, luego cancelada, luego vencida, también en masculino. Si ninguna está así, viaja el primer estado informado, o null si no hay.
- `CertificadoAptitudVigente` es true solo si cada persona leída tiene al menos un certificado vigente.
- Un certificado está vigente si su estado es `VIGENTE`. Si el estado viene vacío y `vigente_hasta` es una fecha `dd/MM/yyyy` mayor o igual que `Hoy`, también cuenta. `VENCIDO` o `CANCELADO` no cuentan.
- Celda disponible y lista de personas vacía: se entrega licencia con estado null y certificado false. La lectura llegó y no hay certificado.
- Celda no disponible: la licencia es null. El evaluador no inventa la advertencia.

La del propietario sale de la celda `RUNT_PROPIETARIO`. La del locatario, de `RUNT_LOCATARIO`.

## Impuesto

No es fuente obligatoria. No dispara `FUENTE_PENDIENTE`.

| Situación | `ImpuestoMontoPesos` |
| --- | --- |
| No hay celda de impuesto | null |
| Celda en error, no aplica, pendiente o sin lectura | null |
| Celda completada con lectura | `MontoTotal` |

## Qué queda fuera

- Llamar a `LectorDetalleRunt`, `SimitVisible` o `LicenciasConductor` desde el cierre del vehículo. Eso es el corte 5.
- Tablas, siembra, redactor, plantillas de respaldo, PDF y endpoints.
- Cambiar `EvaluadorDictamen` o el catálogo. Si un hecho no cabe en los tipos actuales, se ajusta el lector, no la regla.
- Marcar fuentes obligatorias en el alta.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Api/Modulos/Dictamen/LectorHechosDictamen.cs` | `PedidoLectura`, `LecturaCelda` y `Leer` |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Dictamen/LectorHechosDictamenTests.cs` | Casos de este plan |

Los tipos de detalle ya son públicos. El lector nuevo también. No hace falta descifrar en el test: se arman `DetalleRuntReporte`, `VehiculoRunt`, `ResultadoSimitDetalle`, `ResultadoRuntPersonaDetalle` y `ResultadoImpuestoDetalle` en memoria.

## Pruebas

Primero los tests, una sola corrida en rojo, sin tocar producción. El fallo esperado es de compilación: `LectorHechosDictamen` todavía no existe. Después, la implementación hasta dejarlos en verde, sin reescribirlos.

Un caso por fila:

1. RUNT completado con SOAT vigente y otro no vigente, RTM vigente, matrícula de más de 5 años, estado `ACTIVO`, sin prenda ni gravamen: los hechos no hacen disparar SOAT ni RTM al evaluar.
2. SOAT solo `NO VIGENTE`: al evaluar, `BLOQUEADO` por `SOAT_NO_VIGENTE`.
3. Fecha de matrícula ilegible: null, y una RTM no vigente bloquea.
4. Fecha `dd/MM/yyyy` con menos de 5 años a `Hoy`: la RTM no vigente no dispara.
5. Prenda `si` y gravamen `SI` advierten. `NO` no.
6. Conteos de limitación, garantía mobiliaria e infracción.
7. Un propietario `Leasing` y un locatario con número: `EsLeasing` y `HayDocumentoLocatario`.
8. Locatario sin número: `HayDocumentoLocatario` false.
9. Detalle RUNT null con celda completada: fuente en sin resultado y hechos RUNT null.
10. Celda RUNT en error: `Error`, aunque el detalle venga pegado.
11. Celda en `NA`: `NoAplica`.
12. SIMIT con comparendos, paz y salvo false, suspendida, y un acuerdo `pendiente` en `"0"` más otro en `"422652"`: el de cero no bastaría solo; el de 422652 sí advierte.
13. `pendiente` ilegible: el saldo que llega al hecho no es cero.
14. Dos personas SIMIT: se suman comparendos y se concatenan acuerdos. Paz y salvo false si una lo está.
15. Lista SIMIT vacía con celda completada: sin resultado.
16. Licencia `VENCIDA` advierte por `RUNT_PROPIETARIO`.
17. Licencia activa y certificado `VIGENTE`: no dispara licencia.
18. Sin certificados, celda de propietario completada: certificado false y la regla advierte.
19. Certificado con estado vacío y vencimiento posterior a `Hoy`: vigente.
20. Celda de propietario en error: licencia null y `FUENTE_PENDIENTE`.
21. Impuesto 1500 advierte con ese monto. Sin celda, el monto es null y no hay `FUENTE_PENDIENTE` de impuesto.
22. Las cuatro obligatorias en error: cuatro `FUENTE_PENDIENTE` y veredicto `VIABLE` si el resto no bloquea.
23. El pedido null lanza `ArgumentNullException`.

La corrida en verde incluye la regresión `EvaluadorDictamenTests`. No hay Postgres.

```text
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~LectorHechosDictamenTests|FullyQualifiedName~EvaluadorDictamenTests"
```

El formato, con severidad error, solo sobre los `.cs` de ese diff.

## Listo cuando

- Los 23 casos pasan y la regresión del evaluador sigue en verde.
- Ningún archivo de persistencia, redactor, reportes ni catálogo de trámites cambió.
- `AvanceVehiculo` no llama al lector.
- El registro de la corrida en rojo y en verde queda en este documento, en local, sin Azure DevOps.

## Corrida

Hecho el 2026-11-03, sin work item.

Rojo, antes de existir `PedidoLectura` y `LecturaCelda`. El dominio y la API que ya estaban compilaron. Falló el proyecto de tests: 2 errores `CS0246`, 0 advertencias, 34,9 s. Log en `lector-rojo.log`.

```text
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --verbosity minimal
```

Verde, después del lector:

```text
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --verbosity quiet
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~LectorHechosDictamenTests|FullyQualifiedName~EvaluadorDictamenTests"
```

Compilación correcta, 0 advertencias. 74 superadas, 0 con error, 243 ms: 23 del lector y 51 del evaluador. El formato con severidad error, solo sobre los dos `.cs` de este corte, no reportó cambios.
