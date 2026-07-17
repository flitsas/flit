# FEATURE 02 — Configuración de Compañía 2.0

| Campo | Valor |
|---|---|
| **Fase / Entrega** | **Fase 1** — miércoles 15 de julio de 2026, 17:00 |
| **Desarrollador asignado** | **Willyn Londoño** |
| **Módulos afectados** | Config compañía, Compañías, Trámites (registro manual) |
| **Requerimientos cubiertos** | Objetivos2.0 filas 12, 13 y 25 |
| **Rama sugerida** | `feature/F02-configuracion-compania` |

## 1. Objetivo

Ampliar la configuración de compañía (tenant) con los parámetros nuevos que exige FLIT 2.0 y corregir el comportamiento de guardado, dejando la base de configuración lista para la Fase 2 (comparendos y restricciones por OT — FEATURE 05, mismo desarrollador).

## 2. Requerimientos de origen

1. **Parámetro nuevo — fuente de comparendos** (Objetivos2.0 — "Config compañía"): definir si la consulta de comparendos de la empresa se hace de forma **interna** (módulo de comparendos con fuente base cargada en la plataforma) o **externa** (consulta en línea al SIMIT). Coincide con la "Regla especial del SIMIT" de Caracteristicas.docx §3.
2. **Check "solo vehículos propios"** (Objetivos2.0 — "Compañias"): integrar el check de solo vehículos propios en el registro manual de trámite.
3. **AJUSTAR EL GUARDAR TODO de las compañías** (Objetivos2.0 — FLIT 2.0): el guardado de la configuración de compañía debe persistir todas las secciones de forma consistente.

## 3. Alcance

### Incluido
- **Nuevo parámetro** `fines_query_source` en `tenant_operational_policies` (valores: `internal` | `external`), con su UI en la pantalla de configuración de compañía y auditoría del cambio (`tenant_config_audit_logs`).
- **Check "solo vehículos propios"**: la política `only_own_vehicles` ya existe en `tenant_operational_policies`; esta feature la **hace efectiva en el registro manual de trámites**: si está activa, al crear un trámite se valida que el vehículo pertenezca a la compañía (o se restringe la selección), con mensaje claro al usuario.
- **Corrección del "guardar todo"**: revisar la pantalla de configuración de compañía (`components/admin`) y garantizar que el botón de guardado persista todas las secciones modificadas en una sola operación consistente (una llamada por sección o transacción agregada), con manejo de `row_version` (concurrencia optimista) y feedback de éxito/error por sección.

### Excluido
- El **uso** del parámetro de comparendos dentro del flujo del trámite (advertencias, consulta a KYVERUM/SIMIT) → **FEATURE 05** (Fase 2, mismo desarrollador).
- Restricciones por OT específico → FEATURE 05.
- Representación legal múltiple → backlog Entrega 3.

## 4. Diseño técnico propuesto

### Backend (`services/core-api`)
- Migración EF Core (en `Flit.Infrastructure`, único DbContext): columna `fines_query_source text not null default 'external'` en `admin.tenant_operational_policies`.
- Exponer el campo en los endpoints existentes de lectura/actualización de políticas operativas del tenant (módulo `Flit.Admin.Application`), registrando auditoría como el resto de la config.
- Gate en creación de trámite: si `only_own_vehicles = true`, validar contra el registro de vehículos propios del tenant en el handler de creación/preflight y devolver blocker con mensaje.

### Frontend (`frontend`)
- Pantalla de configuración de compañía: sección "Consultas" con selector Interna/Externa para comparendos (radio + texto de ayuda explicando la regla del SIMIT).
- Registro manual de trámite: respetar el check — deshabilitar/advertir cuando el vehículo no es propio.
- Refactor del guardado: estado sucio por sección, botón "Guardar todo" que dispara la persistencia de todas las secciones modificadas y muestra resultado consolidado.

## 5. Criterios de aceptación

1. En la configuración de la compañía puedo elegir la fuente de comparendos (interna/externa); el valor persiste, sobrevive recarga y queda auditado (quién, cuándo, valor anterior/nuevo).
2. Con `solo vehículos propios` activo, un gestor **no puede** radicar manualmente un trámite sobre un vehículo que no sea de la compañía, y recibe un mensaje claro; con el check inactivo el flujo no cambia.
3. "Guardar todo" persiste todas las secciones editadas de la configuración de compañía sin perder cambios ni requerir guardados parciales; si una sección falla, se informa cuál.
4. Dark mode y responsive correctos en las pantallas tocadas (criterio transversal FLIT 2.0).

## 6. Riesgos y mitigaciones

- **Definición de "vehículo propio"**: confirmar contra qué se valida (inventario del tenant / propietario = razón social del tenant en RUNT). Si no hay inventario, validar por coincidencia de NIT del propietario en la consulta RUNT del preflight.
- **Guardar todo**: la pantalla tiene varias secciones con endpoints distintos; evitar un mega-endpoint nuevo — orquestar desde el front con reporte por sección para mantener PRs pequeños.

## 7. Definición de hecho

- PR contra `develop`, migración incluida y aplicada en dev, build + tests verdes, revisión de un compañero.
- Pruebas: unit del gate `only_own_vehicles` + prueba manual del guardado completo documentada en el PR.
