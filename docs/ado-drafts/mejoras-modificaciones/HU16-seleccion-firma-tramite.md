# HU16 — [FULLSTACK] Selección de la firma que se registra en el trámite cuando el representante tiene varias activas

| Campo | Valor |
|-------|-------|
| Tipo | `[FULLSTACK]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11061** |
| Commit | `5354d155` |
| Ajuste origen | `modificaciones.txt:13` |
| Depende de | HU01 (define cómo se plasma cada mecanismo) |
| Posible schema | Sí — ver abajo |

## Descripción

**Como** gestor que registra un trámite de una persona jurídica
**Quiero** elegir con qué firma del representante legal se registra el trámite
**Para** que el documento se firme con el mecanismo que corresponde al negocio

## Criterios de aceptación

```gherkin
Escenario: representante con dos firmas activas
  Dado un comprador o vendedor con NIT cuyo representante legal tiene firma del baúl y validación de identidad vigentes
  Cuando el gestor registra el trámite
  Entonces puede elegir con cuál de las dos se registra el trámite

Escenario: una sola firma activa
  Dado un representante con un único mecanismo de firma vigente
  Cuando el gestor registra el trámite
  Entonces se registra ese mecanismo sin pedir elección

Escenario: el documento respeta la elección
  Dado un trámite registrado con un mecanismo de firma elegido
  Cuando se generan los documentos del trámite
  Entonces se plasma la firma del mecanismo elegido

Escenario: sin firma vigente
  Dado un representante sin firma del baúl ni identidad vigentes
  Cuando el gestor registra el trámite
  Entonces el flujo continúa sin firma registrada y se informa la situación
```

## Estado actual del código

Ya resuelto (no rehacer):

- El lookup por NIT devuelve **por cada representante** las banderas `FirmaVigente` e
  `IdentidadVigente` (`FindRepresentativeByNitResponse.cs:26-35`), calculadas al momento.
- El frontend ya permite **elegir representante** cuando la compañía tiene varios (HU #10937,
  `ActorsForm.tsx:638-656`), y el elegido queda embebido en el actor.

Lo que falta: elegir el **mecanismo de firma** (baúl vs. validación de identidad) y **persistirlo**, de
modo que los generadores lo respeten en vez de aplicar una precedencia fija.

## ⚠️ Colisión con la precedencia vigente

La HU #11031 estableció **prioridad del baúl**: si hay firma de baúl vigente, esa es la firma y no se
añade el sello de identidad. Esa regla es hoy **implícita y global**. Al introducir elección explícita:

- La precedencia pasa a ser el **valor por defecto** cuando el gestor no elige.
- Los generadores (`MandatoPdfGenerator`, `SolicitudVirtualPdfGenerator`, compraventa, FUR) deben leer
  el mecanismo elegido del contexto del documento, no deducirlo.

## Schema — NO hizo falta

El plan estimaba una columna nueva (`signature_mechanism`) con migración y `db-schema-validator`.
**No fue necesario:** `procedure_instance_actors.metadata` (jsonb) ya persiste el representante legal
elegido (HU #10937) a través de `ActorMetadata.RepresentanteLegal`. El mecanismo es un dato **de ese
representante**, así que se añadió al mismo record: viaja por el camino que ya lo lleva hasta los
generadores, está tipado en la capa de aplicación (`MecanismoFirma` normaliza y descarta valores
desconocidos) y no exige DDL. Tampoco se recurrió a un `field_value` suelto.

## Dónde va el guard (una sola costura)

`FurCommand.ResolveVaultSignaturesAsync` es el **único** punto donde se resuelve la imagen de la firma
del baúl. FUR, mandato, solicitud de trámite virtual y compraventa consumen todos `FirmaImagenes` de
ese mismo ensamblado (`MandatoPdfGenerator.FirmaBaulDe` y equivalentes), así que un guard ahí honra la
elección en los cuatro documentos sin tocar ningún generador.

## ⚠️ Antecedente a vigilar

`DbSignatureVaultReader` abría transacción anidada y el best-effort se tragaba el fallo, dejando la
regeneración muerta en trámites de persona jurídica. Cualquier cambio en la resolución de firmas debe
verificarse con un trámite de NIT real, no solo con tests.

## Archivos previstos

- `frontend/components/operacion/ActorsForm.tsx`
- `services/core-api/src/Flit.Tramites.Application/UseCases/ProcedureInstances/ActorsCommand.cs`
- `services/core-api/src/Flit.Tramites.Application/Documents/IFurDocumentGenerator.cs` (contexto)
- `services/core-api/src/Flit.Infrastructure/Persistence/Repositories/DbSignatureVaultReader.cs`
- Migración en `services/core-api/src/Flit.Infrastructure/Migrations/` (si aplica)
