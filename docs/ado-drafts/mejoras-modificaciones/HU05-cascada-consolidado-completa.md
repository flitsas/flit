# HU05 — [BACKEND] Cascada del consolidado extendida a compraventa, mandato y solicitud virtual

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11050** |
| Commit | `3163fc19` |
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

## Hallazgo: AC1 y AC2 ya se cumplían

El plan asumía que había que extender la cascada a compraventa, mandato y solicitud virtual. **Ya
estaban dentro.** `GenerarFurHandler` genera, junto al FUR:

| Documento | Regla | Referencia |
|-----------|-------|------------|
| Compraventa | Siempre en traspaso | ADR-0035 |
| Solicitud de trámite virtual | Siempre (natural y jurídica) | ADR-0036, HU #10914 |
| Contrato de mandato | Condicional: jurídica siempre, natural según el OT | ADR-0036, HU #10915 |

Y el consolidado lo invoca vía `IExpedienteHotDocumentsRegenerator`, que está registrado al propio
`GenerarFurHandler`. Así que los documentos aplicables ya se generaban en cascada (AC1) y los no
aplicables no rompían el consolidado (AC2).

## Lo que sí faltaba: AC3

El resultado de la regeneración **se descartaba** (`await` sin recoger el valor devuelto), así que un
documento que no se podía generar simplemente no aparecía en el expediente y el gestor no tenía forma
de saber por qué. Con la HU #11052 eso pasa de incómodo a crítico: ya no hay botones para generar
documento por documento.

`GenerarConsolidadoResult.AvisosCascada` devuelve `"documento: motivo"`, y el paso FUR lo traduce a
lenguaje del gestor (proveedor no disponible, falta el organismo…). **No bloquea:** el consolidado se
entrega igual, la misma decisión que tomó la HU #11017 con los documentos obligatorios faltantes.

## Verificación

3 tests de backend (fallo de la cascada, fallo de la impronta, sin fallos) + 1 de frontend ·
`Consolidado` 37/37 · paso FUR 32/32.

## Archivos

- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/ConsolidadoCommand.cs`
- `frontend/lib/api/types/procedure-runtime.ts`, `frontend/components/operacion/FirmaFurStep.tsx`
- Tests: `ConsolidadoHandlerTests.cs`, `frontend/__tests__/firma-fur-step.test.tsx`
