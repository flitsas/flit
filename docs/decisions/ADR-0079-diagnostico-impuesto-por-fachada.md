# ADR-0079: El impuesto vehicular del Diagnóstico se consulta por fachada, con modo simulado

**Fecha**: 2026-11-04
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Decisiones de producto del usuario del 2026-11-04 (P1 a P9 del plan C3).
**Tags**: arquitectura, backend, diagnostico, consultas, impuesto, Habeas Data
**Épica / HU**: #12659 · trabajo en `feature/AB-12881-diagnostico-d0-d2`, sin work item (registro diferido)
**Diseño**: `docs/diagnosticos/plan-consulta-impuesto.md`
**Marca para ratificación**: el plan v2 (`docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` §(f), alrededor de la línea 520) eligió el scraper dentro de core-consultas (F1). Este ADR no lo marca como sustituido: pide al Líder Técnico que ratifique la fachada y descarte esa frase. Sigue en Propuesto.

## Contexto

Diagnóstico ya tiene el contrato gRPC de impuesto, la compuerta y la celda, y core-consultas responde `UNIMPLEMENTED`. El servicio que hoy consulta los portales departamentales no está en este repositorio. Las API reales están apagadas. El encargo es dejar modelos, colas y la URL, y un modo simulado que pase por el mismo intérprete que la API real.

Restricciones:

- HTTP 200 no confirma el resultado. La clasificación vive en el cuerpo.
- El nombre del propietario viaja en el mensaje de la cola, descifrado en memoria. No se persiste como columna. Eso enmienda U1 = b de ADR-0075 solo para este dato, y el encendido real espera la revisión de Seguridad y Habeas Data.
- `descriptionError`, `descriptionErrorDevelopment` y `pendingValidity` no entran al proto: pueden traer trazas y la placa.
- ADR-0076 no se amplía: el constructor de impuesto sigue sin respuesta cruda del portal.

## Decisión

**Fachada.** core-consultas arma el cuerpo, llama por HTTP al servicio actual y traduce la respuesta. Diagnóstico no habla con el portal. El modo simulado es otro transporte (`ITransporteImpuesto`) que devuelve JSON crudo sintético al mismo intérprete. Pasar a real es quitar `impuesto` de la simulación en LOCAL y DEV. Los departamentos encendidos salen de `consultas.vehicle_tax_departments` (ADR-0080), no de una variable de entorno.

## Alternativas consideradas

### Opción 1: Fachada HTTP hacia el servicio actual

**Pros:**
- Coincide con el pedido, con el acta de consultas y con la fachada que propone ADR-0075.
- Copia el patrón de Intempo. Sin Playwright en core-consultas.
- El servicio se puede reemplazar sin tocar Diagnóstico.

**Cons:**
- El servicio no está en este repositorio. Dueño y SLA quedan para el Líder Técnico (H-3).

**Esfuerzo:** M
**Riesgos:** Dependencia de un servicio externo apagado. El modo simulado no lo necesita.

### Opción 2: Scraper dentro de core-consultas (plan v2 §(f) F1)

**Pros:**
- Código propio y versionado. Sin servicio aparte.

**Cons:**
- Reescribe los portales. Mete un navegador en el servicio de consultas.

**Esfuerzo:** L
**Riesgos:** Mantener cada portal. Contradice el acta, que pide usar el servicio actual.

### Opción 3: Diagnóstico llama directo al servicio

**Pros:**
- Menos saltos.

**Cons:**
- Rompe la regla de que las integraciones viven en Consultas y lo que propone ADR-0075.
- Sin bus y sin consumo medido. La homologación del proveedor quedaría en Diagnóstico.

**Esfuerzo:** S
**Riesgos:** Dos caminos de consulta. No se adopta.

## Tradeoff aceptado

La opción 1 deja el modo simulado terminable ya y el encendido real detrás de un cambio de configuración. Se acepta depender de un servicio cuyo dueño todavía no está nombrado, porque escribir los portales aquí cuesta más y contradice el acta. La opción 3 es más corta y se descarta porque saca la consulta del bus.

## Consecuencias

### Lo que se gana

- Un intérprete único para el fixture y para el HTTP.
- Departamentos, homologación de documento y tope de 300 s en core-consultas.
- Deuda sin monto visible, sin convertir el vacío en cero.

### Lo que se pierde

- El impuesto real no se enciende con este ADR. Hacen falta la ratificación del Líder Técnico, la enmienda de ADR-0075, Seguridad, Habeas Data y la constancia de Legal.

### Cambios operacionales

- Variables `IMPUESTO_API_*` sin valor por defecto en código.
- Colas propias del impuesto (P8), fuera de `consultas.diagnostico.*`.
- Orden de despliegue: definiciones del broker, core-consultas, core-diagnostico, y al final la lista de departamentos.

## ADRs relacionados

- [ADR-0075](ADR-0075-diagnostico-consultas-por-rabbitmq.md) — la ida no lleva nombres (U1 = b). La enmienda del nombre del propietario queda en Propuesto, en el mismo ADR, cuando cierre el corte de documentos.
- [ADR-0076](ADR-0076-diagnostico-respuesta-completa-de-la-fuente-con-pii-cifrada.md) — no se amplía: el impuesto no pide respuesta cruda del portal.

## Notas para agentes

- **Backend**: el intérprete no conoce HTTP ni las claves de error del proto. No registrar cuerpo, URL, placa, documento, nombre ni el mensaje de la excepción.
- **Frontend**: «No confirmado» y «deuda sin valor informado» no son verde ni cero.
- **QA**: la tabla de simulación del plan es el guion. Placas `TST20Z` a `TST29Z`, más una terminada en 6.
- **Security**: nombre y documento en claro en outbox, colas y DLQ. Bloquea el encendido real, no la simulación.
- **Infra**: la simulación se rechaza al arrancar fuera de LOCAL y DEV.

## Referencias externas

- `docs/diagnosticos/plan-consulta-impuesto.md`
- `docs/diagnosticos/acta-reunion-consultas-colas-y-servicios.md`
