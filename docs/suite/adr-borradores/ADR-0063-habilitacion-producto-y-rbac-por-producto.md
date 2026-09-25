# ADR-0063: Habilitación de productos por empresa y RBAC por producto

**Fecha**: 2026-09-23  
**Status**: BORRADOR (pasa a Propuesto al versionarse en `docs/decisions/`)  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), PO, arquitectura  
**Tags**: seguridad, RBAC, habilitación de productos, productos, SuperAdmin, AdminCompany, HU-10664  
**Base de código**: `origin/develop@e8b7ca65`

## Contexto

En la suite (ADR-0061) una empresa contrata productos por separado, el SuperAdmin los activa y los roles son por producto. Hoy:

- No existe el concepto de producto. Hay tres booleans en `admin.tenant_operational_policies` (`TenantSettings.cs:135-149`) que **ningún endpoint aplica**: solo los lee `DashboardActiveModulesEndpoints.cs` para pintar tarjetas.
- `security.modules` y `security.permissions` son globales; `security.roles` es un catálogo global por `target_entity_type` (HU #10505); `security.user_role_assignments` liga usuario, rol y empresa. Un correo pertenece a una sola empresa (ADR-0060 D3).
- **HU #10664** (commit `8de54606`, Feature #10504) eliminó `security.tenant_module_grants`. Motivo registrado: la habilitación por empresa solo se aplicaba en el menú y la API permitía operar el módulo igual.

## Decisión

1. **No revertir HU #10664.** Se introduce **producto** (lo que se habilita a una empresa) por encima de **módulo** (permisos).
2. Schema `platform` migrado por `core-api`:
   - `platform.products (code, name, icon, status)`, con semilla `plataforma`, `tramites`, `comparendos`, `diagnostico`.
   - `platform.tenant_products (tenant_id, product_code, enabled, notes, updated_at, updated_by)`, auditado en `admin.tenant_config_audit_logs`. Un producto está **encendido o apagado** para una empresa: no es una suscripción comercial, no hay estados, fechas, vencimientos ni cobro (contrato de plataforma v1, §4).
3. `security.modules` y `security.roles` ganan `product_code`. Un rol solo incluye permisos de módulos de su producto. `SuperAdmin` y `AdminCompany` quedan en `plataforma`.
4. **Aplicación en tres puntos, todos obligatorios:**
   - Emisión del token (ADR-0062): con el producto apagado o sin rol en él, no hay token para ese `aud`. El SuperAdmin conserva el bypass en todos los productos (contrato §2.1).
   - Cada API: policy `RequireProduct` que valida `aud` y que el producto esté encendido, con caché en Redis invalidada por el evento `platform.tenant_product.changed`.
   - Launcher: `GET /api/v1/platform/me/apps` lista productos encendidos para la empresa y con rol del usuario.
5. **Manifiesto de producto**: cada servicio registra al arrancar, de forma idempotente, sus módulos, permisos y roles por defecto.
6. Jerarquía: las hijas de una Concesión o Marca Blanca solo pueden tener productos activos en su cabeza, con la misma regla fail-closed de ADR-0057 (jerarquía).
7. Migración: `tramites` encendido para todas las empresas existentes; `tramites_module_enabled` y `comparendos_module_enabled` pasan a filas de `platform.tenant_products` y se retiran tras un sprint. `resoluciones_module_enabled` no se toca: Resoluciones está fuera de v1.

## Alternativas consideradas

### Opción 1: Restaurar `tenant_module_grants` (revertir HU #10664)

**Pros:**
- La tabla y su migración de reversa existen.

**Cons:**
- Mezcla producto comercial con módulo de permisos.
- Repite el defecto que motivó la HU si la API no lo aplica.
- Sin auditoría y sin aplicarse en la API.

**Esfuerzo:** S  
**Riesgos:** Reabrir la incongruencia menú-versus-API.

### Opción 2: Extender los booleans de `tenant_operational_policies`

**Pros:**
- Ya existen y la consola SuperAdmin los edita.

**Cons:**
- Una columna por producto; Flotas exige migración y UI nuevas.
- Sin fechas, estados ni historial.
- Mezcla configuración operativa de trámites con decisiones comerciales.

**Esfuerzo:** S  
**Riesgos:** Crece sin forma; sigue sin aplicarse en la API.

### Opción 3: Schema `platform` con productos y habilitación por empresa; `product_code` en módulos y roles — elegida

**Pros:**
- Separa qué productos tiene una empresa de los permisos dentro de cada producto.
- Un producto nuevo es un dato más su manifiesto.
- Aplicación en token, API y menú: cierra la brecha que motivó la HU #10664.
- Apagar un producto no borra datos; volver a encenderlo los recupera.

**Cons:**
- Tablas y pantallas nuevas; cambio en el constructor de roles.
- Los productos dependen de la caché de habilitación.

**Esfuerzo:** M  
**Riesgos:** Caché desactualizada tras apagar un producto; se mitiga con evento de invalidación y token de vida corta.

## Tradeoff aceptado

Se elige la **Opción 3**: un modelo nuevo a cambio de no mezclar la habilitación de productos con los permisos y de aplicarla donde la HU #10664 mostró que faltaba.

## Consecuencias

### Lo que se gana

- SuperAdmin enciende y apaga productos por empresa, con historial.
- AdminCompany asigna roles por producto desde el hub.
- Tokens más pequeños: solo los permisos del producto que los pide.

### Lo que se pierde

- La vista de "todos los permisos del usuario" en un solo token.

### Cambios operacionales

- Migración que etiqueta módulos y roles actuales con `tramites` o `plataforma`.
- `GET /api/v1/security/modules` acepta filtro por producto.

## Actualización 2026-09-24 (contrato v1)

La primera versión de este borrador modelaba una **suscripción** con estados (`ACTIVE`, `SUSPENDED`, `CANCELLED`) y vigencia (`starts_at`, `ends_at`). Al cerrar el contrato de plataforma v1 se simplificó a **encendido o apagado** por empresa, porque ningún requisito de producto pide fechas, periodos de prueba ni cobro. Si aparece un vencimiento, se agrega una columna opcional `ends_at` sin romper nada.

## ADRs relacionados

- HU #10664 — no se revierte; se incorpora su lección.
- ADR-0023 (core-api) — catálogo global de roles; se amplía.
- ADR-0057 (core-api, jerarquía) — herencia fail-closed.
- ADR-0062 — emisión del token por producto.

## Notas para agentes

- **Backend Agent**: todo endpoint de producto lleva `RequireProduct` más su permiso; test de 403 con el producto apagado.
- **Frontend Agent**: ocultar en el menú no reemplaza el 403 de la API.
- **QA Agent**: matriz empresa con o sin producto × usuario con o sin rol × producto encendido o apagado × empresa hija × SuperAdmin.
- **Security Agent**: auditoría de cada encendido, apagado y asignación de rol.
