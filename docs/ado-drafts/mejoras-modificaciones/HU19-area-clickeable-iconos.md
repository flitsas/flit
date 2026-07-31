# HU19 — [FRONTEND] Área clickeable de los botones de icono en las tablas

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 3 |
| Estado | **Implementada (pendiente de verificar con tests)** |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:38` |

## Descripción

**Como** usuario de las tablas de administración
**Quiero** que el botón de icono responda al clic en toda su superficie
**Para** no tener que apuntar con precisión para ejecutar una acción

## Criterios de aceptación

```gherkin
Escenario: clic en el centro del botón
  Dado una tabla con botones de acción de solo icono
  Cuando el usuario hace clic en el centro del botón o sobre el icono
  Entonces la acción se ejecuta

Escenario: tamaño del objetivo
  Dado un botón de acción de solo icono
  Cuando se inspecciona su área efectiva
  Entonces cumple el mínimo recomendado de accesibilidad

Escenario: aspecto sin cambios
  Dado las tablas con acciones de icono
  Cuando se aplica el ajuste
  Entonces el aspecto visual y el espaciado de la columna de acciones se conservan
```

## Causa raíz (hallazgo de la implementación)

El síntoma reportado —"el puntero no da clic cuando me ubico en el centro del botón y sobre el
icono"— **no** es un problema de propagación de eventos. `frontend/app/globals.css:233-247` define un
cursor personalizado: un SVG de 22×22 px con el hotspot declarado en `2 2` (la esquina). El punto que
recibe el clic queda por tanto hasta ~20 px arriba y a la izquierda del cuerpo visible de la flecha.
Con el objetivo anterior (icono de 16 px + `p-1.5` = 28 px), el usuario ve el puntero sobre el icono
mientras el clic real cae fuera del botón.

Llevar el área a 40 px absorbe ese desfase y además cumple WCAG 2.5.8 (Target Size Minimum). El icono
**no** cambia de tamaño: crece solo la superficie sensible.

## Implementación

Constante compartida `ICON_BUTTON_HIT_AREA` (`inline-flex items-center justify-center min-h-[40px]
min-w-[40px]`) exportada desde `RowActions.tsx` y aplicada en los tres puntos donde vivían botones de
solo icono con el padding antiguo.

| Archivo | Cambio |
|---------|--------|
| `frontend/components/atom/RowActions.tsx` | Define y exporta `ICON_BUTTON_HIT_AREA`; lo aplica a la columna de acciones unificada |
| `frontend/components/admin/transit-offices/OtUsersSection.tsx` | 7 botones de icono (editar, restaurar, reactivar, suspender temporal, desactivar, eliminar, cancelar invitación) + import |
| `frontend/components/atom/modules/users/ResendInvitationButton.tsx` | Botón de reenviar invitación + import |

## Verificación pendiente

```
cd frontend && pnpm test -- --testPathPatterns "ot-users|row-actions"
```

No se esperan roturas: los tests existentes consultan por nombre accesible (`aria-label`), que no
cambia.
