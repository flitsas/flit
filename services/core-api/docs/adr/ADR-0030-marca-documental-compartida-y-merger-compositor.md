# ADR-0030 — Módulo de marca documental compartido y merger del consolidado como compositor con contexto

- **Estado**: Aceptado · 2026-07-23
- **Módulo**: Trámites — Generación documental (`Flit.Infrastructure/Documents`)
- **Feature**: #10852 (puntos 3, 4, 5, 6 y base técnica)
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, documental, pdf

## Contexto

Los puntos 3 (portada), 4 (pie de página), 5 (marca de agua) y 6 (membrete de certificados) del Feature #10852 comparten la misma necesidad: identidad visual FLIT (colores `#557EFF`/`#162744`, membrete, tipografía **Poppins**, tamaño Carta, márgenes 2,54 cm) y estampado de overlays sobre el PDF consolidado. Hoy `PdfExpedienteConsolidadoMerger` es un concatenador de bytes (PdfSharpCore) **sin contexto** (no conoce estado del trámite ni a qué documento pertenece cada página), y los generadores QuestPDF (RUES/RNMC/Identidad) **duplican** página/margen/fuente sin membrete. No existe tema compartido ni Poppins embebida (grep "Poppins" → 0). Coexisten dos rutas de consolidado (`consolidado` wizard y `consolidado_maestro`, #10701).

## Decisión

Crear un módulo `Documents/Branding/` (tema + membrete + Poppins embebida + estampador de overlays + portada) del que dependen todos los generadores, y evolucionar `PdfExpedienteConsolidadoMerger` de concatenador a **compositor con contexto** (portada QuestPDF → merge PdfSharpCore → overlay pie + marca de agua), aplicado a ambas rutas de consolidado.

## Alternativas consideradas

### Opción 1: Módulo Branding compartido + merger compositor (RECOMENDADA)
**Pros:**
- Un solo lugar para colores/fuentes/membrete; reutilización > 70%.
- Generadores dependen del tema, no entre sí (bajo acoplamiento).
- El merger recibe contexto y compone; pie/marca de agua funcionan sobre cualquier página sin importar su origen (overlay PdfSharpCore).
- Alta cohesión: la identidad visual vive junta.

**Cons:**
- Cambia la firma de `IExpedienteConsolidadoMerger.Merge` (impacta 2 handlers + tests).
- Requiere embeber fuentes/assets (tamaño de binario).

**Esfuerzo:** M · **Riesgos:** contrato del merger (mitigado con tests); fidelidad de render SVG en QuestPDF (fallback PNG @72x).

### Opción 2: Estilos inline por generador
**Pros:** sin refactor de contrato; cambios locales.
**Cons:** duplica membrete/fuentes en 4+ generadores; deriva visual; el pie/marca de agua sobre el consolidado igual obliga a tocar el merger.
**Esfuerzo:** M · **Riesgos:** alto de mantenimiento (deriva).

### Opción 3: Post-proceso del PDF con herramienta externa
**Pros:** desacopla el estampado del código .NET.
**Cons:** dependencia/binario nuevo; latencia y complejidad operativa; contradice el stack QuestPDF/PdfSharpCore existente.
**Esfuerzo:** L · **Riesgos:** alto (dependencia nueva — viola regla ADR #3).

## Tradeoff aceptado

Se acepta pagar una vez el cambio de contrato del merger (Opción 1) a cambio de habilitar 4 puntos con un módulo cohesivo y sin deriva visual. La Opción 2 acumula deuda y la 3 introduce dependencias operativas injustificadas.

## Consecuencias

### Lo que se gana
- Identidad visual unificada y consistente en consolidado y certificados.
- Punto único de evolución de marca (colores/fuentes/membrete).
- El merger pasa a soportar portada, pie descriptivo y marca de agua de estado.

### Lo que se pierde
- Se incrementa levemente el tamaño del binario (fuentes + SVG embebidos).
- El consolidado puede contener páginas de tamaño mixto (FUR conserva su tamaño por D3).

### Cambios operacionales
- `Flit.Infrastructure.csproj` declara Poppins (OFL) y SVG como `EmbeddedResource`.
- Ambos handlers de consolidado construyen un `MergeRequest` con contexto (estado + etiquetas por documento).

## ADRs relacionados
- [ADR-0022] — Estados de negocio del ciclo de vida (fuente de la marca de agua de estado).
- [ADR-0029-preview-presigned-get-inline] — patrón de previsualización documental.

## Notas para agentes
- **Backend Agent**: implementar `Branding/` como HU-A (bloqueante). Registrar fuentes con `FontManager.RegisterFont` (distinto del `IFontResolver` PdfSharpCore del FUR). Overlay de pie/marca de agua con `XGraphics`. Marca de agua solo si `status ∉ {aprobado, entregado, preparado}` (reusar `TramiteEstado`, `StatusLabel`).
- **Frontend Agent**: NA.
- **QA Agent**: validar portada en todos los tipos de trámite; pie por documento (texto de `DocumentType.Description`); marca de agua por estado; tamaños de página (FUR distinto) sin recortes.
- **Security Agent**: sin cambios de permisos; documentos pueden contener datos personales (Habeas Data) — no alterar controles existentes.
- **Infra Agent**: sin migración; solo assets embebidos.

## Referencias externas
- Lineamientos de membrete: `recursos dllo membrete/LEER Importante/Importante.md`.
- QuestPDF 2024.12.3 · PdfSharpCore 1.3.65.
