# HU09 — [FRONTEND] Ver los documentos del expediente desde el listado de trámites

| Campo | Valor |
|-------|-------|
| Tipo | `[FRONTEND]` |
| Story Points | 5 |
| Estado | **Implementada y verificada** (Active en ADO, pendiente de PR) |
| Feature padre | [FEATURE.md](FEATURE.md) |
| ADO ID | **#11054** |
| Commit | `2af73838` |
| Ajuste origen | `modificaciones.txt:15` |
| Bloquea a | HU12 (la columna de acciones la incluye) |

## Descripción

**Como** gestor que revisa su bandeja de trámites
**Quiero** consultar los documentos de un trámite en línea desde el listado
**Para** verificar el expediente sin abrir el wizard paso por paso

## Criterios de aceptación

```gherkin
Escenario: abrir los documentos de un trámite
  Dado un trámite con documentos en el expediente
  Cuando el gestor usa la acción de ver documentos en la fila del listado
  Entonces se abre el panel de documentos sin salir del listado
  Y puede previsualizar cada documento en línea

Escenario: documento no previsualizable
  Dado un documento cuyo formato no admite previsualización
  Cuando el gestor lo abre
  Entonces se ofrece la descarga

Escenario: trámite sin documentos
  Dado un trámite sin documentos en el expediente
  Cuando el gestor usa la acción de ver documentos
  Entonces el panel informa que aún no hay documentos
```

## Notas técnicas — todo el andamiaje ya existe

El propio enunciado del negocio lo dice: *"esta lógica y componentes ya existe en el módulo de OT"*.
Verificado:

| Pieza | Dónde |
|-------|-------|
| Modal de previsualización (PDF en iframe, imagen, fallback de descarga, 4 estados) | `frontend/components/shared/DocumentPreviewModal.tsx` |
| Patrón de panel de documentos por fila | `frontend/components/admin/transit-offices/OtDocumentosTab.tsx` y `ClientProceduresSection.tsx` |
| Acción "ver documentos" en la fila | `ClientProceduresTable.tsx:134-145` (`onVerDocumentos`) |
| **Endpoint de URL presignada, ya disponible en trámites** | `GET /api/v1/tramites/instances/{id}/attachments/{attachmentId}/preview-url` — `AttachmentEndpoints.cs:140` |
| Cliente ya implementado | `tramitesClient.fetchAttachmentPreviewUrl` — `tramites-client.ts:817` |

⇒ **La HU es puramente frontend**: no requiere endpoint nuevo. Consiste en montar el panel sobre el
listado de trámites reutilizando esas piezas.

## Archivos previstos

- `frontend/components/operacion/TramitesTable.tsx` (acción en la fila)
- `frontend/components/operacion/` (panel/modal de documentos del trámite, reutilizando
  `DocumentPreviewModal`)
- Tests: `frontend/__tests__/tramites-table.test.tsx`, `frontend/__tests__/document-preview-modal.test.tsx`
