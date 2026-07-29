# HU07 — [FRONTEND] Consolidado como único disparador de generación en el paso FUR

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:49` |
| Depende de | HU05 (cascada completa), HU06 (guard por estado) |

## Descripción

**Como** gestor que prepara un expediente
**Quiero** un solo botón de generación en el paso FUR
**Para** no equivocarme sobre qué documento falta generar ni en qué orden

## Criterios de aceptación

```gherkin
Escenario: paso FUR de un trámite editable
  Dado un trámite en un estado que permite generar documentación
  Cuando el gestor abre el paso FUR
  Entonces solo se ofrece la acción de generar el expediente consolidado
  Y no se muestran botones de generación de documentos individuales

Escenario: documentos ya generados
  Dado un trámite con documentos generados
  Cuando el gestor abre el paso FUR
  Entonces puede ver y descargar cada documento generado

Escenario: trámite aprobado
  Dado un trámite en estado aprobado
  Cuando el gestor abre el paso FUR
  Entonces no se ofrece ninguna acción de generación y sí la descarga de los documentos existentes
```

## Notas técnicas

Botones que siguen visibles hoy en `FirmaFurStep.tsx`:

| Botón | Línea | Acción |
|-------|-------|--------|
| "Generar FUR / certificado" / "Re-generar FUR / certificado" | `:1878-1890` | Ocultar |
| "Generar Improntas" | `:1638` | Ocultar |
| "Generar consolidado" / "Re-generar consolidado" | `:1938-1950` | **Se conserva: único disparador** |

El botón de solicitar firma de la compraventa ya se retiró en la HU #11019 (`:1175`), así que esa
parte del ajuste no requiere trabajo.

**Cuidado:** no basta con esconder los botones — la lista de documentos generados y su descarga deben
seguir visibles, y el texto explicativo de la sección debe dejar de prometer generación por pasos
(`:1810-1816` describe hoy el flujo antiguo).

## Archivos previstos

- `frontend/components/operacion/FirmaFurStep.tsx`
- Tests: `frontend/__tests__/firma-fur-step.test.tsx`
