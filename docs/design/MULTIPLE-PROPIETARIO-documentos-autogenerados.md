# Múltiple propietario — documentos autogenerados (HU #12048)

> Implementación 2026-09-02 · HU [12048](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/12048) · Feature padre [#10453](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10453)  
> Cierra la brecha de **FUR, compraventa, mandato y solicitud virtual** que el diseño de actores ([MULTIPLE-PROPIETARIO-diseno-tecnico.md](MULTIPLE-PROPIETARIO-diseno-tecnico.md) y [ADR-0053](../../services/core-api/docs/adr/ADR-0053-multiple-propietario-modelo-reparto.md)) dejó explícitamente fuera.

## Qué se pidió

Con 2, 3 o 4 propietarios por lado, los PDF oficiales deben listar **todos** los actores (no solo `ordinal=1`) y pintar **todas** las firmas. Con un solo actor por lado el documento **no cambia**. El wizard de actores no forma parte de este paquete.

Fuente: pedidos de producto 2026-09-02 (FUR overlay, luego compraventa, luego mandato y solicitud virtual) más el simulador SuperAdmin para previsualizar 2/3/4 propietarios.

## Alcance cerrado

| Documento | Motor | Un propietario | Varios (2–4) |
|-----------|--------|----------------|--------------|
| FUR (overlay) | PdfSharpCore + `fur-field-manifest*.json` | Igual que hoy | Nombres, apellidos y documento **apilados en vertical**; fuente según N; tipos de documento (X) apilados en el recuadro; firmas **en fila horizontal** con estampa digital |
| Compraventa | QuestPDF | Casillas NIT/C.C./C.E./T.I/P.A. históricas | Lista `NOMBRE TIPO NÚMERO` separada por comas; firmas compactas en una fila; **una página** |
| Mandato | QuestPDF | Mandante + mandatario lado a lado | Otorgantes concatenados (coma + tipo + documento) en comparecencia/`{{mandante_*}}`; firmas de todos los mandantes en fila compacta; mandatario único; **una página** |
| Solicitud de trámite virtual | QuestPDF | Un otorgante | Misma concatenación y fila de firmas; **una página** |
| Preview SuperAdmin (FUR) | `FurPreviewSample` + `FurSimulatorPanel` | 1/1 | Selectores de cantidad de compradores/vendedores (2–4) |

Otorgantes del mandato y de la solicitud virtual: **vendedores en traspaso**, **compradores en matrícula** (misma regla que `FurDocumentData.Otorgante`).

## Fuera de alcance (sigue `ordinal=1`)

Consolidado del expediente, impronta, certificado RUES, escrituras. No se tocó el wizard ni `FirmaCommand`.

## Dónde vive el código

| Pieza | Ruta |
|-------|------|
| Narrativa y claves de firma por ordinal | `FurCompraventaCopropiedad`, `FurOverlayPartyKey` |
| Overlay FUR | `FurFieldMapper`, `FurOverlayRenderer`, `FurSignatureLayout`, `FurCheckboxLayout`, manifiestos |
| Observaciones FUR (porcentajes) | `FurCopropiedadObservation` — ver también [REGLAS-NUMERAL-3-TRES-CAPAS.md](../ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md) |
| QuestPDF | `FurCompraventaDocumentGenerator`, `MandatoPdfGenerator`, `SolicitudVirtualPdfGenerator`, `FlitFirmaBlock` (`compact`) |
| Preview | `FurPreviewSample`, `PreviewFurCommand`, `FurSimulatorPanel` |

## Reglas de producto (no normativas RNA)

El numeral 3 del FUR (casillas de trámite) **no cambia** por copropiedad. Lo que cambia es el **llenado de titulares y firmas** y el bloque de observaciones de copropiedad ya dictaminado.

El **objeto** `{{tramite}}` del mandato **no** concatena copropietarios: eso va en comparecencia y firmas. El objeto sigue [REGLAS-OBJETO-TRES-CAPAS.md](../ot/mandato/REGLAS-OBJETO-TRES-CAPAS.md).

## Verificación

Tests de infraestructura y aplicación (layout de una página, lista con coma, mapper vertical, checkboxes, preview con estampa). Validación visual del simulador SuperAdmin y de un trámite real con actores guardados queda para QA (HU en New; registro diferido).
