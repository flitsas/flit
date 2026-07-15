# ADR-0029: Capa de avalúo comercial multi-proveedor (agregación paralela)

**Fecha**: 2026-07-14
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT, Juan Felipe Montoya Garcia
**Tags**: arquitectura, backend, modulo-tramites, traspaso, integraciones

## Contexto

En el paso comercial del traspaso se requiere **sugerir el valor de venta** agregando varias fuentes de avalúo (Fasecolda real; base gravable y Mercado Libre como referencia), con **desglose por fuente** y **tolerancia a fallo parcial**: si una fuente no responde o no tiene datos, el paso no se bloquea (Feature #10707, AC#3). Esta semántica —agregación paralela orientada a **valor monetario**— difiere de la verificación de identidad/vehículo de [ADR-0020], cuyo `ConsultationResult` modela *checks* (`overall green|yellow|red`) y cuyo chain resolver hace **failover secuencial** (el primer proveedor sin `error` gana). La API real de Fasecolda opera por VIN (flujo `analisis`: `busquedaVin`→`token`→`consultabycodigo`, filtros de vehículo, valor en miles de COP → ×1000).

## Decisión

Introducir una abstracción propia **`IAvaluoProvider`** (`Key` + `GetAvaluoAsync`) con su **`AvaluoProviderRegistry`** y un handler de **agregación paralela** (`GetSuggestedCommercialValueHandler`), reutilizando las convenciones de [ADR-0020] (identidad por `Key`, `Options` + `HttpClient` tipado, toggle mock/real en `appsettings`, registro en `InfrastructureExtensions`). Fasecolda real por VIN; base gravable y Mercado Libre como proveedores mock activables por configuración.

## Alternativas consideradas

### Opción 1: Reusar `IConsultationProvider` + nuevo `ConsultationKind` "avaluo"

**Pros:** reutiliza registry/failover/mock; sin abstracción nueva; consistente con RUNT/Verifik.
**Cons:** `ConsultationResult` es de checks, no de valor; el chain es failover secuencial, no agregación paralela; el desglose multi-fuente no encaja en `HydratedFields`.
**Esfuerzo:** M
**Riesgos:** degrada/contorsiona el contrato de consultas de identidad.

### Opción 2: Nueva abstracción `IAvaluoProvider` + handler paralelo (elegida)

**Pros:** contrato natural para valor + desglose; paralelo + fallo parcial nativo; separa "avalúo" de "consulta"; patrón reconocible (espeja ADR-0020); extensible sin tocar el handler.
**Cons:** segunda abstracción de proveedor a mantener; algo más de código inicial.
**Esfuerzo:** M
**Riesgos:** bajo.

### Opción 3: Cliente Fasecolda directo sin abstracción

**Pros:** mínimo código; rápido.
**Cons:** no extensible (agregar proveedor = tocar el handler); sin registry/toggle uniforme; contradice ADR-0020 y FEATURE-03; deuda inmediata.
**Esfuerzo:** S
**Riesgos:** rework al agregar el 2º proveedor real.

## Tradeoff aceptado

Se acepta **mantener un segundo patrón de proveedor** a cambio de no degradar el contrato de consultas de [ADR-0020] y de modelar correctamente valor + desglose + agregación paralela con fallo parcial. La reutilización de las convenciones (Key, Options, HttpClient tipado, toggle mock/real) mantiene la curva de aprendizaje baja.

## Consecuencias

### Lo que se gana
- Extensibilidad multi-fuente de avalúo sin tocar el handler.
- Separación de concerns: verificación (ADR-0020) vs valoración (este ADR).
- Fallo parcial y demo mock por configuración (FEATURE-03 §6).

### Lo que se pierde
- Un patrón adicional de proveedor que mantener y testear.

### Cambios operacionales
- Nueva sección `Fasecolda` + toggle `Consultations:FasecoldaMode` en `appsettings`; credenciales por User Secrets/env (rotar las expuestas).
- ALTER de `tramites.procedure_instance_commercial` (`suggested_value`, `suggested_source`, `value_origin`).
- Seed mock DEV/QA (`avaluo_mock_values`) + binding `external_refs` de la fila `FASECOLDA` (ya sembrada, HU10151).

## ADRs relacionados

- [ADR-0020] — Capa multi-proveedor de consultas externas (convenciones reutilizadas; este ADR **extiende**, no supersede).
- [ADR-0018] — Modelo de datos Fase 1 (trazabilidad HU→migración, reversible).

## Notas para agentes

- **Backend Agent**: `IAvaluoProvider` + Fasecolda real (token cacheado por `expires_in`, ×1000, filtros desde `field_values`, distinguir 404/timeout/5xx ≠ "no data") + mocks; handler con `Task.WhenAll` y tolerancia a fallo parcial.
- **Frontend Agent**: tarjeta "Avalúo comercial" fiel al design system; Aceptar setea `valorVenta` y marca `valueOrigin=suggestion`; nunca bloquear el paso.
- **QA Agent**: cubrir AC#1–6 (match feliz, VIN sin coincidencia, fuente caída, aceptar/modificar, trazabilidad, ×1000). Fixture VIN `93Y9SR333RJ563653`.
- **Security Agent**: credenciales fuera del repo; rotar expuestas; no loguear token; Habeas Data N/A (dato de vehículo).
- **Infra Agent**: migraciones al arranque (`Program.cs`); inyectar `Fasecolda__*` / `Consultations__FasecoldaMode` por env en QA/PDN.

## Referencias externas

- `docs/servicio-fasecolda-guia-implementacion.md` — guía validada de la API Fasecolda.
- `docs/design/FEATURE-10707-diseno-avaluo-comercial.md` — diseño técnico completo.
