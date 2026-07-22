# Importación masiva de trámites (Excel/CSV)

Crea trámites en estado **borrador** en lote desde un archivo **Excel (.xlsx)** o **CSV** (un Excel
exportado como CSV UTF-8 también encaja). El formato se detecta por la extensión/Content-Type. Cada
fila crea un borrador mínimo; el resto del trámite (actores, documentos, comercial, prenda,
biometría, firma) se completa después en el wizard de la aplicación.

> **Límite:** máximo **500 filas de datos** por archivo. Si se supera, la importación se rechaza con
> `400` sin crear ningún trámite.

## Endpoint

```
POST /api/v1/tramites/instances/bulk-import
Content-Type: multipart/form-data   (campo: file)
Authorization: Bearer <jwt>
X-Tenant-Id: <compañía>             (obligatorio solo para superadmin)
```

La **compañía** y el **usuario** NO van en el archivo: se resuelven del JWT. Un usuario de compañía
importa en su propia compañía; el superadmin debe indicar la compañía destino con `X-Tenant-Id`.
Un archivo = una compañía.

## Columnas del archivo

El encabezado va en la primera línea. Las columnas pueden ir en **cualquier orden** y el nombre es
**case-insensitive**. Separador coma; se admiten comillas dobles (`"…"`) con escape `""`.

| Columna | Requerido | Valores | Qué hace |
|---|---|---|---|
| `modalidad` | **Sí** (o `tipo_codigo`) | `matricula_inicial` \| `traspaso` | Modalidad de entrada del trámite |
| `tipo_codigo` | Alternativa a `modalidad` | ej. `MATRICULA_NUEVA`, `TRASPASO_STANDARD` | Código del tipo publicado (**precede** a `modalidad`) |
| `oficina_transito_codigo` | No | código de `catalogs.transit_offices.code` (ej. `05001`) | Asigna el organismo de tránsito |
| `vin` | No (recomendado en matrícula) | VIN de 17 caracteres | Se siembra como `field_value` `vin` |
| `placa` | No (recomendado en traspaso) | ej. `ABC123` | Se siembra como `field_value` `plate` |

Reglas:
- Cada fila **debe** traer `modalidad` **o** `tipo_codigo` (si vienen ambos, manda `tipo_codigo`).
- `vin`/`placa` son opcionales pero recomendados: sin ellos los borradores creados quedan sin dato
  identificable en la lista.
- Las filas con `modalidad=matricula_inicial` fallan si la compañía no tiene habilitada la matrícula
  inicial (`AllowInitialRegistration`). El error se reporta por fila, sin abortar el lote.

Ver [`plantilla-tramites.csv`](./plantilla-tramites.csv).

## Respuesta

`200 OK` con un reporte por fila (partial success es normal en lote):

```json
{
  "total": 3,
  "created": 2,
  "failed": 1,
  "results": [
    { "row": 1, "input": "traspaso", "status": "created", "referenceNumber": "TRM-2026-000045", "instanceId": "…", "error": null },
    { "row": 2, "input": "matricula_inicial", "status": "failed", "referenceNumber": null, "instanceId": null, "error": "matricula_inicial_no_habilitada" },
    { "row": 3, "input": "TRASPASO_STANDARD", "status": "created", "referenceNumber": "TRM-2026-000046", "instanceId": "…", "error": null }
  ]
}
```

Códigos de error por fila más comunes: `oficina_no_encontrada`, `matricula_inicial_no_habilitada`,
`invalid_request` (falta modalidad/tipo o vienen ambos sin código), `modalidad_not_available`,
`not_found`, `invalid_reference`. Si el borrador se creó pero la seed `vin`/`placa` falló, la fila
queda `created` con `error = "seed_warning:<código>"`.
