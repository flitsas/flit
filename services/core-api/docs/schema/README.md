# Schema Fase 1 — FLIT Evolution

DDL ejecutable por Historia de Usuario de migración. Orden de aplicación:

| Orden | Archivo | HU ADO | Feature |
|------|---------|--------|---------|
| 0 | `ddl/00-bootstrap.sql` | — | Schemas, uuidv7, audit, triggers |
| 1 | `ddl/01-HU10146-identity-security-auth.sql` | [#10146](https://dev.azure.com/FlitDevOps/_workitems/edit/10146) | #10113 Auth |
| 2 | `ddl/02-HU10148-rbac.sql` | [#10148](https://dev.azure.com/FlitDevOps/_workitems/edit/10148) | #10134 RBAC |
| 3 | `ddl/03-HU10147-invitations.sql` | [#10147](https://dev.azure.com/FlitDevOps/_workitems/edit/10147) | #10115 Invitaciones |
| 4a | `ddl/04-HU10151-tramites-parametrizacion.sql` | [#10151](https://dev.azure.com/FlitDevOps/_workitems/edit/10151) | #10116 Motor — DDL base |
| 4b | `ddl/04-HU10151-revision-parametrizacion.sql` | [#10151](https://dev.azure.com/FlitDevOps/_workitems/edit/10151) | #10116 Motor — Revisión incremental (consultation_templates G1; publication_status/published_at/by G2 en procedure_types; is_locked/consultation_template_id G3 en form_fields; row_version A16) |
| 4c | `ddl/04-HU10151-seeds-minimos.sql` | [#10151](https://dev.azure.com/FlitDevOps/_workitems/edit/10151) | #10116 Motor — Seeds mínimos (4 aristas, 6 fuentes, 4 plantillas, 3 familias) |
| 5 | `ddl/05-HU10149-business-rules.sql` | [#10149](https://dev.azure.com/FlitDevOps/_workitems/edit/10149) | #10120 Reglas |
| 6 | `ddl/06-HU10150-procedure-instances.sql` | [#10150](https://dev.azure.com/FlitDevOps/_workitems/edit/10150) | #10128 Runtime |
| 7 | `ddl/07-HU10154-admin-tenants.sql` | [#10154](https://dev.azure.com/FlitDevOps/_workitems/edit/10154) | #10118 Compañías |
| 8 | `ddl/08-HU10155-documental.sql` | [#10155](https://dev.azure.com/FlitDevOps/_workitems/edit/10155) | #10138 Documental |
| 9 | `ddl/09-HU10152-ot-admin.sql` | [#10152](https://dev.azure.com/FlitDevOps/_workitems/edit/10152) | #10133 OT |
| 10 | `ddl/10-HU10153-analytics.sql` | [#10153](https://dev.azure.com/FlitDevOps/_workitems/edit/10153) | #10139 Dashboard |
| 11 | `ddl/11-schema-conformance-patch.sql` | — | PK `pk_*`, RLS, triggers, PII |
| 12 | `ddl/12-HU10505-catalogo-global-roles.sql` | [#10505](https://dev.azure.com/FlitDevOps/_workitems/edit/10505) | #10504 Roles y Permisos — catálogo global de roles por tipo de entidad (COMPANY \| TRANSIT_OFFICE); elimina `tenant_id`/RLS de `security.roles`/`security.role_permissions` |

**ADR:** [ADR-0018](../adr/ADR-0018-modelo-datos-fase1-evolution.md) (Propuesto) · [ADR-0023](../adr/ADR-0023-catalogo-global-roles.md) (Propuesto, excepción A4/A20 para `security.roles`).

## EF Core

La migración `HU10146_IdentitySecurityAuth` en `Flit.Infrastructure/Migrations/` materializa el script 00 + 01.

Migraciones aplicadas en orden:

| Timestamp | Clase EF | DDL embebido |
|---|---|---|
| 20260617225223 | `HU10146_IdentitySecurityAuth` | 01 |
| 20260617225843 | `AlignSnakeCaseColumns` | — (inline SQL) |
| 20260617230000 | `HU10148_Rbac` | 02 |
| 20260617230100 | `HU10147_Invitations` | 03 |
| 20260617230200 | `HU10151_TramitesParametrizacion` | 04a base |
| **20260617230250** | **`HU10151_RevisionParametrizacion`** | **04b revisión + 04c seeds** |
| 20260617230300 | `HU10149_BusinessRules` | 05 |
| 20260617230400 | `HU10150_ProcedureInstances` | 06 |
| 20260617230500 | `HU10154_AdminTenants` | 07 |
| 20260617230600 | `HU10155_Documental` | 08 |
| 20260617230700 | `HU10152_OtAdmin` | 09 |
| 20260617230800 | `HU10153_Analytics` | 10 |
| 20260617310000 | `SchemaConformancePatch` | 11 |
| 20260617310100 | `FixAuditLogsPk` | — (inline SQL) |
| … | (migraciones intermedias no listadas aquí — ver carpeta `Migrations/` para el historial completo) | — |
| 20260706223157 | `HU10505_GlobalRoleCatalog` | 12 |

```bash
pnpm migrate:core-api   # aplica todas las migraciones pendientes
```

Validar cada migración con `@db-schema-validator` antes de merge.

**ADR relacionados:** [ADR-0018](../adr/ADR-0018-modelo-datos-fase1-evolution.md) · [ADR-0019](../adr/ADR-0019-motor-parametrizacion-global-superadmin.md) · [ADR-0023](../adr/ADR-0023-catalogo-global-roles.md)
