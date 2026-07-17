# Diseño: Avalúo comercial (Fasecolda) — Feature #10707

> architecture-agent · 2026-07-14 · Feature ADO #10707 · Sprint 2
> Fuentes: `docs/FEATURE-03-valor-comercial-sugerido.md`, `docs/fasecolda.md`, `docs/servicio-fasecolda-guia-implementacion.md`.

## Contexto

En el paso `comercial` del traspaso (exclusivo de esa modalidad, `CommercialForm.tsx`), el gestor hoy captura "Valor de venta" (`valorVenta`) 100% a mano. Se requiere una sección "Avalúo comercial" que consulte fuentes de valoración y **sugiera** el valor; el gestor lo **acepta** (autocompleta `valorVenta`) o lo **modifica**. Fasecolda es la primera fuente real; base gravable y Mercado Libre entran como referencia (mock en Fase 1). El paso **nunca debe bloquearse** si una fuente falla (AC#3).

Restricciones del repo:
- Ya existe el patrón `IConsultationProvider` + `ConsultationProviderRegistry` + toggle mock/real (`ConsultationProviderModeOptions.IsMock`) — pero su `ConsultationResult` es **orientado a verificación** (`overall green|yellow|red` + `checks[]`), no a valor monetario ni a agregación multi-fuente.
- Persistencia comercial: entidad `ProcedureInstanceCommercial` (1:1 con la instancia, `tramites.procedure_instance_commercial`, con `TenantId`/RLS). Endpoints `GET/PUT /api/v1/tramites/instances/{id}/commercial`. `CommercialDto` es "contrato congelado".
- `external_data_sources.FASECOLDA` ya sembrado (HU10151), sin template ni binding.
- La API real de Fasecolda funciona por **VIN** (flujo `analisis`: `busquedaVin`→`token`→`consultabycodigo`, filtros de vehículo, `valor ×1000`). Ver guía validada.

## Alternativas evaluadas

### Opción 1 — Reusar `IConsultationProvider` + nuevo `ConsultationKind` "avaluo"
Fasecolda como `IConsultationProvider`, invocado por el chain resolver; hidrata un `field_value` (`fasecolda_avaluo`).
- **Pros:** reutiliza registry/failover/mock existentes; cero abstracción nueva; consistente con RUNT/Verifik.
- **Cons:** `ConsultationResult` es de checks, no de valor; el chain es **failover secuencial** (primer no-error gana), no **agregación paralela** de varias fuentes; el desglose `{fasecolda, baseGravable, mercadoLibre, sugerido}` no encaja en `HydratedFields`; el endpoint tendría que reconstruir el breakdown desde `field_values`.
- **Esfuerzo:** M · **Riesgos:** contorsiona y degrada la semántica del contrato de consultas.

### Opción 2 — (Recomendada) Nueva abstracción `IAvaluoProvider` + handler paralelo + endpoint `suggested-value`
Abstracción propia orientada a valor que **reusa las convenciones** de la capa de consultas (registry por `Key`, `Options` + `HttpClient` tipado, toggle mock/real en `appsettings`, registro en `InfrastructureExtensions`).
- Contrato: `AvaluoResult { Source, Found, RawValue, Value (×1000), Currency, Message }`.
- `GetSuggestedCommercialValueHandler` corre **todos los providers configurados en paralelo**, tolera fallo parcial y compone `{ sources[], sugerido }`. Fasecolda real; base gravable + ML mock.
- **Pros:** contrato natural para valor + desglose; paralelo + fallo parcial nativo (AC#3); separa "avalúo" (valor) de "consulta" (verificación); patrón reconocible (espeja `IConsultationProvider`); extensible sin tocar el handler.
- **Cons:** segunda abstracción de proveedor (justificada por semántica distinta); algo más de código inicial.
- **Esfuerzo:** M · **Riesgos:** bajo.

### Opción 3 — Sin abstracción: cliente Fasecolda directo + stubs inline
`FasecoldaAvaluoClient` llamado directo por el handler; base gravable/ML como métodos mock inline.
- **Pros:** mínimo código; rápido para el deadline.
- **Cons:** no extensible (agregar proveedor = tocar el handler); sin registry ni toggle uniforme; contradice ADR-0020 y el espíritu multiproveedor de FEATURE-03; deuda técnica inmediata.
- **Esfuerzo:** S · **Riesgos:** rework al agregar el 2º proveedor real.

## Decisión

**Opción 2.** Es la única que modela con naturalidad el desglose multi-fuente + valor sugerido y la agregación **paralela con fallo parcial** (AC#3), sin degradar el contrato de consultas de identidad/vehículo. Reutiliza >70% de las convenciones de ADR-0020 (registry, Options, HttpClient tipado, toggle mock/real), por lo que es familiar y de bajo riesgo, y deja base gravable/ML activables por configuración (FEATURE-03 §6).

## Sequence Diagram

```mermaid
sequenceDiagram
    participant FE as CommercialForm (FE)
    participant API as GET .../commercial/suggested-value
    participant H as GetSuggestedCommercialValueHandler
    participant FA as FasecoldaAvaluoProvider (real)
    participant BG as BaseGravableProvider (mock)
    participant ML as MercadoLibreProvider (mock)
    participant FX as Fasecolda API (externa)

    FE->>API: GET suggested-value (X-Tenant-Id)
    API->>H: instanceId, tenantId
    H->>H: lee field_values (vin, cilindraje, combustible, pasajeros, año)
    par En paralelo (tolera fallo parcial)
        H->>FA: GetAvaluoAsync(ctx)
        FA->>FX: busquedaVin(vin) → token → consultabycodigo
        FX-->>FA: ficha + valorModelo
        FA-->>H: {found, value = valorModelo(año)*1000}
    and
        H->>BG: GetAvaluoAsync(ctx) (mock)
        BG-->>H: {found, value}
    and
        H->>ML: GetAvaluoAsync(ctx) (mock)
        ML-->>H: {mediana, muestras}
    end
    H->>H: compone sugerido (Fasecolda principal) + sources[]
    H-->>FE: { sources[], sugerido }
    FE->>FE: pinta tarjeta; usuario Acepta
    FE->>API: PUT commercial (valorVenta, valueOrigin=suggestion, suggestedSource)
```

## Contrato API (OpenAPI — `contracts/openapi/core-api.v1.yaml`)

Nuevo endpoint (borrador):

```yaml
/api/v1/tramites/instances/{id}/commercial/suggested-value:
  get:
    tags: [Tramites]
    summary: Sugerencia de valor comercial (avalúo multi-fuente)
    parameters:
      - { name: id, in: path, required: true, schema: { type: string, format: uuid } }
      - { name: X-Tenant-Id, in: header, required: true, schema: { type: string, format: uuid } }
    responses:
      '200':
        description: Desglose por fuente + valor sugerido
        content:
          application/json:
            schema: { $ref: '#/components/schemas/SuggestedCommercialValue' }
      '404': { description: Instancia no encontrada }
components:
  schemas:
    SuggestedCommercialValue:
      type: object
      properties:
        sugerido: { type: integer, format: int64, nullable: true, description: Valor sugerido en COP (Fasecolda principal) }
        fuentePrincipal: { type: string, nullable: true, example: fasecolda }
        sources:
          type: array
          items: { $ref: '#/components/schemas/AvaluoSource' }
    AvaluoSource:
      type: object
      properties:
        source: { type: string, enum: [fasecolda, base_gravable, mercado_libre] }
        status: { type: string, enum: [ok, no_data, error] }
        value: { type: integer, format: int64, nullable: true, description: Valor en COP }
        currency: { type: string, default: COP }
        message: { type: string, nullable: true }
        detalle: { type: object, nullable: true, description: "p.ej. Mercado Libre: { mediana, muestras }" }
```

`CommercialDto` (PUT) se extiende (contrato congelado → cambio versionado): `valueOrigin` (`suggestion|manual`), `suggestedSource`, `suggestedValue`.

## Modelo de datos (conceptual + DDL de referencia)

La consulta multi-fuente se calcula **on-demand** (no se persiste el breakdown completo). Solo se persiste la **trazabilidad del valor aceptado** (AC#4), extendiendo la entidad comercial 1:1:

```sql
-- ALTER (materializa el database-agent con Up/Down y checklist §A)
ALTER TABLE tramites.procedure_instance_commercial
  ADD COLUMN suggested_value  numeric(15,2) NULL,
  ADD COLUMN suggested_source varchar(30)   NULL,   -- fasecolda | base_gravable | mercado_libre
  ADD COLUMN value_origin     varchar(20)   NULL;    -- suggestion | manual
```

Datos de prueba (fasecolda.md §"migraciones para datos de prueba"): tabla de fixtures mock **gated a DEV/QA** (patrón `HU10200_DevSeed`), leída por los providers mock (y por el mock de Fasecolda cuando `FasecoldaMode≠real`):

```sql
-- tramites.avaluo_mock_values (seed DEV/QA) — vin/placa → valor por fuente
CREATE TABLE IF NOT EXISTS tramites.avaluo_mock_values (
  id uuid PRIMARY KEY DEFAULT uuidv7(),
  match_key varchar(32) NOT NULL,   -- vin o placa
  source    varchar(30) NOT NULL,   -- fasecolda | base_gravable | mercado_libre
  value_cop numeric(15,2) NOT NULL,
  CONSTRAINT uq_avaluo_mock UNIQUE (match_key, source)
);
-- Seed incluye el fixture validado: VIN 93Y9SR333RJ563653 → fasecolda 105600000 (Renault Oroch 2024)
```

`external_data_sources.FASECOLDA` ya existe → migración **data-only** (estilo HU10201) que hace `UPDATE ... external_refs = '{"provider":"fasecolda"}'::jsonb WHERE code='FASECOLDA'`.

## Archivos a crear / modificar

**Backend — `services/core-api` (Application):**
- `UseCases/Avaluos/IAvaluoProvider.cs` (nuevo) — `Key` + `GetAvaluoAsync(AvaluoContext, ct)`.
- `UseCases/Avaluos/AvaluoContracts.cs` (nuevo) — `AvaluoContext`, `AvaluoResult`, `SuggestedCommercialValue`, `AvaluoSource`.
- `UseCases/Avaluos/FasecoldaAnalysisResponse.cs` + `FasecoldaAvaluoMapper.cs` (nuevo) — DTOs + normalización ×1000.
- `UseCases/Avaluos/GetSuggestedCommercialValueQuery.cs` (nuevo) — handler paralelo, fallo parcial.
- `UseCases/ProcedureInstances/CommercialCommand.cs` (modif) — `CommercialDto` + `value_origin/suggested_*`.
- `DependencyInjection.cs` (modif) — registrar handler.

**Backend (Infrastructure):**
- `Avaluos/FasecoldaAvaluoProvider.cs` (nuevo, `Key="fasecolda"`, real+mock) + `FasecoldaOptions.cs`.
- `Avaluos/MockBaseGravableAvaluoProvider.cs` + `Avaluos/MockMercadoLibreAvaluoProvider.cs` (nuevos).
- `Avaluos/AvaluoProviderRegistry.cs` (nuevo) + `AvaluoProviderModeOptions.cs` (o extender `ConsultationProviderModeOptions` con `FasecoldaMode`).
- `InfrastructureExtensions.cs` (modif) — `AddAvaluoProviders(...)`: `Configure<FasecoldaOptions>`, 2× `AddHttpClient` (host VIN + host token/consulta), registro `IAvaluoProvider`, registry, toggle.
- `Persistence/Configurations/Tramites/ProcedureInstanceCommercialConfiguration.cs` (modif).
- `Migrations/{ts}_FEATURE10707_CommercialValuationTrace.cs` (ALTER) + `{ts}_FEATURE10707_AvaluoMockSeed.cs` (data-only) + `.sql` en `Persistence/Sql/Ddl/`.

**Backend (Api):**
- `Endpoints/Tramites/CommercialEndpoints.cs` (modif) — `MapGet(".../commercial/suggested-value")`.

**Config:**
- `appsettings.json` + `appsettings.Development.json` (modif) — `Consultations:FasecoldaMode` + sección `Fasecolda` (URLs/timeout). `.env.fasecolda.example` (nuevo). Credenciales por User Secrets/env.

**Frontend — `frontend`:**
- `lib/api/tramites-client.ts` (modif) — `getSuggestedCommercialValue(id, tenantId)`.
- `lib/api/types/procedure-runtime.ts` (modif) — tipos `SuggestedCommercialValue`, `AvaluoSource`; `CommercialData` + `valueOrigin`.
- `components/operacion/AvaluoComercialCard.tsx` (nuevo) — tarjeta desglose + Aceptar.
- `components/operacion/CommercialForm.tsx` (modif) — montar la tarjeta sobre el campo "Valor de venta".

## Notas operativas por agente

- **Database Agent:** ALTER de `procedure_instance_commercial` (Up/Down, RLS ya heredada por la tabla) + tabla `avaluo_mock_values` seed gated DEV/QA + migración data-only del binding `external_refs` de FASECOLDA. Validar con `db-schema-validator` (§A tablas nuevas, §B repos). No secretos en `base_url`.
- **Backend Agent:** implementar `IAvaluoProvider` + Fasecolda real (token cacheado por `expires_in`, ×1000, filtros desde `field_values`, distinguir 404/timeout/5xx≠"no data") y mocks; handler paralelo con `Task.WhenAll` + tolerancia a fallo parcial; extender PUT commercial. Seguir checklist §B.
- **Frontend Agent:** tarjeta "Avalúo comercial" fiel al design system (`flit-design-guardian`); skeleton mientras carga; Aceptar setea `valorVenta` (mismo setter) y marca `valueOrigin=suggestion`; nunca bloquear el paso si falta una fuente.
- **QA Agent:** TCs para AC#1–6: match feliz, VIN sin coincidencia (no bloquea), fuente caída, aceptar vs modificar, trazabilidad persistida, ×1000. Fixture VIN 93Y9SR333RJ563653.
- **Security Agent:** credenciales Fasecolda fuera del repo (User Secrets/env); rotar las expuestas; no loguear token ni credenciales; Habeas Data N/A (dato de vehículo, no personal).
- **Infra Agent:** migraciones se aplican al arranque (`Program.cs` `Database.Migrate()`); inyectar `Fasecolda__*` / `Consultations__FasecoldaMode` por env en QA/PDN.

---

## ADR-0029 (BORRADOR — se versiona en `services/core-api/docs/adr/` tras aprobación)

**ADR-0029: Capa de avalúo comercial multi-proveedor (agregación paralela)** · Status **Propuesto** · extiende (no supersede) **ADR-0020**.

- **Contexto:** se necesita sugerir valor de venta agregando varias fuentes de avalúo, con desglose y tolerancia a fallo parcial — semántica distinta a la verificación de identidad/vehículo de ADR-0020 (failover secuencial, checks).
- **Decisión:** introducir `IAvaluoProvider` + `AvaluoProviderRegistry` y un handler de **agregación paralela**, reutilizando las convenciones de ADR-0020 (Key, Options, HttpClient tipado, toggle mock/real). Fasecolda real por VIN; base gravable y Mercado Libre mock activables por config.
- **Alternativas:** (1) reusar `IConsultationProvider`+`ConsultationKind` avaluo; (2) **nueva abstracción `IAvaluoProvider`** (elegida); (3) cliente directo sin abstracción. (Pros/cons arriba.)
- **Tradeoff aceptado:** una segunda abstracción de proveedor a cambio de no degradar el contrato de consultas y de modelar correctamente valor+desglose+paralelo.
- **Consecuencias:** +extensibilidad multi-fuente; +separación de concerns; −un patrón más que mantener. Cambios operativos: nueva sección `Fasecolda` + toggle `FasecoldaMode` en appsettings; ALTER comercial; seed mock DEV/QA.
- **Relacionados:** ADR-0020 (consultas multiproveedor), ADR-0018 (modelo de datos).
