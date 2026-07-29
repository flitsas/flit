# HU03 — [BACKEND] Ajuste automático del nombre de empresa largo en los campos del FUR

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | Pendiente |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | _pendiente de registro_ |
| Ajuste origen | `modificaciones.txt:3` (según aclaración del PO) |

## Aclaración del alcance

El ajuste escrito decía *"en el último paso FUR, en la sección del comprador no llena los datos del
representante legal cuando es NIT"*. **El PO aclaró que el FUR se mantiene tal cual está**: el único
defecto es que **cuando el nombre de la empresa es muy largo el texto se desborda del campo nombre**.

No se añaden datos del representante legal al FUR (el formulario oficial no tiene casillas para ello:
el manifest solo declara apellidos, nombre, tipo y número de documento).

## Descripción

**Como** gestor que radica trámites de personas jurídicas
**Quiero** que la razón social larga quepa dentro de su campo en el FUR
**Para** entregar al organismo de tránsito un formulario legible y sin texto desbordado

## Criterios de aceptación

```gherkin
Escenario: razón social que excede el ancho del campo
  Dado un trámite cuyo comprador es una persona jurídica con razón social más larga que el campo de nombre del FUR
  Cuando se genera el FUR
  Entonces el texto se ajusta para quedar dentro de los límites del campo
  Y no se superpone con los campos vecinos

Escenario: razón social que cabe
  Dado un trámite cuya razón social cabe en el campo
  Cuando se genera el FUR
  Entonces el texto se imprime con el tamaño y la posición calibrados actuales

Escenario: sección del propietario
  Dado un trámite de traspaso cuyo vendedor es persona jurídica con razón social larga
  Cuando se genera el FUR
  Entonces el ajuste aplica igual en la sección del propietario
```

## Notas técnicas

`FurFieldMapper.NameParts` (`:435`) mete la razón social **completa** en la casilla de nombre
(`vehicle_owner_name` / `vehicle_buyer_name`) con apellidos vacíos, decisión de la HU #10688. El
overlay la pinta con el cuerpo declarado en el manifest.

## ⚠️ Restricción

El manifest del FUR está **calibrado en milímetros** (HU #10921, plantilla 792 recalibrada). El ajuste
va en el **renderer** (`FurOverlayRenderer`), usando el ancho declarado del campo para reducir el
cuerpo o partir el texto. **No recalibrar el manifest ni mover posiciones**, o se rompe la calibración
de las tres plantillas (automotor / maquinaria / remolques).

Decidir en implementación entre reducir cuerpo (shrink-to-fit) o partir en dos líneas: el recuadro del
FUR es de una sola línea, así que lo previsible es shrink-to-fit con un mínimo legible.

## Archivos previstos

- `services/core-api/src/Flit.Infrastructure/Documents/Fur/FurOverlayRenderer.cs`
- `services/core-api/src/Flit.Infrastructure/Documents/Fur/FurFieldModels.cs` (si el ancho del campo
  necesita exponerse al renderer)
- Tests: `services/core-api/tests/Flit.Infrastructure.Tests/Documents/`
- Verificación visual: `services/core-api/artifacts/render-documentos/`
