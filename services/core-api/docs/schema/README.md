# Schema Fase 1 — FLIT Evolution

DDL ejecutable por Historia de Usuario de migración. Orden de aplicación:

| Orden | Archivo | HU ADO | Feature |
|------|---------|--------|---------|
| 0 | `ddl/00-bootstrap.sql` | — | Schemas, uuidv7, audit, triggers |
| 1 | `ddl/01-HU10146-identity-security-auth.sql` | [#10146](https://dev.azure.com/FlitDevOps/_workitems/edit/10146) | #10113 Auth |
| 2 | `ddl/02-HU10148-rbac.sql` | [#10148](https://dev.azure.com/FlitDevOps/_workitems/edit/10148) | #10134 RBAC |
| 3 | `ddl/03-HU10147-invitations.sql` | [#10147](https://dev.azure.com/FlitDevOps/_workitems/edit/10147) | #10115 Invitaciones |
| 4 | `ddl/04-HU10151-tramites-parametrizacion.sql` | [#10151](https://dev.azure.com/FlitDevOps/_workitems/edit/10151) | #10116 Motor |
| 5 | `ddl/05-HU10149-business-rules.sql` | [#10149](https://dev.azure.com/FlitDevOps/_workitems/edit/10149) | #10120 Reglas |
| 6 | `ddl/06-HU10150-procedure-instances.sql` | [#10150](https://dev.azure.com/FlitDevOps/_workitems/edit/10150) | #10128 Runtime |
| 7 | `ddl/07-HU10154-admin-tenants.sql` | [#10154](https://dev.azure.com/FlitDevOps/_workitems/edit/10154) | #10118 Compañías |
| 8 | `ddl/08-HU10155-documental.sql` | [#10155](https://dev.azure.com/FlitDevOps/_workitems/edit/10155) | #10138 Documental |
| 9 | `ddl/09-HU10152-ot-admin.sql` | [#10152](https://dev.azure.com/FlitDevOps/_workitems/edit/10152) | #10133 OT |
| 10 | `ddl/10-HU10153-analytics.sql` | [#10153](https://dev.azure.com/FlitDevOps/_workitems/edit/10153) | #10139 Dashboard |
| 11 | `ddl/11-schema-conformance-patch.sql` | — | PK `pk_*`, RLS, triggers, PII |

**ADR:** [ADR-0018](../adr/ADR-0018-modelo-datos-fase1-evolution.md) (Propuesto).

## EF Core

La migración `HU10146_IdentitySecurityAuth` en `Flit.Infrastructure/Migrations/` materializa el script 00 + 01.

```bash
pnpm migrate:core-api:add HU10148_Rbac   # siguiente HU
pnpm migrate:core-api
```

Validar cada migración con `@db-schema-validator` antes de merge.
