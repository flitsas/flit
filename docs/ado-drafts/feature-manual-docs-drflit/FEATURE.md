# FEATURE (borrador local — NO creado en Azure DevOps)

Título: [DOCS] - Centro de ayuda FLIT (manual Gestor / OT) + puente DR-FLIT

# OBJETIVO

Publicar un centro de documentación operativo, con experiencia equivalente a
[FLIT People Manual](https://people.flitsas.com/manual) (sidebar por módulos,
búsqueda, badge “Aplica para”, TOC y prev/next), cubriendo solo los módulos
del Gestor cliente (no AdminCompany) y del Gestor OT (`ot_admin`). Integrar
en DR-FLIT la opción “Necesito ayuda” que enlaza al artículo documental
correspondiente cuando exista coincidencia.

# DESCRIPTION

- Portal público en ruta `/manual` (misma app Next.js; dominio docs.* pendiente de DNS).
- Secciones: Introducción, Gestor, Organismo de Tránsito.
- Fuera de alcance: SuperAdmin, consola AdminCompany, RBAC, Auditoría, ICT, Log QX.
- DR-FLIT: nuevo intent “Necesito ayuda” → pregunta libre → match por keywords
  del índice documental → chips que abren `/manual/...`.
- Matching v1 sin LLM (índice estático).

# CRITERIOS FUNCIONALES

- [ ] El portal `/manual` es público (sin login) y navega por sidebar + búsqueda.
- [ ] Cada artículo muestra etiqueta “Aplica para: …”.
- [ ] Contenido v1 cubre Dashboard/Trámites/Ayuda (Gestor) y hub OT
      (Trámites, Preasignación, Reportes, Usuarios, Reglas, Documentos, Requisitos).
- [ ] DR-FLIT muestra “Necesito ayuda”; si hay match, ofrece abrir el artículo.
- [ ] Si no hay match, informa que aún no hay documentación y sugiere reformular.
- [ ] El módulo SPA Ayuda enlaza al centro documental.

## Notas de trazabilidad

- Autorizado por el usuario a continuar **sin crear work items en ADO**.
- Referencia UX: https://people.flitsas.com/manual
