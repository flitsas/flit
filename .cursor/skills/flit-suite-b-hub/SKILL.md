---
name: flit-suite-b-hub
description: "Ejecuta el plan del Frente B de la FLIT Suite (área de trabajo; toda la suite la construye un solo desarrollador). Carga el plan del frente, las reglas de trabajo y el contrato de plataforma; elige o confirma la tarea; respeta el gate de ADO, las banderas, un PR por Feature y los commits por HU; actualiza la casilla y la bitácora del plan. Triggers: productos, habilitación de productos, RequireProduct, roles por producto, product_code, me/apps, manifiesto, DomainContext con producto, tenant_domains purpose, hub, frontend-hub, menú de productos, @flit/shell, @flit/ui, frente B."
---

# Frente B — Productos, habilitación por empresa, roles por producto y hub

Skill del área **B** de la FLIT Suite. Desde el 2026-09-25 toda la suite la construye **un solo desarrollador** (reglas v2: sin propiedad de carpetas, un PR por Feature). Plan: [`docs/suite/frentes/frente-b-productos-y-hub.md`](../../../docs/suite/frentes/frente-b-productos-y-hub.md).

## 1. Al iniciar cada sesión

1. Lee, en este orden y completos:
   - `docs/suite/reglas-trabajo-paralelo.md`
   - `docs/suite/contrato-plataforma-v1.md`
   - `docs/suite/frentes/frente-b-productos-y-hub.md`
   Del plan maestro (`docs/suite/plan-maestro.md`) y los ADR lee solo las secciones que cita la tarea.
2. Verifica la rama con `git branch --show-current`:
   - Debe tener la forma `feature/AB-<Feature>-suite-<descripcion>` (un PR por Feature de ADO, reglas R1).
   - Si estás en `develop` o en `feature/nueva-suite-flit`, pide el número del Feature de ADO y propone crear la rama. No la crees sin confirmación.
3. Sincroniza con `git fetch origin` y `git merge origin/develop`. Si hay conflictos, detente y avisa.
4. Elige la tarea:
   - Si el usuario indicó una (por ejemplo `B-03`), usa esa.
   - Si no, propone la primera casilla sin marcar de la sección **Estado** del plan cuyas dependencias de la tabla "Lo que consumes" estén entregadas, o que pueda avanzar con su stub.
   - Muestra el **Qué**, el **Dónde** y el **Hecho cuando** de la tarea y pide confirmación.
5. **Gate de ADO**: sigue las fases 1 y 2 de `.cursor/workflows/implement-story.md`. Valida el DoR y activa la HU **solo con confirmación explícita del usuario**. No toques archivos antes de eso. La tarea L-01 y las tareas de documentación del líder pueden omitir el gate si el usuario lo indica.

## 2. Mientras implementas

- **Carpetas:** puedes tocar cualquier área de la suite (reglas v2, R4 retirada). Los archivos que comparte el resto del equipo (`Program.cs`, `InfrastructureExtensions.cs`, `FlitDbContext.cs`, `Directory.Packages.props`, `appsettings*.json`, `frontend/package.json`) solo con el protocolo R5: una línea que llama a la extensión del módulo, dentro del bloque `// === FLIT Suite ===`.
- **Migraciones de `core-api`:** sin turno (R6). Si al traer `develop` aparece una migración más nueva que la tuya, borra la tuya y genérala de nuevo sobre `develop`. Nunca resuelvas a mano un conflicto en `FlitDbContextModelSnapshot.cs`.
- **Dependencias no entregadas:** usa el stub que indica el plan (`Stub<Nombre>`) y anótalo en la bitácora.
- **Contrato:** si la tarea necesita cambiarlo, detente y propónlo al usuario. El cambio lleva su fila en el historial del contrato (R3).
- **Banderas:** todo cambio visible o de autenticación va detrás de su bandera del contrato §9, apagada por defecto.
- **Tamaño y commits:** sin límite de líneas (un PR por Feature, R1). Cada commit empieza con el id de su HU (`HU<id>: …`) y ningún commit mezcla dos HUs.
- Los cambios de `DomainContext` y `tenant_domains` deben pasar `services/core-api/tests/Flit.Integration.Tests/MarcaBlanca` sin cambios de comportamiento.
- `RequireProduct` se entrega en modo "solo registra" (`Suite:ProductAccess:Enforce` apagada). Nunca propongas encenderla en QA o PDN.
- Al mover pantallas al hub, un commit por sección, con su redirección desde la ruta vieja.

## 3. Pruebas

- Agrega pruebas de lo que cambias (skill `dev-tester`).
- Corre:

  ```bash
  pnpm run build:core-api
  pnpm test
  pnpm typecheck
  ```

- Compara los fallos del frontend con `docs/suite/linea-base-pruebas.md`. El número no puede subir. Si sube, detente y muestra cuáles son nuevos.

## 4. Al terminar la tarea

1. En `docs/suite/frentes/frente-b-productos-y-hub.md`: marca la casilla de la tarea en **Estado** y agrega una fila a la **Bitácora** con fecha, tarea, PR y stubs usados.
2. Actualiza el PR del Feature a `develop` (en borrador desde el primer push):
   - Título con el Feature: `F<id Feature>: <descripción>`.
   - Descripción ordenada por HU: por cada una, la tarea (`B-xx`), el criterio de terminado cumplido, las banderas, los stubs y las pruebas corridas.
   - **No lo abras, no lo pases a «listo» ni lo fusiones sin confirmación explícita.**

## 5. Prohibido

- `git push --force` sin `--force-with-lease`, o sobre ramas ajenas.
- Encender banderas o cambiar configuración de QA o PDN.
- Pasar la HU a `Resolved`: al cerrar el desarrollo queda en `Active` con Commits y Evidences.
- Escribir ADR en `docs/decisions/` sin aprobación humana (regla FLIT 15).
