# Planificación: [TRAMITES] - Visualización y Expediente Documental

**Usuario:** Abraham Cañón Vasquez  
**Fecha:** 2026-07-14  
**Feature ADO:** [#10701](https://dev.azure.com/FlitDevOps/FLIT%20-%20EVOLUTION/_workitems/edit/10701)

## Detalle de planificación

### Historias de Usuario (HUs)

Máximo 5 HUs (BE/FE separados):

1. **[BACKEND]** Presigned preview-url inline (`Content-Disposition: inline`)
2. **[FRONTEND]** `DocumentPreviewModal` en wizard y detalle del trámite
3. **[BACKEND]** Lista documentos OT (preview + download) con grant
4. **[FRONTEND]** Vista OT documentos separados + consolidado
5. **[BACKEND]** Consolidado 100% desde tabla maestra + matriz resuelta

Dependencias: HU2 ← HU1; HU4 ← HU3; HU5 paralelo a FE.

### Diseños (UI/UX)

- Modal de preview: iframe (PDF) / img (imágenes); fallback descarga si MIME no previsualizable.
- Wizard y detalle: botón “ojo” junto a descarga existente.
- Vista OT: tabla de documentos (nombre, tipo, tamaño, fecha) + acciones preview/descarga + botón consolidado.
- Dark mode y responsive obligatorios.

### Desarrollo Frontend

- Componente `DocumentPreviewModal` en `frontend/components/ui`.
- Cableado en paso documentos del wizard y detalle.
- API client para preview-url y lista OT documents.
- Reutilizar consolidado existente en `admin-ot.ts`.

### Desarrollo Backend

- `GET .../attachments/{id}/preview-url` — presign inline vía `FileManagerAttachmentStorage`.
- `GET .../client-procedures/.../documents` — lista + URLs; `ITransitOfficeGrantGate`; TTL ≤ 15 min.
- Orquestación consolidado: attachments + generados, orden `ResolvedDocumentMatrixResolver`; anexos al final. Sin schema nuevo (Fase 2b NA).

---

*Documento generado con la skill planification-wiki bajo supervisión de Abraham Cañón Vasquez (fallback local; Azure Wiki no configurado en sesión).*
