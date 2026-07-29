# HU15 — [FULLSTACK] Renovación de la identidad vencida del mandatario del organismo de tránsito

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Story Points | 5 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:11` |

## Descripción

**Como** administrador de la configuración del organismo de tránsito
**Quiero** renovar la validación de identidad del mandatario cuando está vencida
**Para** que los mandatos del organismo se sigan firmando sin registrar otro mandatario

## Criterios de aceptación

```gherkin
Escenario: identidad del mandatario vencida
  Dado un mandatario del organismo de tránsito con la validación de identidad vencida
  Cuando el administrador abre su registro
  Entonces se indica que está vencida y se ofrece renovarla

Escenario: renovar la identidad del mandatario
  Dado un mandatario con la identidad vencida
  Cuando el administrador renueva la validación
  Entonces se inicia una validación nueva y el estado pasa a en proceso

Escenario: vigencia en curso
  Dado un mandatario con identidad aprobada y vigente
  Cuando el administrador abre su registro
  Entonces no se ofrece renovar y se informa hasta cuándo es válida
```

## Notas técnicas

- `AdminMandateSignerIdentityEndpoints.cs:44` ya expone `send` y `resend` con la misma semántica de
  vigencia que el representante legal.
- El mandatario del OT es una entidad **independiente** del representante legal de la compañía
  (ADR-0036, que supersede el ADR-0023 de exclusividad). No mezclar ambos directorios.
- Entidad: `Flit.Infrastructure/Persistence/Entities/Admin/MandateSigner.cs`; lectura:
  `DbMandateSignerReader`; migración de identidad: `20260724060049_HU10910_MandateSignerIdentity`.
- Antecedente relevante: la HU de "mandatario identidad + fix 500" del paquete anterior
  (Features #11000-11003) ya tocó este flujo — revisar qué quedó resuelto allí antes de implementar.

## Gemela de

[HU14](HU14-renovar-identidad-firma-rl.md) — misma mecánica sobre otro sujeto. Conviene implementarlas
seguidas y, si emerge lógica común de "vigencia + renovación", extraerla una sola vez.

## Archivos previstos

- `frontend/components/admin/…` (pestaña de mandatarios del hub OT)
- `services/core-api/src/Flit.Admin.Application/Identity/AdminIdentityValidationService.cs` (si aplica)
- Tests: `services/core-api/tests/Flit.Admin.Tests/Companies/MandateSigners/`
