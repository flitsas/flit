# ADR-0035 — Compraventa autogenerada SIEMPRE (coexistiendo con la del usuario) y sin encabezados personalizados

- **Estado**: Aceptado · 2026-07-23
- **Módulo**: Trámites — Traspaso (TR), generación documental
- **Feature**: #10852 (mejoras finales)
- **Supersede parcialmente**: ADR-0031 (condición de autogeneración) · **Mantiene**: ADR-0031 (firmas por sello de identidad), ADR-0028 (no bloqueante)
- **Nota**: el §2 (sin membrete) queda **supersedido** por [ADR-0053] (Propuesto). El §1 (autogenerar siempre y coexistir con la del usuario) **sigue vigente**.
- **Deciders**: Líder Técnico FLIT
- **Tags**: arquitectura, backend, documental, traspaso

## Contexto

ADR-0031 decidió generar la compraventa del sistema **solo cuando el usuario no hubiera cargado** una compraventa autenticada (`Source != "system"`), para respetar el documento del usuario y corregir un bug de sobrescritura.

Negocio revisó el comportamiento con el documento real y pide **dos cambios**:

1. **La compraventa del sistema debe generarse siempre**, incluso si el usuario cargó un documento en el paso de carga. Motivo: el documento del sistema es el que lleva la declaración con el formato exigido y los sellos de validación de identidad de ambas partes; el del usuario puede no cumplir ese formato. Ambos deben quedar en el expediente.
2. **El documento no debe llevar encabezados personalizados** (membrete FLIT): es una declaración legal que se presenta limpia, como en la muestra oficial (`recursos dllo membrete/PDF/compraventa-generado-sistema.pdf`).

El punto 2 también **corrige la nota de ADR-0031** que instruía "aplicar el membrete de ADR-0030" a este documento.

## Decisión

1. **Autogenerar siempre** la compraventa del sistema en traspaso, eliminando la condición `!TieneCompraventaDelUsuario`. La compraventa **subida por el usuario se conserva** como adjunto independiente (sigue protegida del borrado idempotente, que solo alcanza `Source="system"`): **ambas coexisten** en el expediente.
2. ~~La compraventa autogenerada se emite **sin membrete ni pie de marca** (excepción explícita a ADR-0030).~~ **Supersedido por [ADR-0053]**: el documento vuelve a llevar membrete FLIT (`FlitLetterhead`).
3. Se mantiene sin cambios de ADR-0031: firmas por **sello de validación de identidad** (`SellosIdentidad`/`FirmaImagenes`), render **sin firmas** si la identidad no está validada, y carácter **no bloqueante** (ADR-0028).

## Alternativas consideradas

### Opción 1: Autogenerar siempre y conservar la del usuario (ELEGIDA)
**Pros:** el expediente siempre tiene la declaración con el formato exigido y los sellos; no se pierde el documento del usuario; cambio mínimo (una condición).
**Cons:** el consolidado puede incluir **dos** documentos de compraventa (posible duplicidad visual para el revisor).
**Esfuerzo:** S · **Riesgos:** bajo.

### Opción 2: Autogenerar siempre y que la del sistema reemplace a la del usuario
**Pros:** un único documento, sin duplicidad.
**Cons:** se descarta evidencia aportada por el usuario (riesgo documental/legal); reintroduce el clobber que ADR-0031 corrigió.
**Esfuerzo:** S · **Riesgos:** alto.

### Opción 3: Mantener ADR-0031 (no autogenerar si el usuario cargó)
**Pros:** sin cambios; sin duplicidad.
**Cons:** incumple el requerimiento de negocio; expedientes sin la declaración con sellos de identidad.
**Esfuerzo:** nulo · **Riesgos:** incumplimiento funcional.

## Tradeoff aceptado

Opción 1: se acepta la posible **duplicidad** de documentos de compraventa en el consolidado a cambio de garantizar que siempre exista la declaración con el formato oficial y los sellos de identidad, sin descartar la evidencia del usuario. Si la duplicidad molesta en revisión, se resuelve después en el **orden/etiquetado** del compositor (ADR-0030), no descartando documentos.

## Consecuencias

### Lo que se gana
- Toda compraventa de traspaso incluye la declaración con formato oficial y sellos de identidad de ambas partes.
- El documento sale limpio, fiel a la muestra, sin marca que lo desvirtúe como declaración legal.

### Lo que se pierde
- Posible duplicidad de compraventas en el consolidado (una del usuario + una del sistema).
- Excepción a la uniformidad de marca documental de ADR-0030 (documentada aquí para que no se lea como deriva).

### Cambios operacionales
- Sin migración. Sin cambios en gates de ciclo de vida (sigue no bloqueante).
- El consolidado puede crecer una página en traspasos donde el usuario cargó su compraventa.

## ADRs relacionados
- [ADR-0031] — Compraventa autogenerada firmada con identidad (este ADR **supersede su condición de autogeneración** y **corrige** su nota de membrete; mantiene el resto).
- [ADR-0028] — Firma de compraventa no bloqueante (intacto).
- [ADR-0030] — Marca documental compartida (la exclusión de membrete de este ADR §2 la **supersede** [ADR-0053]).
- [ADR-0053] — Compraventa con membrete FLIT (Propuesto; supersede este ADR §2).

## Notas para agentes
- **Backend Agent**: en `FurCommand`, eliminar la condición `!TieneCompraventaDelUsuario` de la autogeneración; **no** tocar el filtro de borrado idempotente (debe seguir alcanzando solo `Source="system"`). El membrete lo define [ADR-0053] (`FlitLetterhead` en `FurCompraventaDocumentGenerator`).
- **QA Agent**: casos: usuario carga compraventa → **ambas** quedan en el expediente y la del usuario **no** se sobrescribe; sin carga → solo la del sistema; identidad pendiente → autogenerada sin firmas; verificar que el consolidado incluye ambas y en orden legible.
- **Security Agent**: sin cambios de permisos; el documento sigue conteniendo datos personales y sellos (Habeas Data).
- **Infra Agent**: sin migración.

## Referencias externas
- Muestra oficial: `recursos dllo membrete/PDF/compraventa-generado-sistema.pdf`.
- Template legado de referencia: `right-petition/partials/content.buyingselling.hbs`.
