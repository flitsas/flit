# [ADMIN OT] - Configuración de mandatos por organismo y empresa que radica

**Registro:** local (MCP Azure DevOps no disponible en la sesión). HUs hijas **no** se crearon en ADO a pedido.

# OBJETIVO

Unificar cómo se configura y resuelve el Contrato Privado de Mandato: el OT nace en formato abierto con plantilla genérica y mandatario vacío; la empresa que radica elige un solo modelo para todas las familias de trámite; Plataforma → Mandatos convive con el hub del OT; se conservan mandato institucional, firma por identidad y firma de baúl.

# DESCRIPTION

Tres modos (`signer`, `institutional`, `open`). Precedencia al generar: PDF personalizado de compañía (ADR-0042) → regla compañía×OT → `assignment_mode` del OT → legado sin fila = `signer`. Mandato cliente = regla `signer` de la empresa que radica + plantilla `generico`. Al crear el tenant OT se siembra `admin.transit_office_mandate_config`.

# CRITERIOS FUNCIONALES

- [ ] Al activar un OT nace con plantilla genérica, modo abierto y mandatario vacío.
- [ ] Sin regla de compañía se honra el `assignment_mode` del OT (ya no se ignora).
- [ ] El mandato abierto deja el bloque de firma con líneas `___`.
- [ ] Un solo modelo por empresa que radica, para todas las familias.
- [ ] Mandato cliente usa plantilla genérica.
- [ ] Institucional sigue vivo.
- [ ] Firma por identidad y baúl conviven.
- [ ] Plataforma → Mandatos no se depreca; misma persistencia que el hub OT.
