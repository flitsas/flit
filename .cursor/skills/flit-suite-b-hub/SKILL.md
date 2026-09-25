---
name: flit-suite-b-hub
description: "Ejecuta el plan del Frente B de la FLIT Suite (desarrollador de Comparendos). Carga el plan del frente, las reglas de trabajo en paralelo y el contrato de plataforma; elige o confirma la tarea; respeta el gate de ADO, la propiedad de carpetas, el turno de migraciones y las banderas; actualiza la casilla y la bitácora del plan. Triggers: productos, habilitación de productos, RequireProduct, roles por producto, product_code, me/apps, manifiesto, DomainContext con producto, tenant_domains purpose, hub, frontend-hub, menú de productos, @flit/shell, @flit/ui, frente B."
---

# Frente B — Productos, habilitación por empresa, roles por producto y hub

Skill del **desarrollador de Comparendos**. Plan: [`docs/suite/frentes/frente-b-productos-y-hub.md`](../../../docs/suite/frentes/frente-b-productos-y-hub.md).

## 1. Al iniciar cada sesión

1. Lee, en este orden y completos:
   - `docs/suite/reglas-trabajo-paralelo.md`
   - `docs/suite/contrato-plataforma-v1.md`
   - `docs/suite/frentes/frente-b-productos-y-hub.md`
   Del plan maestro (`docs/suite/plan-maestro.md`) y los ADR lee solo las secciones que cita la tarea.
2. Verifica la rama con `git branch --show-current`:
   - Debe tener la forma `feature/AB-<HU>-suite-b-<descripcion>`.
   - Si estás en `develop` o en `feature/nueva-suite-flit`, pide el número de la HU y propone crear la rama. No la crees sin confirmación.
3. Sincroniza con `git fetch origin` y `git merge origin/develop`. Si hay conflictos en archivos de otro frente, detente y avisa.
4. Elige la tarea:
   - Si el usuario indicó una (por ejemplo `B-03`), usa esa.
   - Si no, propone la primera casilla sin marcar de la sección **Estado** del plan cuyas dependencias de la tabla "Lo que consumes" estén entregadas, o que pueda avanzar con su stub.
   - Muestra el **Qué**, el **Dónde** y el **Hecho cuando** de la tarea y pide confirmación.
5. **Gate de ADO**: sigue las fases 1 y 2 de `.cursor/workflows/implement-story.md`. Valida el DoR y activa la HU **solo con confirmación explícita del usuario**. No toques archivos antes de eso. La tarea L-01 y las tareas de documentación del líder pueden omitir el gate si el usuario lo indica.

## 2. Mientras implementas

- **Solo modificas las carpetas de tu frente:**
- `services/core-api/src/Flit.Modules.Platform/**` (nuevo)
- RBAC: `services/core-api/src/Flit.Infrastructure/Persistence/Configurations/Security/**`, `Persistence/Entities/Security/**`, `Persistence/Repositories/SecurityModuleRepository.cs`
- `services/core-api/src/Flit.Admin.Domain/Companies/Domains/**`, `services/core-api/src/Flit.Infrastructure/Domains/**`
- `packages/ui/**`, `packages/shell/**`
- `frontend-hub/**`, salvo `app/(auth)` y `redirects/legacy-tramites.ts` (frente A)
- `frontend/components/atom/Shell.tsx`, `frontend/components/atom/dock/**`, `frontend/lib/nav/**`, `frontend/components/atom/modules/RbacAdmin.tsx`
- `DomainContext.cs` y `DomainContextMiddleware.cs` solo para B-08, con revisión del frente A
- Archivos compartidos (`Program.cs`, `InfrastructureExtensions.cs`, `FlitDbContext.cs`, `Directory.Packages.props`, `appsettings*.json`, `frontend/package.json`): solo con el protocolo R5. Una línea que llama a tu propia extensión, dentro del bloque `// === FLIT Suite ===`.
- **Carpetas de otro frente:** no las cambies. Si la tarea lo exige, detente y propone el cambio para que lo haga su dueño.
- **Migraciones de `core-api`:** antes de `dotnet ef migrations add`, pregunta al usuario si tiene el **turno de migración** (R6). Nunca resuelvas a mano un conflicto en `FlitDbContextModelSnapshot.cs`: borra la migración, sincroniza y regénérala.
- **Dependencias no entregadas:** usa el stub que indica el plan (`Stub<Nombre>`) y anótalo en la bitácora. Nunca implementes la pieza de otro frente.
- **Contrato:** si la tarea necesita cambiar el contrato, detente. Propón un PR que toque solo `docs/suite/contrato-plataforma-v1.md` y espera la aprobación de los tres frentes.
- **Banderas:** todo cambio visible o de autenticación va detrás de su bandera del contrato §9, apagada por defecto.
- **Tamaño:** si el diff pasa de 800 líneas, propone cómo dividirlo antes de seguir.
- Los cambios de `DomainContext` y `tenant_domains` deben pasar `services/core-api/tests/Flit.Integration.Tests/MarcaBlanca` sin cambios de comportamiento.
- `RequireProduct` se entrega en modo "solo registra" (`Suite:ProductAccess:Enforce` apagada). Nunca propongas encenderla en QA o PDN.
- Al mover pantallas al hub, un PR por sección, con su redirección desde la ruta vieja.

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

1. En `docs/suite/frentes/frente-b-productos-y-hub.md`: marca la casilla de la tarea en **Estado** y agrega una fila a la **Bitácora** con fecha, tarea, PR y stubs usados. Es el único archivo de plan que editas.
2. Prepara el PR a `develop`:
   - Título `HU<id>: <descripción>`.
   - Descripción con la tarea (`B-xx`), el criterio de terminado cumplido, las banderas, los stubs y las pruebas corridas.
   - **No lo fusiones sin confirmación explícita.**
3. Redacta el mensaje diario con la plantilla de la regla R10 para que el usuario lo publique.

## 5. Prohibido

- Editar el plan de otro frente o sus carpetas.
- `git push --force` sobre ramas publicadas.
- Encender banderas o cambiar configuración de QA o PDN.
- Pasar la HU a `Resolved` antes del merge en DEV.
- Escribir ADR en `docs/decisions/` sin aprobación humana (regla FLIT 15).
