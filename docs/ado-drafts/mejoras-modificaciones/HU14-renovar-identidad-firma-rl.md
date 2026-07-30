# HU14 — [FULLSTACK] Renovación de la identidad o la firma del baúl vencidas de un representante legal

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11059** |
| Commit | `5354d155` |
| Ajuste origen | `modificaciones.txt:9` |

## Descripción

**Como** administrador del directorio de representantes legales
**Quiero** renovar la validación de identidad o la firma del baúl de un representante cuando están vencidas
**Para** que sus trámites puedan firmarse sin crear un representante nuevo

## Criterios de aceptación

```gherkin
Escenario: identidad vencida
  Dado un representante legal cuya validación de identidad está vencida
  Cuando el administrador abre su detalle
  Entonces se indica que está vencida y se ofrece renovarla

Escenario: renovar la identidad
  Dado un representante con la identidad vencida
  Cuando el administrador renueva la validación
  Entonces se inicia una validación nueva y el estado pasa a en proceso

Escenario: firma del baúl vencida
  Dado un representante cuya firma del baúl está vencida
  Cuando el administrador abre su detalle
  Entonces se indica que está vencida y se ofrece actualizarla

Escenario: vigencia en curso
  Dado un representante con identidad aprobada y vigente
  Cuando el administrador abre su detalle
  Entonces no se ofrece renovar y se informa hasta cuándo es válida
```

## Estado actual del código

`AdminLegalRepresentativeIdentityEndpoints.cs` (SuperAdmin, ADR-0034) ya expone:

- `POST …/identity/send` — inicia la validación (`:30`).
- `POST …/identity/resend` — reenvía, **"respeta la vigencia: no reenvía si ya hay aprobada y
  vigente"** (`:41`).

La respuesta ya devuelve `status`, `captureUrl`, `validUntil` y `reused` (`:112-120`), así que el dato
de vigencia está disponible.

⇒ **Lo que falta es el camino de "está vencida, renuévala"** y su superficie en UI. Revisar en
`AdminIdentityValidationService.ResendAsync` qué hace exactamente cuando la última validación está
**expirada** (¿reutiliza?, ¿crea nueva?) antes de decidir si el backend necesita cambios o solo la UI.

Firma del baúl: el baúl vive como sección dentro de la pestaña "Representantes legales"
(`CompanyConfigTabs.tsx:61-64`, HU #10904 / ajustes #10929); la vigencia de la firma se calcula ya en el
lookup por NIT (`FirmaVigente`).

## Archivos previstos

- `frontend/components/admin/companies/legal-representatives/LegalRepresentativeDetailModal.tsx`
- `frontend/components/admin/companies/legal-representatives/RepresentativesAndVaultTab.tsx`
- `services/core-api/src/Flit.Admin.Application/Identity/AdminIdentityValidationService.cs` (si el
  reenvío no cubre el caso vencido)
- Tests: `services/core-api/tests/Flit.Admin.Tests/Identity/`, `frontend/…/__tests__/`
