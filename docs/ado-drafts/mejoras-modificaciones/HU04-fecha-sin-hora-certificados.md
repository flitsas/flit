# HU04 — [BACKEND] Fecha AÑO/MES/DÍA sin hora en los certificados del expediente consolidado

| Campo | Valor |
|-------|-------|
| Tipo | `[BACKEND]` |
| Story Points | 3 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11049** |
| Commit | `b37f5310` |
| Implementación | `FlitDocumentDate` con `Format` (fechas tipadas) y `Normalize` (texto del proveedor, día primero, valor intacto si no se puede interpretar). Aplicado a RNMC, identidad, SOAT/RTM y RUES. Sin hora, el sufijo `UTC` se retira. 15 tests. **Fuera de alcance:** exportadores de Excel y resumen ejecutivo del módulo de analítica, que no van en el consolidado |
| Ajuste origen | `modificaciones.txt:47` |

## Descripción

**Como** revisor del expediente consolidado
**Quiero** ver las fechas de los certificados sin hora, minuto ni segundo
**Para** leer el documento sin ruido y de forma consistente entre certificados

## Criterios de aceptación

```gherkin
Escenario: certificado con fecha de consulta
  Dado un certificado generado por el sistema que incluye fecha de consulta
  Cuando se genera el documento
  Entonces la fecha se imprime en formato AÑO/MES/DÍA sin hora, minuto ni segundo

Escenario: tablas certificadoras de SOAT, RTM y RUES
  Dado un expediente consolidado con certificados de SOAT, RTM y RUES
  Cuando se generan sus tablas de datos
  Entonces las fechas de expedición, vigencia y vencimiento se imprimen en formato AÑO/MES/DÍA

Escenario: pie de documento generado
  Dado cualquier certificado generado por el sistema
  Cuando se imprime el pie con la fecha de generación
  Entonces la fecha se muestra sin hora
```

## Estado actual del código

| Archivo | Línea | Formato hoy |
|---------|-------|-------------|
| `RnmcCertificatePdfGenerator.cs` | `:47` | `{data.ConsultadoEn:yyyy-MM-dd HH:mm} UTC` |
| `RnmcCertificatePdfGenerator.cs` | `:83` | `{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC` (pie) |
| `IdentityCertificatePdfGenerator.cs` | `:70` | `{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC` (pie) |
| `SoatRtmCertificatePdfGenerator.cs` | `:99-105` | Chips de fecha de expedición / vigencia / vencimiento pintados **tal como llegan del proveedor** |
| `RuesCertificatePdfGenerator.cs` | — | Revisar en implementación (mismo patrón que SOAT/RTM) |

## Notas técnicas

Dos casos distintos:

1. **Fechas propias del sistema** (`DateTimeOffset`/`DateTime` formateadas): cambiar el formato a
   `yyyy/MM/dd`. Decidir si se conserva el sufijo `UTC` — sin hora, `UTC` deja de aportar y conviene
   quitarlo.
2. **Fechas que llegan como texto del proveedor** (SOAT, RTM, RUES): pueden traer hora incluida en la
   cadena. Requiere normalizar: parsear con formatos tolerados y reformatear, dejando el valor original
   si no se puede parsear (nunca inventar ni vaciar el dato).

Conviene un helper compartido de formato de fecha documental en `Documents/` para no repetir la regla en
cinco generadores.

## Archivos previstos

- `services/core-api/src/Flit.Infrastructure/Documents/RnmcCertificatePdfGenerator.cs`
- `services/core-api/src/Flit.Infrastructure/Documents/IdentityCertificatePdfGenerator.cs`
- `services/core-api/src/Flit.Infrastructure/Documents/SoatRtmCertificatePdfGenerator.cs`
- `services/core-api/src/Flit.Infrastructure/Documents/RuesCertificatePdfGenerator.cs`
- Helper nuevo en `services/core-api/src/Flit.Infrastructure/Documents/`
- Tests: `services/core-api/tests/Flit.Infrastructure.Tests/Documents/`
