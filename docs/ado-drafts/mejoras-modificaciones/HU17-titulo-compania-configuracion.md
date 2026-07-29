# HU17 — [FRONTEND] Identificación de la compañía en el administrador de configuración

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 2 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:33` |

## Descripción

**Como** administrador que configura compañías
**Quiero** ver en todo momento qué compañía estoy modificando
**Para** no cambiar por error parámetros de otra compañía

## Criterios de aceptación

```gherkin
Escenario: encabezado de la compañía
  Dado un administrador que abre la configuración de una compañía
  Cuando se muestra la pantalla
  Entonces un encabezado identifica la compañía con su razón social y su NIT

Escenario: cambio de pestaña
  Dado un administrador dentro de la configuración de una compañía
  Cuando cambia de pestaña
  Entonces el encabezado con la compañía permanece visible

Escenario: confirmación de guardado
  Dado un administrador que guarda cambios de configuración
  Cuando se abre la ventana de confirmación
  Entonces la compañía afectada queda identificada en la confirmación
```

## Notas técnicas

- Contenedor: `frontend/app/admin/companies/[tenantId]/page.tsx` sobre
  `frontend/components/admin/companies/CompanyConfigTabs.tsx`, que hoy **no imprime la compañía en
  ninguna pestaña** (siete pestañas: matrícula, traspasos, configuración, documentos, placas,
  representantes, historial).
- El riesgo que reporta el negocio es real: el `tenantId` va en la URL y nada en pantalla confirma
  sobre qué compañía se está guardando, mientras "Guardar todo" persiste con un único PUT atómico.
- El diálogo de confirmación es `SaveConfigDialog` (ya lista los cambios detectados): añadir ahí el
  nombre de la compañía es la mitad del valor de esta HU.
- Ubicar el encabezado por encima de la barra de pestañas para que sobreviva al cambio de pestaña.

## Archivos previstos

- `frontend/components/admin/companies/CompanyConfigTabs.tsx`
- `frontend/components/admin/companies/SaveConfigDialog.tsx`
- `frontend/app/admin/companies/[tenantId]/page.tsx`
- Tests: `frontend/components/admin/companies/__tests__/CompanyConfigTabs.test.tsx`
