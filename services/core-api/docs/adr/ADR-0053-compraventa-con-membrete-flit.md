# ADR-0053 — Compraventa autogenerada con membrete FLIT (marca documental)

- **Estado**: Propuesto · 2026-09-01
- **Módulo**: Trámites — Traspaso (TR), generación documental
- **Feature/HU**: sin work item en ADO (cambio visual autorizado en sesión; trazabilidad Feature #10852)
- **Supersede parcialmente**: ADR-0035 §2 (excepción de membrete/pie). **Mantiene**: ADR-0035 §1 (autogenerar siempre y coexistir con la del usuario), ADR-0031 (firmas), ADR-0028 (no bloqueante)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, documental, traspaso

## Contexto

ADR-0035 (Aceptado) dejó la compraventa autogenerada **sin** membrete ni pie de marca, como excepción a [ADR-0030], para presentarla como declaración legal “limpia”.

Negocio pide ahora el mismo membrete institucional que ya usan Mandato y Solicitud de trámite virtual (`FlitLetterhead`: bandas SVG Carta + Poppins), más una tabla de descripción del vehículo legible y el reorden de la cita legal detrás de Fecha/Ref.

El pie con el nombre del documento sigue siendo responsabilidad de `FlitPdfStamper.ApplyDocumentName` al componer el consolidado (ADR-0030 / ADR-0049, perfil `Default`), no de QuestPDF.

> Numeración: el id 0052 quedó ocupado en `develop` por la resolución de mandato; este ADR es el siguiente libre.

## Decisión

La compraventa autogenerada del sistema **vuelve al alcance de la marca documental compartida** (ADR-0030): se emite con `FlitLetterhead.ApplyTo` y contenido en el margen FLIT. La excepción de ADR-0035 §2 queda **supersedida**. La autogeneración siempre y la coexistencia con el adjunto del usuario (ADR-0035 §1) no cambian.

## Alternativas consideradas

### Opción 1: Membrete compartido `FlitLetterhead` (RECOMENDADA)

**Pros:** misma identidad que Mandato y Trámite virtual; un solo componente de marca; pie del consolidado ya calibrado (ADR-0049 perfil Default).
**Cons:** reduce el área útil (2,54 cm arriba/abajo); el caso con dos personas jurídicas y sellos largos puede pasar a una segunda hoja.
**Esfuerzo:** S · **Riesgos:** bajo.

### Opción 2: Mantener ADR-0035 §2 (documento limpio, sin membrete)

**Pros:** más área para firmas; no contradice un ADR Aceptado hasta que el humano lo acepte.
**Cons:** incumple el requerimiento visual actual; la compraventa queda distinta al resto de documentos generados.
**Esfuerzo:** nulo · **Riesgos:** deriva de marca.

### Opción 3: Membrete solo en el consolidado (PDF suelto sin bandas)

**Pros:** el adjunto “crudo” sigue viéndose como declaración limpia.
**Cons:** dos apariencias del mismo tipo; el visor de adjuntos no coincidiría con Mandato/Solicitud; el stamper no pinta las bandas SVG, solo el nombre.
**Esfuerzo:** M · **Riesgos:** medio (doble pipeline de apariencia).

## Tradeoff aceptado

Opción 1: se acepta el menor espacio de contenido a cambio de uniformidad de marca. El cuerpo legal conserva su aire; el hueco libre de la hoja empuja las firmas al pie (`Extend` + `AlignBottom`), **sin** recortar el texto legal.

## Consecuencias

### Lo que se gana
- Compraventa del sistema alineada a Mandato y Trámite virtual.
- Descripción del vehículo en grilla de chips (`FlitRoundedCells`), mismo patrón SOAT/RTM.

### Lo que se pierde
- La excepción documentada en ADR-0035 §2 (documento “limpio”).
- Holgura vertical en el caso PJ con sellos de varias líneas (posible segunda hoja).

### Cambios operacionales
- Sin migración. Sin cambio de gates (sigue no bloqueante).
- `Aceptado` de este ADR es exclusivo del Líder Técnico humano.

## ADRs relacionados

- [ADR-0035] — Autogenerar siempre + coexistencia (intacto §1; §2 supersedido por este ADR).
- [ADR-0030] — Marca documental compartida (la compraventa vuelve a su alcance).
- [ADR-0031] — Firmas por sello de identidad (intacto).
- [ADR-0028] — Firma no bloqueante (intacto).
- [ADR-0049] — Geometría del pie por tipo (perfil Default de la compraventa, intacto).

## Notas para agentes

- **Backend Agent**: en `FurCompraventaDocumentGenerator`, aplicar `FlitLetterhead.ApplyTo` + `Content(..., verticalCm: 0)`; título centrado; Fecha y Ref. antes de la cita; tabla 2×3 con `FlitRoundedCells`; firmas al pie con `Extend`. No tocar `FurCommand` ni el filtro `Source="system"`.
- **Frontend Agent**: sin cambios de UI.
- **QA Agent**: PDF de traspaso con membrete; cita después de Fecha/Ref.; campos Marca/Chasis/Motor/Modelo/VIN/Referencia; firmas sin línea y ancladas al pie. Coexistencia usuario+sistema sigue siendo ADR-0035 §1.
- **Security Agent**: mismos PII y sellos; sin cambio de permisos.
- **Infra Agent**: sin migración.

## Referencias externas

- Guía visual de negocio (mock de sesión): membrete FLIT + tabla de vehículo + orden Fecha/Ref. → cita.
- Componentes: `FlitLetterhead`, `FlitRoundedCells`, `SoatRtmCertificatePdfGenerator` (patrón chip).
