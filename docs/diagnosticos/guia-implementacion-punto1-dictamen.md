# Guía de implementación — punto 1, dictamen (redactor LLM)

Documento local del 2026-11-03. Guía de esta rama. No crea work items, no cambia estados en Azure DevOps y no publica contratos.

## Relación con lo que ya existe

La épica **#12678** ([5] Motor de IA y dictamen) está en **New** y no tiene Historias hijas. Este trabajo no la activa ni siembra Features ni HUs.

Se engancha en lo ya entregado:

| Qué | Dónde | HU existente |
|---|---|---|
| Permiso `diagnostico.dictamen.override` (aún sin endpoint) | `Plataforma/Permisos.cs` | #13724 |
| Interruptor `redaccion_ia_suspendida` (solo backoffice) | `diagnostico.configuracion_empresa` | #13741 |
| Modo `CON_ANALISIS` y familia TRASPASO en la cabecera | `ValidadorCabecera` | olas D0–D1, paraguas #12881 |

## Qué entra en esta entrega

Solo el **redactor** (IA1), el mismo patrón que el OCR de Comparendos (`Comparendos:Ocr` → Anthropic si hay llave, simulado si no, y si no hay ninguno el proveedor no configurado). Diagnóstico usa su propia sección porque la llave no se comparte con Comparendos ni con core-api.

| Pieza | Decisión |
|---|---|
| Opciones | `Diagnostico:Anthropic` (`BaseUrl`, `ApiKey`, `Modelo`, `TimeoutSegundos`, `MaxTokens`, `ReintentosContradiccion`, `Simulado`) |
| Con llave | `ProveedorRedactorAnthropic` (Messages API, solo un bloque `text`; la llave va en `x-api-key` y no se registra) |
| Sin llave, simulación permitida | `ProveedorRedactorSimulado` |
| Si no | `ProveedorRedactorNoConfigurado` |
| Orquestación | `ServicioRedactor` |
| Pedido al modelo | Veredicto, reglas (`codigo`, `fuente`, `efecto`, `monto_pesos`) y bien (`placa`, `clase`, `marca`, `linea`, `modelo`, `servicio`). Sin documentos, nombres, propietario ni locatario |
| Contradicción | Si el texto trae el veredicto contrario, se descarta y se vuelve a pedir, hasta `ReintentosContradiccion` (1 por defecto, o sea dos llamadas). Si sigue en contra, sale la plantilla |
| LLM caído o interruptor | Plantilla con el texto «Redacción IA pendiente», `redactado_por=PLANTILLA`, `estado_redaccion=PENDIENTE`. Un fallo técnico no se reintenta aquí: eso queda para la cola durable |
| Simulado | Solo con `Diagnostico:Ambiente` en `LOCAL` o `DEV`, y con `Diagnostico:Anthropic:Simulado=true` o `Diagnostico:Simulacion:Habilitada=true`. En QA, PDN o sin ambiente no se simula aunque la bandera esté en true. Si hay `ApiKey`, gana Anthropic |

Escenarios del simulado, por el último carácter de la placa:

| Carácter | Resultado |
|---|---|
| `F` | Fallo de red |
| `T` | Tiempo agotado |
| `X` | Texto con el veredicto contrario (el servicio lo descarta) |
| Cualquier otro | Texto alineado al veredicto |

Activación local, sin commitear la llave:

```text
Diagnostico__Ambiente=LOCAL
Diagnostico__Anthropic__Simulado=true
```

Con llave real, en el mismo ambiente:

```text
Diagnostico__Anthropic__ApiKey=<la llave del producto>
Diagnostico__Anthropic__Simulado=false
```

## Qué queda fuera de esta entrega

El catálogo v1 y el evaluador ya están en el dominio. El resto del punto 1 sigue sin código:

| Pieza | Por qué espera |
|---|---|
| Tablas `reglas_dictamen` y `dictamenes` | El catálogo v1 ya está en memoria. La migración sigue fuera de este corte |
| Siembra de fuentes obligatorias en el alta | Sigue esperando el dictamen de `expert-doc-engine` (R-17). El evaluador no marca esas fuentes |
| Cola durable, invalidación del PDF y `GET`/`PUT` de dictamen | Dependen de la fila de dictamen y del enganche al vehículo terminado |
| ADR (g) y OpenAPI | El ADR queda en Propuesto hasta que el Líder Técnico lo acepte. El OpenAPI se publica con el endpoint |
| Activar #12678 | Sigue en New. Pasarla de estado pide confirmación humana |

No hay migración EF en esta entrega. El catálogo v1 y el evaluador ya están en el dominio, sin tabla: `docs/diagnosticos/evaluador-reglas-dictamen-v1.md`. Cuando se persistan, la migración sale solo de `dotnet ef migrations add`, seguida de `migrations list` y de `db-schema-validator`, antes del primer test de integración.

## Dónde enganchar el siguiente corte

1. Al pasar el vehículo a terminado (`AvanceVehiculo.Terminado`), evaluar y llamar a `ServicioRedactor`.
2. Leer `redaccion_ia_suspendida` de la compañía y pasarla en el pedido.
3. Persistir `redactado_por`, `estado_redaccion` e intentos. La cola de `PENDIENTE` completa la redacción y, si ya había PDF, invalida la huella en `Modulos/Reportes/`.
4. El DTO de cliente omite `analisis_override`, `usuario_override_id` y `override_en`. El `PUT` exige `confirmado=true` y el permiso de la HU #13724.

## Pruebas

Proyecto: `services/core-diagnostico/tests/Flit.Diagnostico.Tests`.

Clases: `RedactorLlmTests`, `RedactorArquitecturaTests`.

No levantan Postgres. La corrida en rojo es la compilación de esos tests antes de que existan los tipos. Después, un solo `dotnet build` del proyecto de tests y `dotnet test --no-build` con el filtro de las clases del redactor.
