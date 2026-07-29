# HU06 — [BACKEND] Bloqueo de regeneración documental cuando el trámite está aprobado

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11051** |
| Commit | `db22eb0d` |
| Ajuste origen | `modificaciones.txt:5` |
| Bloquea a | HU07, HU05 |

## Descripción

**Como** líder de operación
**Quiero** que la documentación de un trámite aprobado no se pueda regenerar desde el gestor
**Para** que el expediente aprobado conserve exactamente los documentos con los que el organismo de
tránsito lo aprobó

## Criterios de aceptación

```gherkin
Escenario: gestor intenta regenerar en trámite aprobado
  Dado un trámite en estado aprobado
  Cuando el gestor solicita generar o regenerar el FUR, el consolidado, la impronta o cualquier documento
  Entonces la petición se rechaza con un conflicto que explica que el trámite ya está aprobado

Escenario: trámite anulado
  Dado un trámite en estado anulado
  Cuando el gestor solicita generar documentación
  Entonces la petición se rechaza con el mismo criterio

Escenario: regeneración por aprobación del organismo de tránsito
  Dado un trámite que el organismo de tránsito aprueba
  Cuando el flujo de aprobación regenera la documentación definitiva
  Entonces la regeneración se completa con éxito

Escenario: trámite en proceso
  Dado un trámite en borrador, preparado o entregado
  Cuando el gestor solicita generar documentación
  Entonces la generación procede como hasta ahora
```

## Notas técnicas

- `TramiteEstado.Finales = [aprobado, anulado]` y `TramiteEstado.PermiteEdicionDatos`
  (`TramiteEstado.cs:66`) son los helpers de dominio de referencia.
- Hoy `ConsolidadoEndpoints.cs:34` solo contempla `migrado_solo_lectura`; no hay guard por estado.
- Rutas a cubrir: generación de FUR, consolidado, impronta y —tras HU05— compraventa, mandato y
  solicitud virtual.

## ⚠️ Trampa crítica

El propio enunciado del negocio dice que *"esta documentación ya se re-genera cuando el OT aprueba el
trámite"*. Es decir, **la aprobación regenera por diseño** (`ApproveOtClientProcedureCommand`). Si el
guard se aplica por estado sin distinguir quién invoca, **se rompe el flujo de aprobación del OT**.

El guard debe discriminar la invocación del **gestor** (endpoints de trámites) de la regeneración
**interna** del flujo de aprobación. Opciones a evaluar en implementación: parámetro explícito de
contexto en el handler, o aplicar el guard en la capa de endpoint del gestor y no en el handler
compartido.

## Implementación (commit `db22eb0d`)

Los llamadores se auditaron antes de decidir dónde poner el gate. `GenerarFurHandler.HandleAsync`
tiene **cinco llamadores internos del sistema** que corren legítimamente con el trámite ya en estado
final, y **uno solo del gestor**:

| Llamador | Contexto | ¿Debe seguir generando en estado final? |
|----------|----------|----------------------------------------|
| `AdminOtEndpoints.cs:954` (aprobación OT, HU #10996) | Sistema | **Sí** — regenera con las firmas definitivas |
| `AdminPlateRangesEndpoints.cs:117` (asignación de placa) | Sistema | Sí |
| `IdentityValidationCompletedConsumer.cs:76` | Sistema | Sí |
| `TransitionProcedureInstanceCommand.cs:46` | Sistema | Sí |
| `ConsolidadoCommand` vía `IExpedienteHotDocumentsRegenerator` | Mixto | Según el llamador |
| `FurEndpoints.cs:21` | **Gestor** | **No** |

⇒ El gate va en los **endpoints del gestor**, no en el handler compartido (habría roto los cinco).

| Archivo | Cambio |
|---------|--------|
| `Flit.Tramites.Domain/Tramites/Estados/TramiteEstado.cs` | `PermiteGeneracionDocumentalDelGestor` (= `!EsFinal`) |
| `Flit.Tramites.Domain/Tramites/Estados/TramiteEstadoErrores.cs` | `GeneracionBloqueadaEstadoFinal` |
| `Flit.Tramites.Application/…/GeneracionDocumentalGestorGuard.cs` | Guard nuevo; lectura ligera (`GetByIdAsync`, sin grafos) |
| `Flit.Tramites.Application/DependencyInjection.cs` | Registro scoped |
| `Flit.Api/Endpoints/Tramites/GeneracionEstadoProblem.cs` | Traducción a `ProblemDetails` (409 / 404) |
| `Flit.Api/Endpoints/Tramites/FurEndpoints.cs` | Gate aplicado |
| `Flit.Api/Endpoints/Tramites/ConsolidadoEndpoints.cs` | Gate aplicado |

**La impronta no necesitó cambio:** su gate `not_draft` (`AttachmentEndpoints.cs:219`) ya impide
generarla fuera de borrador/subsanación, así que añadir el nuevo gate habría sido redundante.

## Verificación

- `dotnet build Flit.slnx` → 0 errores, 0 advertencias.
- 19 tests nuevos: 11 de dominio (`GeneracionDocumentalEstadoTests`) + 8 del guard
  (`GeneracionDocumentalGestorGuardTests`), incluido uno que fija que el gate **no** cargue los grafos
  del expediente.
- Regresión: `Flit.Tramites.Domain.Tests` 351/351 y `Flit.Tramites.Application.Tests` 1179/1179.
