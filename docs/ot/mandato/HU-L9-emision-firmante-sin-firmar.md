# HU-L9 — Emisión en radicación: datos del firmante y «Sin firmar»

> Generado: 2026-08-27  
> Registro local (sin work item ADO). Rama: `review/rl-ficha-aislada-escritura-unica`.

## Objetivo

El contrato de mandato del **trámite** se arma en `FurCommand` (generar FUR / radicación), no con `MandatoPreviewSample`.

Ya no aplica: *si no hay firmante elegido, el PDF lleva placeholders y la aprobación lo regenera para rellenar nombre/cédula*.

## Comportamiento

- Con firmante resuelto (elección o default HU-L8): **nombre y documento siempre**, aunque no haya baúl ni identidad vigente.
- Recuadro de firma sin estampa: texto **«Sin firmar»**. Al firmar y regenerar el FUR en borrador, entra la estampa (baúl o sello).
- Aprobar **sigue** exigiendo una vía de firma (baúl, identidad o firma a mano). No es el momento en que “aparecen” los datos de persona.
- Modos `open` / `institutional`: sin firmante persona (nacimiento del OT).
