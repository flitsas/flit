---
name: flit-suite-c-sdk
description: "Ejecuta el plan del Frente C de la FLIT Suite (desarrollador de Diagnóstico). Carga el plan del frente, las reglas de trabajo en paralelo y el contrato de plataforma; elige o confirma la tarea; respeta el gate de ADO, la propiedad de carpetas, el turno de migraciones y las banderas; actualiza la casilla y la bitácora del plan. Triggers: SDK de plataforma, services/shared, Flit.Platform.Contracts, eventos, outbox, RabbitMQ, AsyncAPI, consultas compartidas, Flit.Modules.Consultas, medición de consumo, plantilla flit-product, producto demo, frente C."
---

# Frente C — Consultas compartidas, SDK, eventos y plantilla de producto

Skill del **desarrollador de Diagnóstico**. Plan: [`docs/suite/frentes/frente-c-consultas-sdk-y-plantilla.md`](../../../docs/suite/frentes/frente-c-consultas-sdk-y-plantilla.md).

## 1. Al iniciar cada sesión

1. Lee, en este orden y completos:
   - `docs/suite/reglas-trabajo-paralelo.md`
   - `docs/suite/contrato-plataforma-v1.md`
   - `docs/suite/frentes/frente-c-consultas-sdk-y-plantilla.md`
   Del plan maestro (`docs/suite/plan-maestro.md`) y los ADR lee solo las secciones que cita la tarea.
2. Verifica la rama con `git branch --show-current`:
   - Debe tener la forma `feature/AB-<HU>-suite-c-<descripcion>`.
   - Si estás en `develop` o en `feature/nueva-suite-flit`, pide el número de la HU y propone crear la rama. No la crees sin confirmación.
3. Sincroniza con `git fetch origin` y `git merge origin/develop`. Si hay conflictos en archivos de otro frente, detente y avisa.
4. Elige la tarea:
   - Si el usuario indicó una (por ejemplo `C-03`), usa esa.
   - Si no, propone la primera casilla sin marcar de la sección **Estado** del plan cuyas dependencias de la tabla "Lo que consumes" estén entregadas, o que pueda avanzar con su stub.
   - Muestra el **Qué**, el **Dónde** y el **Hecho cuando** de la tarea y pide confirmación.
5. **Gate de ADO**: sigue las fases 1 y 2 de `.cursor/workflows/implement-story.md`. Valida el DoR y activa la HU **solo con confirmación explícita del usuario**. No toques archivos antes de eso. La tarea L-01 y las tareas de documentación del líder pueden omitir el gate si el usuario lo indica.

## 2. Mientras implementas

- **Solo modificas las carpetas de tu frente:**
- `services/shared/**`
- `services/core-api/src/Flit.Modules.Consultas/**` (nuevo), `services/core-api/src/Flit.Infrastructure/Consultations/**`, `services/core-api/src/Flit.Tramites.Application/UseCases/Consultations/**`
- Archivos nuevos del publicador de eventos en `services/core-api/src/Flit.Infrastructure/Messaging/`
- `templates/**`, `services/core-demo/**`, `frontend-demo/**`
- `contracts/asyncapi/**`
- Archivos compartidos (`Program.cs`, `InfrastructureExtensions.cs`, `FlitDbContext.cs`, `Directory.Packages.props`, `appsettings*.json`, `frontend/package.json`): solo con el protocolo R5. Una línea que llama a tu propia extensión, dentro del bloque `// === FLIT Suite ===`.
- **Carpetas de otro frente:** no las cambies. Si la tarea lo exige, detente y propone el cambio para que lo haga su dueño.
- **Migraciones de `core-api`:** antes de `dotnet ef migrations add`, pregunta al usuario si tiene el **turno de migración** (R6). Nunca resuelvas a mano un conflicto en `FlitDbContextModelSnapshot.cs`: borra la migración, sincroniza y regénérala.
- **Dependencias no entregadas:** usa el stub que indica el plan (`Stub<Nombre>`) y anótalo en la bitácora. Nunca implementes la pieza de otro frente.
- **Contrato:** si la tarea necesita cambiar el contrato, detente. Propón un PR que toque solo `docs/suite/contrato-plataforma-v1.md` y espera la aprobación de los tres frentes.
- **Banderas:** todo cambio visible o de autenticación va detrás de su bandera del contrato §9, apagada por defecto.
- **Tamaño:** si el diff pasa de 800 líneas, propone cómo dividirlo antes de seguir.
- Al mover consultas (C-04), las pruebas existentes de consultas deben pasar **sin modificarse**. Si una prueba necesita cambios, detente y consulta.
- Las consultas a proveedores reales solo en modo mock salvo aprobación explícita del líder.

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

1. En `docs/suite/frentes/frente-c-consultas-sdk-y-plantilla.md`: marca la casilla de la tarea en **Estado** y agrega una fila a la **Bitácora** con fecha, tarea, PR y stubs usados. Es el único archivo de plan que editas.
2. Prepara el PR a `develop`:
   - Título `HU<id>: <descripción>`.
   - Descripción con la tarea (`C-xx`), el criterio de terminado cumplido, las banderas, los stubs y las pruebas corridas.
   - **No lo fusiones sin confirmación explícita.**
3. Redacta el mensaje diario con la plantilla de la regla R10 para que el usuario lo publique.

## 5. Prohibido

- Editar el plan de otro frente o sus carpetas.
- `git push --force` sobre ramas publicadas.
- Encender banderas o cambiar configuración de QA o PDN.
- Pasar la HU a `Resolved` antes del merge en DEV.
- Escribir ADR en `docs/decisions/` sin aprobación humana (regla FLIT 15).
