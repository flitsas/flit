# HU01 — [BACKEND] Estampa de firma sobre la línea en mandato y solicitud de trámite virtual

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11046** |
| Commit | `c436b4bf` |
| Implementación | `FlitFirmaBlock` (nuevo, compartido por los dos generadores) compone estampa → línea → datos. `ResolverEstampa` deja la prioridad del baúl (HU #11031) como función pura testeable. 3 tests sobre esa decisión |
| Ajuste origen | `modificaciones.txt:19` y `:22` (describen el mismo cambio de posición) |
| Bloquea a | HU16 (decide qué firma se plasma) |

## Descripción

**Como** gestor documental de FLIT
**Quiero** que la firma del mandato y de la solicitud de trámite virtual se plasme sobre la línea de firma
**Para** que el documento se lea como un documento firmado y no como una plantilla con la firma desplazada

## Criterios de aceptación

```gherkin
Escenario: firma por validación de identidad
  Dado un trámite con validación de identidad aprobada para la parte que firma
  Cuando se genera el mandato o la solicitud de trámite virtual
  Entonces la línea de firma se imprime sobre los datos del firmante
  Y la estampa de la firma electrónica se plasma sobre esa línea

Escenario: firma por baúl de firmas
  Dado un trámite en el que la parte que firma tiene firma vigente en el baúl
  Cuando se genera el mandato o la solicitud de trámite virtual
  Entonces la imagen de la firma y sus datos se plasman sobre la línea
  Y no se añade además el sello de la validación de identidad

Escenario: firma registrada con el trámite
  Dado un trámite cuya firma quedó registrada al radicar
  Cuando se regenera el documento
  Entonces se plasma la misma firma que quedó registrada con el trámite

Escenario: sin firma disponible
  Dado un trámite sin firma de baúl ni validación de identidad aprobada
  Cuando se genera el documento
  Entonces se imprime la línea de firma en blanco para firma manuscrita
```

## Estado actual del código

| Generador | Comportamiento hoy |
|-----------|--------------------|
| `SolicitudVirtualPdfGenerator.RenderFirmaSlot` (`:197`) | Pinta **imagen o línea**, nunca las dos; la línea es un `LineHorizontal` de 240 px |
| `SolicitudVirtualPdfGenerator.RenderSello` (`:214`) | El sello de identidad va **debajo** del bloque de datos del firmante |
| `MandatoPdfGenerator.RenderFirmaSlot` (`:304`) | Igual: imagen o guiones bajos |
| `MandatoPdfGenerator.RenderSello` (`:327`) | Sello debajo del bloque de identificación |

Lo pedido invierte la composición: **línea siempre presente**, estampa (imagen del baúl o sello de
identidad) **sobre** la línea, y los datos del firmante debajo.

## ⚠️ Trampa crítica

La HU #11031 fijó la **prioridad del baúl**: con firma de baúl vigente **no** se añade además el sello
de la validación de identidad, porque el documento quedaba como si la parte hubiera firmado de dos
maneras distintas. Este ajuste solo debe **mover la posición**, conservando esa exclusividad.

Segundo antecedente a vigilar: `DbSignatureVaultReader` abría transacción anidada y el best-effort se
tragaba el fallo, dejando la regeneración muerta en trámites de persona jurídica. Verificar con un
trámite de NIT real.

## Archivos previstos

- `services/core-api/src/Flit.Infrastructure/Documents/MandatoPdfGenerator.cs`
- `services/core-api/src/Flit.Infrastructure/Documents/SolicitudVirtualPdfGenerator.cs`
- Tests: `services/core-api/tests/Flit.Infrastructure.Tests/Documents/MandatoPdfGeneratorTests.cs`,
  `SolicitudVirtualPdfGeneratorTests.cs`
- Verificación visual: `services/core-api/artifacts/render-documentos/`
