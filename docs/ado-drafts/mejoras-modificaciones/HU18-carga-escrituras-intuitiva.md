# HU18 — [FRONTEND] Carga y actualización de escrituras por compañía más intuitiva

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 5 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:35` |

## Descripción

**Como** administrador del directorio de representantes legales
**Quiero** cargar o reemplazar la escritura de una compañía sin recorrer varios niveles
**Para** mantener las escrituras al día sin depender de conocer la navegación

## Criterios de aceptación

```gherkin
Escenario: cargar una escritura
  Dado un administrador en la sección de representantes legales
  Cuando carga la escritura de una compañía
  Entonces puede hacerlo desde la propia sección indicando vigencia y compañía en un solo paso

Escenario: reemplazar una escritura
  Dado una compañía con escritura registrada
  Cuando el administrador la reemplaza
  Entonces el documento anterior se conserva como histórico y el nuevo queda vigente

Escenario: vigencia visible
  Dado un listado de escrituras
  Cuando el administrador lo consulta
  Entonces cada escritura muestra su estado de vigencia y los días restantes

Escenario: archivo no admitido
  Dado un administrador que carga un archivo que no es PDF
  Cuando intenta guardarlo
  Entonces el sistema lo rechaza indicando el formato admitido
```

## Notas técnicas — el backend ya está completo

- `AdminDeedsEndpoints.cs:51` registra una escritura (PDF) **con vigencia y compañías**; `:60` la edita
  (descripción, vigencia, compañías, PDF opcional). Almacenamiento: `DeedDocumentStorage`.
- El consumo del wizard ya calcula vigencia: `GET /api/v1/tramites/deeds/active` devuelve
  `[{ nit, name, diasRestantes, vigenciaHasta }]` (`LegalRepresentativeConsumptionEndpoints.cs:29-30`),
  y `ActiveDeedsCollapse.tsx` ya lo pinta en el paso 1 del wizard.

⇒ **La HU es de UX, no de capacidades:** los datos y las operaciones existen; lo que falla es el
recorrido. Hoy el alta/edición vive **dentro del detalle del representante**, en la pestaña
"Representantes legales" (decisión de los ajustes de la HU #10929), que es justo lo que el negocio
describe como poco intuitivo.

## Punto a decidir con el PO

El ajuste dice "que sea más intuitivo" sin especificar. Propuesta a validar antes de implementar:
acceso directo a escrituras desde la propia pestaña (sin entrar al detalle del representante),
arrastrar y soltar el PDF, chip de vigencia con días restantes reutilizando el de `ActiveDeedsCollapse`
y reemplazo en un paso conservando histórico. **Conviene mostrar una maqueta antes de codificar.**

## Archivos previstos

- `frontend/components/admin/companies/legal-representatives/RepresentativesAndVaultTab.tsx`
- `frontend/components/admin/companies/legal-representatives/LegalRepresentativesTab.tsx`
- `frontend/components/operacion/ActiveDeedsCollapse.tsx` (reutilizar el chip de vigencia)
- Tests: `frontend/components/admin/companies/legal-representatives/__tests__/`
