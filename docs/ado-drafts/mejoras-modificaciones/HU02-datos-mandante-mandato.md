# HU02 — [BACKEND] Bloque de identificación completo del mandante en el contrato de mandato

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 3 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11047** |
| Commit | `c436b4bf` |
| Implementación | `MandanteIdentificacion` pasa a EMPRESA/NIT/NOMBRE/documento/CELULAR/CORREO (persona natural sin EMPRESA ni NIT). 6 tests sobre orden, campos y dato ausente. **El mandatario no lleva contacto**: `MandatarioFirmante` no lo transporta y el contacto que el organismo necesita es el del mandante |
| Ajuste origen | `modificaciones.txt:22-30` |

## Descripción

**Como** organismo de tránsito receptor del mandato
**Quiero** ver los datos completos del mandante bajo su firma
**Para** poder contactarlo y verificar su identidad sin consultar otro documento

## Criterios de aceptación

```gherkin
Escenario: mandante persona jurídica
  Dado un mandato cuyo mandante es una persona jurídica
  Cuando se genera el documento
  Entonces bajo la firma se imprimen empresa, NIT, nombre, tipo y número de documento, celular y correo electrónico en ese orden

Escenario: mandante persona natural
  Dado un mandato cuyo mandante es una persona natural
  Cuando se genera el documento
  Entonces bajo la firma se imprimen nombre, tipo y número de documento, celular y correo electrónico
  Y no se imprimen empresa ni NIT

Escenario: dato de contacto ausente
  Dado un mandante sin celular registrado
  Cuando se genera el documento
  Entonces la línea de celular se imprime con el marcador de dato ausente y el resto del bloque no se altera
```

## Formato pedido por el negocio

```
EMPRESA: BANCOLOMBIA S.A.S
NIT: 890903938
NOMBRE: Juan Felipe Montoya
CÉDULA DE CIUDADANÍA: 1038409485
CELULAR: 3112789718
CORREO ELECTRÓNICO: daniel.amado@flitsas.com
```

## Estado actual del código

`MandatoPdfGenerator.MandanteIdentificacion` (`:404`) imprime hoy, para persona jurídica:

```
NOMBRE / <doc> / EMPRESA / NIT
```

Faltan **CELULAR** y **CORREO ELECTRÓNICO**, y el orden no coincide con el pedido (empresa y NIT deben
ir primero).

`SolicitudVirtualPdfGenerator.FirmaBlock` (`:236`) **ya** produce el orden y los campos correctos
—incluyendo celular y correo desde `DocumentParte.Phone` / `.Email`— así que sirve de referencia
directa y confirma que los datos están disponibles en el contexto del documento.

## Archivos previstos

- `services/core-api/src/Flit.Infrastructure/Documents/MandatoPdfGenerator.cs`
- Tests: `services/core-api/tests/Flit.Infrastructure.Tests/Documents/MandatoPdfGeneratorTests.cs`
