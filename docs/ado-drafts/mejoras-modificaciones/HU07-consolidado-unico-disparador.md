# HU07 — [FRONTEND] Consolidado como único disparador de generación en el paso FUR

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11052** |
| Commit | `298111f4` |
| Ajuste origen | `modificaciones.txt:49` |
| Depende de | HU06 (guard por estado) — HU05 no era bloqueante, ver abajo |

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

## Implementación (commit `298111f4`)

| Cambio | Detalle |
|--------|---------|
| Botón "Generar FUR / certificado" | Retirado; `handleGenerate` y sus estados eliminados |
| Botón "Generar Improntas" | Retirado; `ImprontaSection` queda como aviso informativo y desaparece cuando ya hay impronta |
| Botón del consolidado | Único disparador. Renombrado a "Generar / Re-generar expediente consolidado" |
| Guardado previo | `handleGenerateConsolidado` hereda el `guardarCampos()` que hacía el botón del FUR |
| Textos | La sección ya no promete generación por pasos; explica que todo sale con el consolidado |
| Estado final | Con `aprobado`/`anulado` no se ofrece generar; se explica que la documentación es definitiva |

**HU05 no era bloqueante como suponía el plan.** La cascada de backend ya cubre FUR e impronta
(HU #10860 / #11017), que son los dos únicos documentos que tenían botón en el paso. Mandato y
solicitud virtual no se generan desde el wizard, y la firma de compraventa ya se había retirado en la
HU #11019. Por eso esta HU pudo cerrarse antes que HU05, que sigue siendo necesaria para que esos
documentos entren en la cascada.

**Riesgo aceptado:** la cascada de impronta es best-effort y depende de Kyverum RUNT. Sin botón manual,
un fallo del proveedor se reintenta volviendo a generar el consolidado (la cascada se re-dispara), así
que no se pierde capacidad; pero el reintento ya no es explícito.

## Verificación

`npx tsc --noEmit` y `eslint` limpios · suite `firma-fur-step` **31/31**, con los tests de generación
manual reescritos sobre el comportamiento nuevo (incluye el orden guardar-antes-de-generar y el caso de
trámite aprobado).

## Archivos

- `frontend/components/operacion/FirmaFurStep.tsx`
- `frontend/__tests__/firma-fur-step.test.tsx`
