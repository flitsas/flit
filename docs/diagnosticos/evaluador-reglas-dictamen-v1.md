# Evaluador y catálogo v1 del dictamen

Documento local del 2026-11-03. Rama `feature/AB-12881-diagnostico-d0-d2`. Épica **#12678**, estado New, sin historias hijas. No se activó ningún work item y no se publicó nada en Azure DevOps.

La ficha `ficha-catalogo-reglas-dictamen.md` quedó confirmada y este corte la convierte en código. El redactor LLM de `guia-implementacion-punto1-dictamen.md` no se tocó.

## Qué entra

Un evaluador puro en el dominio de Diagnóstico. Recibe hechos ya leídos y devuelve las reglas que dispararon y el veredicto. No abre celdas, no llama fuentes, no redacta y no escribe en base.

La condición de cada regla vive en código. La fila del catálogo guarda versión, tipo de trámite, código, efecto y plantilla. La misma definición se repite en:

- `TRASPASO_STANDARD`
- `TRASPASO_UNILATERAL`
- `TRASPASO_TRANSFERENCIA_DE_DOMINIO`

Son 14 reglas por tipo, 42 filas, versión 1. Una regla `BLOQUEA` deja el veredicto en `BLOQUEADO`. Si solo hay `ADVIERTE`, o ninguna dispara, el veredicto es `VIABLE`.

| Código | Efecto al disparar | Fuente que informa |
| --- | --- | --- |
| `SOAT_NO_VIGENTE` | BLOQUEA | `RUNT` |
| `RTM_NO_VIGENTE` | BLOQUEA | `RUNT` |
| `ESTADO_VEHICULO` | ADVIERTE | `RUNT` |
| `SIMIT_VEHICULO` | ADVIERTE | `SIMIT_VEHICULO` |
| `SIMIT_PROPIETARIO` | ADVIERTE | `SIMIT_PROPIETARIO` |
| `SIMIT_LOCATARIO` | ADVIERTE | `SIMIT_LOCATARIO` |
| `LIMITACION`, `GRAVAMEN`, `GARANTIA_MOBILIARIA`, `PRENDA`, `INFRACCION_RUNT` | ADVIERTE | `RUNT` |
| `LICENCIA_CONDUCTOR` | ADVIERTE | `RUNT_PROPIETARIO` o `RUNT_LOCATARIO` |
| `IMPUESTO` | ADVIERTE | `IMPUESTO_DEPARTAMENTAL` |
| `FUENTE_PENDIENTE` | ADVIERTE | la fuente obligatoria que no llegó |

El monto en pesos solo viaja en `IMPUESTO`. En el resto queda null.

## Decisiones confirmadas

- `PRENDA`, `IMPUESTO` y `FUENTE_PENDIENTE` entran en la versión 1 y advierten.
- SOAT y RTM no disparan si alguna póliza o revisión está `VIGENTE` o `NO REQUIERE` (también `vigente`, `no requiere` y `no_requiere`). Disparan si no hay ninguna en esos estados: vencida, no vigente, lista vacía, estado nulo o estado que no reconocemos.
- La RTM solo se evalúa si la matrícula tiene 5 años o más, contados desde `fechaMatricula`, o si esa fecha no viene. El día en que cumple 5 años ya se evalúa. Con menos de 5 años la regla no dispara: no advierte.
- Cuando la RTM dispara, el efecto es `BLOQUEA`.
- `REGISTRADO` y `ACTIVO` no disparan `ESTADO_VEHICULO`. Cualquier otro estado, incluido el vacío, advierte. Ningún estado bloquea.
- Un acuerdo de pago está al día cuando el saldo pendiente es cero pesos. Un saldo distinto de cero advierte. El acuerdo al día, por sí solo, no dispara.
- En leasing no se evalúa `SIMIT_PROPIETARIO`. Si no hay documento de locatario, `SIMIT_LOCATARIO` advierte que la deuda no se pudo consultar. Con documento, se evalúa el SIMIT del locatario. Sin leasing y sin documento, el locatario no se evalúa.
- La licencia se mira en el propietario y, en leasing, también en el locatario. Dispara si el estado es suspendida, vencida o cancelada (también en masculino), o si no hay certificado de aptitud vigente. Una lectura de licencia en null significa que no se pasó: no se inventa la advertencia.
- El impuesto no dispara si el monto es null o cero. Si es mayor que cero, advierte y el hallazgo lleva ese monto.
- `FUENTE_PENDIENTE` dispara, una vez por fuente, si el RUNT del vehículo, el SIMIT del vehículo, el RUNT del propietario o el SIMIT del propietario está en error, no aplica o sin resultado. No bloquea por sí sola. Si el RUNT no está disponible, sus hechos no se evalúan: un SOAT vacío pegado a una celda en error no bloquea.
- Un tipo de trámite fuera de la familia TRASPASO no se evalúa.

## Qué queda fuera

- Migración de `diagnostico.reglas_dictamen` y `diagnostico.dictamenes`.
- Traducir el payload de la celda a `HechosDictamen`. Esos datos hoy están en el detalle del PDF y del tablero, no en `ResultadoRunt` ni en `ResultadoSimit`.
- Enganche al vehículo terminado, cola de redacción, invalidación del PDF y endpoints.
- Marcar las cuatro fuentes obligatorias en el alta. `CatalogoTiposTramiteGrpc` sigue devolviendo la lista vacía. Eso espera el dictamen de `expert-doc-engine` (R-17).
- Usar las plantillas en el respaldo del redactor. Hoy `PlantillaRedaccion` sigue armando el texto genérico de «Redacción IA pendiente».
- ADR (g), OpenAPI y activar la épica #12678.

## Archivos

| Archivo | Papel |
| --- | --- |
| `services/core-diagnostico/src/Flit.Diagnostico.Domain/Dictamen/HechosDictamen.cs` | Entrada ya leída |
| `services/core-diagnostico/src/Flit.Diagnostico.Domain/Dictamen/CatalogoReglasDictamen.cs` | 42 filas y las plantillas de la ficha |
| `services/core-diagnostico/src/Flit.Diagnostico.Domain/Dictamen/EvaluadorDictamen.cs` | Motor y veredicto |
| `services/core-diagnostico/tests/Flit.Diagnostico.Tests/Dictamen/EvaluadorDictamenTests.cs` | 32 métodos, 51 casos |

## Corrida en rojo

Antes de existir el dominio `Flit.Diagnostico.Domain.Dictamen`. El dominio que ya estaba compiló; falló solo el proyecto de tests.

```text
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --verbosity minimal
```

Resultado: error de compilación, 12 errores, 0 advertencias, 15,8 s. El primero:

```text
EvaluadorDictamenTests.cs(1,31): error CS0234: El tipo o el nombre del espacio de nombres
'Dictamen' no existe en el espacio de nombres 'Flit.Diagnostico.Domain'
```

El resto son `CS0246` y `CS0103` por `EstadoFuenteDictamen`, `HechosDictamen`, `HechosRuntVehiculo` y `HechosSimit`. El log completo está en `evaluador-rojo.log`.

## Corrida en verde

Un build del proyecto de tests, que arrastra el dominio y la API de Diagnóstico. Sin build de la solución.

```text
dotnet build services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --verbosity quiet
```

Compilación correcta. 0 advertencias, 0 errores.

```text
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~EvaluadorDictamenTests"
```

51 superadas, 0 con error, 186 ms.

Regresión del redactor, el mismo assembly y `--no-build`:

```text
dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~RedactorLlmTests|FullyQualifiedName~RedactorArquitecturaTests"
```

27 superadas, 0 con error. Juntas con el evaluador son las 78 de la primera corrida filtrada (`evaluador-verde.log`).

El formato con severidad error, solo sobre estos cuatro `.cs`, no reportó cambios:

```text
dotnet format services/core-diagnostico/Flit.Diagnostico.slnx --severity error --include <cada archivo>
```

## Casos que cubre el test

- Nada dispara y el veredicto es `VIABLE`.
- SOAT cubierto, SOAT vigente junto a pólizas no vigentes, y SOAT sin cobertura (no vigente, vencido, desconocido, vacío, nulo).
- RTM con menos de 5 años, el día en que cumple 5, sin fecha, y vigente junto a otra vencida.
- Estado activo, registrado y distinto. El distinto deja el traspaso viable.
- SIMIT del vehículo por comparendos, paz y salvo, suspensión, cancelación y saldo pendiente.
- Acuerdo con saldo cero.
- Leasing: no se evalúa el propietario; sin documento de locatario advierte; con documento y saldo advierte; sin leasing y sin documento no se evalúa el locatario.
- Limitación, gravamen, garantía mobiliaria, prenda e infracción, en ese orden, todas advierten.
- Licencia suspendida, vencida o cancelada, certificado sin vigencia, y licencia del locatario en leasing.
- Impuesto en cero, sin monto y con monto.
- Fuente obligatoria en error, no aplica o sin resultado, las cuatro juntas, y SOAT que bloquea aunque otra fuente solo advierta.
- Los tres tipos comparten veredicto. El catálogo tiene 42 filas únicas y las plantillas de la ficha.
- Tipo desconocido, hechos nulos y RUNT disponible sin lectura.
