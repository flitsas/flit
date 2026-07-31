# HU18 — [FRONTEND] Carga y actualización de escrituras por compañía más intuitiva

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11063** |
| Commit | `5354d155` |
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

## Alcance — DECIDIDO con el PO

El ajuste decía "que sea más intuitivo" sin especificar. El PO eligió, sobre maqueta, la
**sección propia de escrituras**: tercera sección de la pestaña, hermana de Representantes y Baúl.

- Tabla por compañía: razón social + NIT · escritura · vigencia · estado + días restantes · acciones.
- Chip de días restantes **reutilizando** `deedVigenciaTone`/`deedVigenciaLabel` de
  `ActiveDeedsCollapse` (mismos umbrales que el wizard: ≤7 rojo, ≤30 ámbar).
- Las compañías **sin escritura se listan igual**, con acceso directo a cargarla: son justo las que hay
  que resolver.
- Reemplazo en un paso (el histórico lo conserva el backend). Alta general con selector de compañía
  solo cuando hay más de una.
- El detalle del representante **conserva** su vista de escrituras: responde a otra pregunta
  ("qué escrituras asoció ESTE representante"), no a "qué compañías están al día".

Descartado por ahora: arrastrar y soltar el PDF (el panel de carga existente ya funciona y añadirlo
habría duplicado el componente).

## Archivos previstos

- `frontend/components/admin/companies/legal-representatives/RepresentativesAndVaultTab.tsx`
- `frontend/components/admin/companies/legal-representatives/LegalRepresentativesTab.tsx`
- `frontend/components/operacion/ActiveDeedsCollapse.tsx` (reutilizar el chip de vigencia)
- Tests: `frontend/components/admin/companies/legal-representatives/__tests__/`
