# Prueba de carga: crear trámites desde el diagnóstico (masivo)

> Generado: 2026-11-11 · Estado: **guion para QA**; el objetivo es el propuesto en el diseño y lo ratifica el Líder Técnico antes de correrlo.
> Diseño: `docs/diagnosticos/diseno-crear-tramite-desde-diagnostico.md` §5.3 y §9 · Decisión: ADR-0084 · Script: `tests/load/diagnostico-derivacion.js` (k6).

## 1. Qué se prueba y qué ya está cubierto

El masivo encola un ítem por vehículo. Diagnóstico libera como mucho `CreditoPorCompania` ítems en vuelo por compañía, y el consumidor `diagnostico.derivaciones` llama al rpc `CrearBorradorDesdeDiagnostico` de core-api con `Concurrencia` llamadas a la vez, un freno (tasa + pausa tras fallos seguidos) y reintentos de 30 s, 2 min y 10 min.

Lo que **ya** prueba la suite de core-diagnostico, sin core-api real (`tests/Flit.Diagnostico.Tests/Derivacion/SimulacionDerivacionTests.cs`, doble del cliente gRPC):

| Escenario | Qué comprueba |
|---|---|
| 1 compañía con 500 placas + 3 con 10 | las pequeñas terminan en turno rotativo (no esperan a la grande); nunca más de crédito × compañías ítems en vuelo; nunca más de `Concurrencia` llamadas a la vez; 0 duplicados |
| RESOURCE_EXHAUSTED en el primer intento de cada placa | el ítem reintenta conservando su crédito (la compañía no inunda); cada borrador se crea una sola vez |
| core-api saturado 300 ms | el freno abre la pausa y casi no entran llamadas nuevas hasta que vence; luego retoma solo |

Lo que **solo** mide esta prueba: latencia real del rpc, efecto sobre el resto de core-api, rendimiento sostenido con la configuración por defecto y la recuperación tras una caída real.

## 2. Ambiente y datos

- Ambiente **QA**, con core-api y core-diagnostico desplegados con la misma imagen que irá a PDN. **Nunca PDN.**
- Encender en QA: `TRAMITES_DERIVACION_HABILITADA=true` (core-api) y `DIAGNOSTICO_DERIVACION_HABILITADA=true` (core-diagnostico). El resto, con los valores por defecto (`Concurrencia` 4, `Prefetch` 8, `CreditoPorCompania` 2, `TasaPorSegundo` 10, `PausaTrasFallos` 5, `PausaSegundos` 30, `MaxIntentos` 3; core-api `ConcurrenciaMaxima` 8).
- La cola `diagnostico.derivaciones` debe existir en el broker (`deploy/rabbitmq/definitions.json`).
- **Datos sintéticos, sin PII real** (Ley 1581): placas de prueba (`TSTnnnn`) y documentos ficticios del rango `99999nnn`. Prohibido usar `context/muestras/` o copias de PDN.
- Preparación (fuera del script, una vez): 11 compañías de prueba con Diagnóstico activo y la familia de traspaso habilitada en Trámites; en cada una, un usuario con `diagnostico.derivacion.crear` + `diagnostico.diagnosticos.ver` en Diagnóstico y `tramites.create` + Crear de la familia en Trámites; y por compañía 2 lotes **Con análisis** terminados con dictamen VIABLE (1.000 placas cada uno; la ballena, uno de 5.000; las 3 pequeñas, lotes de 20). El OT de matrícula de las placas debe estar en la lista efectiva de cada compañía (si no, los ítems salen `OMITIDO OT_NO_HABILITADO` y no miden nada).
- El script recibe los tokens y los ids de lote por variables de entorno; **no** guarda ni imprime tokens.

## 3. Escenarios

| Id | Carga | Cómo |
|---|---|---|
| E1 base | 10 compañías × 2 lotes × 1.000 placas encolados en el mismo minuto (20.000 borradores) | `ESCENARIO=E1` |
| E2 ballena | E1 + 1 compañía con 5.000 placas; en paralelo, 3 compañías pequeñas encolan 20 placas cada 2 min | `ESCENARIO=E2` |
| E3 caída | E1 con core-api detenido 5 min a mitad de corrida (`docker compose stop core-api` en QA, luego `start`) | `ESCENARIO=E1` + parada manual a los ~15 min |
| E4 ruido | Durante E1, 30 usuarios virtuales navegando Trámites (`GET /api/v1/tramites/instances`, detalle) | `ESCENARIO=E4` |

## 4. Umbrales (criterio de aceptación AC9)

| Métrica | Objetivo | Dónde se mide |
|---|---|---|
| Latencia del rpc | p95 ≤ 1,5 s, p99 ≤ 3 s | log de core-diagnostico «CrearBorradorDesdeDiagnostico del ítem … en N ms» (o la traza OTel `grpc.client.duration`) |
| Resto de core-api (E4) | p95 no empeora más de 20 % frente a la línea base sin carga | k6: `http_req_duration{grupo:tramites}`; correr E4 una vez sin masivo para la línea base |
| Errores | 5xx / `INTERNAL` < 0,5 %; **0 borradores duplicados** por `external_ref` | k6 + consulta SQL de duplicados (§6) |
| Rendimiento | ≥ 8 borradores/s sostenidos (≈ 42 min para E1) | k6: `derivacion_creados` / duración; o `creadas` de las operaciones |
| Equidad (E2) | cada lote de 20 placas termina en ≤ 3 min mientras corre la ballena | k6: `derivacion_lote_pequeno_ms` p100 ≤ 180.000 |
| Cola | `diagnostico.derivaciones` ≤ `CreditoPorCompania × compañías activas` mensajes listos (≤ 2 × 14 = 28 en E2) | panel de RabbitMQ (`messages_ready`), muestreo cada 30 s |
| Caída (E3) | ningún ítem `FALLIDO` antes de agotar reintentos; al volver core-api se retoma sin intervención | progreso de las operaciones + logs del freno «pausa de 30 s» |

## 5. Cómo correrlo

```bash
# Máquina de pruebas con k6 instalado (binario; no es dependencia del repo).
export DGN_BASE_URL="https://qa.<dominio>/api/v1/diagnostico"       # por el gateway
export TRAMITES_BASE_URL="https://qa.<dominio>/api/v1/tramites"     # solo E4
export ESCENARIO=E1                                                  # E1 | E2 | E4
# Un JSON por compañía: token de usuario de esa compañía y los lotes a derivar (ids de lote; el script lee la grilla).
export DGN_COMPANIAS_JSON=/ruta/fuera-del-repo/companias-qa.json
k6 run tests/load/diagnostico-derivacion.js --summary-export=/ruta/fuera-del-repo/resumen-e1.json
```

Formato de `DGN_COMPANIAS_JSON` (fuera del repo; contiene tokens):

```json
[
  { "nombre": "qa-carga-01", "token": "<jwt aud=diagnostico>", "tokenTramites": "<jwt aud=tramites, solo E4>", "lotes": ["<uuid lote 1>", "<uuid lote 2>"], "rol": "base" },
  { "nombre": "qa-ballena",  "token": "<jwt>", "lotes": ["<uuid lote 5000>"], "rol": "ballena" },
  { "nombre": "qa-peq-01",   "token": "<jwt>", "lotes": ["<uuid lote 20>", "…"], "rol": "pequena" }
]
```

## 6. Verificaciones después de cada corrida

1. **Duplicados** (core-api, base de QA):
   ```sql
   SELECT tenant_id, external_ref, count(*) FROM tramites.procedure_instances
    WHERE origin = 'diagnostico' AND deleted_at IS NULL GROUP BY 1, 2 HAVING count(*) > 1;   -- debe dar 0 filas
   ```
2. **Cierre de las operaciones** (core-diagnostico, base de QA): todas `TERMINADA`; `creadas + omitidas + fallidas = total`.
   ```sql
   SELECT estado, count(*), sum(total), sum(creadas), sum(omitidas), sum(fallidas) FROM diagnostico.derivacion_operaciones GROUP BY 1;
   ```
3. **Nada colgado**: `SELECT count(*) FROM diagnostico.derivaciones WHERE estado = 'ENVIADO' AND enviado_en < now() - interval '15 minutes';` debe ser 0 al terminar (si no, revisar el reconciliador).
4. **DLQ**: `diagnostico.derivaciones.dlq` vacía.
5. **Logs sin PII**: buscar en los logs de core-diagnostico y core-api de la ventana de la prueba las placas `TST` y los documentos `99999`: 0 coincidencias.

## 7. Qué hacer si no se cumple

| Síntoma | Ajuste (sin redeploy de código) |
|---|---|
| rpc p95 alto y `RESOURCE_EXHAUSTED` frecuentes | bajar `Diagnostico__Derivacion__Concurrencia`/`TasaPorSegundo` o subir `Tramites__Derivacion__ConcurrenciaMaxima` (≥ suma de `Concurrencia` de las réplicas) |
| Resto de core-api se degrada > 20 % (E4) | bajar `TasaPorSegundo` |
| Lotes pequeños tardan > 3 min (E2) | bajar `CreditoPorCompania` a 1 o subir `Concurrencia` |
| Rendimiento < 8/s sin errores | subir `TasaPorSegundo` y `Concurrencia` (y `ConcurrenciaMaxima` en core-api) |
| Pool de conexiones de Diagnóstico agotado | sumar `Concurrencia` al tamaño del pool (el consumidor mantiene la conexión durante el rpc) |

Registrar en la HU de QA: escenario, configuración usada, métricas de §4, resultado de §6 y la decisión del LT.
