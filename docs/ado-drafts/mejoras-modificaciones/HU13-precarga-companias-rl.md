# HU13 — [FULLSTACK] Precarga de las compañías asociadas al editar un representante legal

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11058** |
| Commit | `5354d155` |
| Ajuste origen | `modificaciones.txt:7` |

## Descripción

**Como** administrador del directorio de representantes legales
**Quiero** ver todas las compañías asociadas al abrir un representante legal para editarlo
**Para** no perder asociaciones existentes al guardar los cambios

## Criterios de aceptación

```gherkin
Escenario: representante con varias compañías
  Dado un representante legal asociado a varias compañías
  Cuando el administrador lo abre para editar
  Entonces el formulario precarga todas sus compañías asociadas

Escenario: guardar sin tocar las asociaciones
  Dado un representante con compañías precargadas
  Cuando el administrador cambia solo un dato de contacto y guarda
  Entonces las asociaciones se conservan intactas

Escenario: representante sin compañías
  Dado un representante sin compañías asociadas
  Cuando el administrador lo abre para editar
  Entonces el formulario lo indica y permite asociar compañías
```

## Notas técnicas

- La relación representante ↔ varias compañías existe desde la migración
  `20260724232444_HU10932_LegalRepresentativeCompanies`.
- Formulario: `frontend/components/admin/companies/legal-representatives/LegalRepresentativesFormPanel.tsx`.
- Detalle: `LegalRepresentativeDetailModal.tsx`; listado: `LegalRepresentativesTab.tsx`.
- Verificar en implementación si el endpoint de lectura ya devuelve la colección de compañías o si hay
  que ampliarlo (de ahí el tipo FULLSTACK). El lookup por NIT del wizard ya expone datos por
  representante (`FindRepresentativeByNitResponse`), pero el de administración es otro camino.

## Riesgo — CONFIRMADO, con un matiz

El riesgo era real pero **no donde lo suponía el plan**:

- Las **asociaciones** ya se conservaban: `DbLegalRepresentativeReader` proyecta el puente completo
  (`LegalRepresentativeCompanies`) y el formulario ya precargaba `companies[]`.
- Lo que se perdía era el **contacto de cada compañía**. El formulario mapeaba solo `nit` y `name`
  (dejando email/dirección/ciudad/teléfono en blanco), y `UpsertRepresentedCompanyAsync` →
  `RepresentedCompany.UpdateDetails` normaliza los vacíos a `null`. Resultado: **cada edición de un
  representante borraba el contacto de todas sus compañías**. El propio comentario del formulario
  documentaba la carencia sin advertir la consecuencia.

⇒ Tratado como corrección de defecto. **Pendiente de negocio:** revisar si hay datos ya afectados
en DEV.

## Archivos previstos

- `frontend/components/admin/companies/legal-representatives/LegalRepresentativesFormPanel.tsx`
- `services/core-api/src/Flit.Admin.Application/Companies/LegalRepresentatives/` (si falta proyectar)
- Tests: `frontend/components/admin/companies/legal-representatives/__tests__/`
