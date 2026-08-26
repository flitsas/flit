# Feature (borrador local) — Autorización AdminCompany: paridad escrituras y reset de contraseña

> **Estado ADO:** no creado (decisión de sesión 2026-08-04). Documentación local para trazabilidad.  
> **Rama:** `feature/AB-admincompany-deeds-reset-password`  
> **Alcance:** solo permisos / policies / UI. Sin cambios de modelo de datos ni servicios de negocio.

## OBJETIVO

Que el administrador de una compañía gestora (`AdminCompany`) pueda:

1. Administrar **escrituras** de **su** tenant (misma capacidad que ya tiene sobre RL y baúl de firmas).
2. **Restablecer contraseñas** de usuarios de **su** tenant (capacidad ya diseñada en HU #10170, operativizada).

Sin abrir suspender/eliminar usuarios ni el CRUD del catálogo global de roles (ADR-0023 / HU #10505).

## CRITERIOS FUNCIONALES

1. `AdminCompany` ejecuta CRUD de `/api/v1/admin/companies/{tenantId}/deeds` solo si `tenantId` coincide con el claim `tenant_id`; en caso contrario `403 FORBIDDEN_TENANT`.
2. `SuperAdmin` conserva acceso cross-tenant a escrituras.
3. `AdminCompany` puede `POST /api/v1/auth/admin/reset-password` sobre usuarios de su mismo tenant; otro tenant → `403 FORBIDDEN_SCOPE`.
4. La UI del módulo Usuarios expone “Restablecer contraseña” a SuperAdmin y AdminCompany.
5. Fuera de alcance: suspender/eliminar/restaurar usuarios; crear/editar/borrar definición de roles; migraciones DDL de tablas de dominio.

## DESCOMPOSICIÓN

| HU local | Título | SP |
|---|---|---|
| HU-A | [BACKEND] Escrituras: policy AdminCompany + OwnTenant | 2 |
| HU-B | [BACKEND]+[FRONTEND] Reset admin operable para AdminCompany | 3 |

## FUERA DE ALCANCE (backlog consciente)

- Reabrir `POST/DELETE /security/users/{id}/suspend` a AdminCompany.
- Gobernanza de roles por tenant (contrario a catálogo global).

## Trazabilidad de sesión

| Fase | Resultado |
|---|---|
| 1 Feature | Este documento |
| 2 Diseño | `DISENO-TECNICO.md` |
| 3 HUs | `HU-A-escrituras-policy.md`, `HU-B-reset-password.md` |
| 4 Implementación | Commits `HU…` en la rama del feature |
| ADO | Pendiente de creación cuando el PO lo autorice |
