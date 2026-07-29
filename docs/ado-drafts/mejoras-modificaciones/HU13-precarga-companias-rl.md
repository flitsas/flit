# HU13 — [FULLSTACK] Precarga de las compañías asociadas al editar un representante legal

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Story Points | 5 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
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

## Riesgo

**Pérdida silenciosa de datos:** si el formulario hoy envía la lista de compañías y la precarga está
incompleta, un guardado normal puede estar **borrando asociaciones**. Comprobarlo antes de tocar la UI
y, si se confirma, tratarlo como corrección de defecto (y revisar si hay datos ya afectados en DEV).

## Archivos previstos

- `frontend/components/admin/companies/legal-representatives/LegalRepresentativesFormPanel.tsx`
- `services/core-api/src/Flit.Admin.Application/Companies/LegalRepresentatives/` (si falta proyectar)
- Tests: `frontend/components/admin/companies/legal-representatives/__tests__/`
