# HU-A — [BACKEND] Escrituras: policy AdminCompany + OwnTenant

> Feature local: AdminCompany auth parity · **Sin ID ADO** · SP: 2

## Description

**Como** administrador de una compañía gestora (`AdminCompany`),  
**quiero** registrar, consultar, actualizar y dar de baja escrituras de **mi** compañía,  
**para** reutilizar esa documentación en los trámites sin depender de un SuperAdmin.

## Acceptance Criteria

### AC1 — AdminCompany opera escrituras de su tenant

```gherkin
Given un JWT con rol AdminCompany y tenant_id = T
When se llama GET/POST/PUT/DELETE /api/v1/admin/companies/T/deeds[...]
Then la operación se autoriza (no 403 por policy) y el handler de negocio responde como hoy
```

### AC2 — AdminCompany no opera otro tenant

```gherkin
Given un JWT con rol AdminCompany y tenant_id = T1
When se llama cualquier verbo sobre /api/v1/admin/companies/T2/deeds (T2 ≠ T1)
Then la respuesta es 403 con error FORBIDDEN_TENANT
```

### AC3 — SuperAdmin conserva acceso cross-tenant

```gherkin
Given un JWT con rol SuperAdmin
When opera /api/v1/admin/companies/{cualquierTenantId}/deeds
Then no se aplica el bloqueo de tenant propio
```

### AC4 — Sin cambios de dominio

```gherkin
Given la implementación de esta HU
When se revisa el diff
Then no hay cambios en handlers *Deed*, storage, DDL ni FE de formularios de escrituras
```

## Notas técnicas

- Archivo único de cambio funcional: `AdminDeedsEndpoints.cs`
- Patrón: igual que `AdminSignatureVaultEndpoints` / `AdminLegalRepresentativesEndpoints` (HU #11228)
- Commit: `HU-A: alinear policy de escrituras a AdminCompany + OwnTenant`
