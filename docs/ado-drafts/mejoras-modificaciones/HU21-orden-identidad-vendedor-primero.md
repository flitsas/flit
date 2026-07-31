# HU21 — [FRONTEND] Vendedor antes que comprador en el resumen de validación de identidad

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | **Implementada (pendiente de verificar con tests)** |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:44` |

## Descripción

**Como** gestor que revisa el paso FUR
**Quiero** ver el resumen de validación de identidad con el vendedor primero
**Para** leerlo en el mismo orden que el expediente y el resumen de firmas

## Criterios de aceptación

```gherkin
Escenario: traspaso con dos partes
  Dado un trámite de traspaso con validación de identidad de ambas partes
  Cuando el gestor abre el paso FUR
  Entonces el resumen de validación de identidad muestra primero el vendedor y luego el comprador

Escenario: matrícula inicial
  Dado un trámite de matrícula inicial
  Cuando el gestor abre el paso FUR
  Entonces el resumen muestra la única parte del trámite sin cambios de comportamiento

Escenario: consistencia con el resumen de firmas
  Dado un trámite de traspaso
  Cuando el gestor compara el resumen de identidad con el de firmas
  Entonces ambos presentan las partes en el mismo orden
```

## Notas técnicas

El resumen de firmas del paso FUR (`FirmaFurStep.tsx:1051`) y el expediente (`ExpedienteVisor`) ya
ordenaban saliente antes que entrante desde las HU #11019 / #11020. El resumen de identidad era el
único que quedaba invertido, en dos componentes distintos.

## Implementación

| Archivo | Cambio |
|---------|--------|
| `frontend/components/operacion/BiometricStep.tsx` | `partesFor` pasa de `['comprador','vendedor']` a `['vendedor','comprador']` en traspaso |
| `frontend/components/operacion/IdentityStatusPanel.tsx` | `buildOutcomeRows` iteraba los actores en orden de llegada; se añade `PARTE_ORDEN` + `ordenarPartes` |
| `frontend/__tests__/biometric-step.test.tsx` | Test nuevo: en traspaso los grupos aparecen como `['Biométrica Vendedor', 'Biométrica Comprador']` |

**Alcance ampliado a propósito:** `IdentityStatusPanel` se usa en la página de detalle del trámite
(`app/tramites/[instanceId]/page.tsx:49`), no en el paso FUR. Se ajusta igual por consistencia, ya que
el usuario ve el mismo resumen en los dos sitios.

## Verificación pendiente

```
cd frontend && pnpm test -- --testPathPatterns "biometric-step"
```
