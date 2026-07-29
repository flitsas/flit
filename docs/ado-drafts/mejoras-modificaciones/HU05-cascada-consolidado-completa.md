# HU05 — [BACKEND] Cascada del consolidado extendida a compraventa, mandato y solicitud virtual

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:49` |
| Depende de | HU06 (no generar sobre trámites aprobados) |
| Bloquea a | HU07 (ocultar botones exige que la cascada cubra todo) |

## Descripción

**Como** gestor que prepara un expediente
**Quiero** que al generar el consolidado se produzcan todos los documentos que el trámite requiere
**Para** no tener que generar cada documento por separado antes de consolidar

## Criterios de aceptación

```gherkin
Escenario: documentos faltantes al consolidar
  Dado un trámite al que le faltan documentos generables aplicables
  Cuando el gestor genera el expediente consolidado
  Entonces el sistema los genera en cascada y los incluye en el consolidado

Escenario: documento no aplicable
  Dado un trámite cuyo organismo de tránsito no exige mandato
  Cuando el gestor genera el expediente consolidado
  Entonces el consolidado se genera sin mandato y sin error

Escenario: fallo en un documento de la cascada
  Dado un documento de la cascada que no se puede generar por datos faltantes
  Cuando el gestor genera el expediente consolidado
  Entonces el sistema informa qué documento no se pudo generar y por qué
```

## Estado actual del código

`GenerarConsolidadoHandler` (`ConsolidadoCommand.cs:54`) ya implementa la cascada para:

- **FUR** — lo regenera para que salga con fecha vigente antes de consolidar (HU #10860, cascada β de
  ADR-0032).
- **Impronta** — la genera cuando falta, para no obligar al gestor a un paso previo (HU #11017).

Falta extender el mismo patrón a **compraventa**, **mandato** y **solicitud de trámite virtual**.

## ⚠️ Trampa

Mandato y solicitud virtual dependen de configuración **por organismo de tránsito**
(`MandatoTemplateResolver`, matriz de la HU #10917). Un trámite cuyo OT no los exige **no debe fallar**
al consolidar: la cascada tiene que ser tolerante, exactamente como ya lo es con la impronta.

Distinguir tres situaciones y no confundirlas:
1. **No aplicable** → se omite en silencio, consolidado OK.
2. **Aplicable y generable** → se genera y se incluye.
3. **Aplicable pero con datos faltantes** → error explicable que nombra el documento (AC3).

## Archivos previstos

- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/ConsolidadoCommand.cs`
- Puertos de generación análogos a `IImprontaAutoGenerator`
- `services/core-api/src/Flit.Api/Endpoints/Tramites/ConsolidadoEndpoints.cs` (nuevos códigos de error)
- Tests: `services/core-api/tests/Flit.Tramites.Application.Tests/UseCases/ProcedureInstances/`
