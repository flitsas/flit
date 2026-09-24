# Reglas para trabajar en paralelo sin pisarnos — FLIT Suite

> Aplica a los tres frentes y al líder durante la construcción de la plataforma (Fases 0 a 2) y
> después a los productos. Complementa, sin reemplazar, las reglas de
> `.cursor/rules/00-flit-conventions.mdc` y el flujo `.cursor/workflows/implement-story.md`.
> Hereda las prácticas del plan de trabajo paralelo de fase 1
> (`.cursor/docs/plans/artefacto_trabajo_paralelo_bb9a902d.plan.md`): contrato primero,
> aislamiento por schema, PRs pequeños y turno de migraciones.

## Resumen en diez líneas

1. Todos parten de `develop` y todo PR va a `develop`, con merge commit.
2. Una rama por HU: `feature/AB-<HU>-suite-<frente>-<descripcion>`.
3. PRs de hasta 800 líneas, vivos como máximo 3 días hábiles.
4. Cada frente es dueño de sus carpetas. En carpetas ajenas no se cambia nada sin aprobación del dueño.
5. Los archivos compartidos se tocan con el protocolo de la regla R5.
6. Una sola migración de `core-api` en vuelo a la vez (turno de migración).
7. El contrato de plataforma manda. Se cambia por PR con aprobación de los tres frentes.
8. Todo lo visible va detrás de una bandera apagada por defecto.
9. Ningún PR sube el número de pruebas que fallan.
10. Mensaje diario de sincronización y revisión semanal en DEV.

---

## R1. Ramas, PRs y merges

- **Punto de partida.** El PR de `feature/nueva-suite-flit` a `develop` se fusiona primero. Contiene el plan, este documento, el contrato y el arreglo de PDN `5ae9578f`. Mientras se aprueba, las ramas pueden salir de `feature/nueva-suite-flit`. Como el repo fusiona con merge commit, esos commits no se duplican cuando llegue a `develop`.
- **Después de ese PR**, toda rama sale de `develop` y vuelve a `develop`. No hay rama de integración larga: el CD despliega `develop` en DEV y la suite necesita probarse ahí.
- **Nombre de rama:** `feature/AB-<id HU>-suite-<a|b|c|l>-<descripcion-corta>`. Ejemplo: `feature/AB-13010-suite-a-gateway-valida-jwt`.
- **Commits:** `HU<id>: descripción`, según la regla vigente. Si el PR mezcla dos HUs, sepáralo.
- **Tamaño:** hasta 800 líneas. Si no cabe, divide por capa (`[BACKEND]` y `[FRONTEND]`) o detrás de una bandera.
- **Vida máxima:** 3 días hábiles. Una rama que envejece es la primera fuente de conflictos.
- **Revisión:** al menos un desarrollador **de otro frente** más las revisiones que ya exige el repo (code review, seguridad, humano). Así los tres conocen todo el código.
- **Merge:** el autor fusiona después de la aprobación, con el build en verde y sin hilos abiertos. Nadie fusiona el PR de otro sin avisar.

## R2. Sincronización con `develop`

- Cada mañana, antes de escribir código:

  ```bash
  git fetch origin
  git merge origin/develop
  ```

- Si tu rama nunca se publicó, puedes usar `git rebase origin/develop`. Una rama ya publicada **no** se reescribe ni se empuja con `--force`.
- Antes de abrir el PR, vuelve a sincronizar y corre las pruebas.

## R3. El contrato manda

- El contrato de plataforma es [`contrato-plataforma-v1.md`](contrato-plataforma-v1.md). Se cierra en la semana 1.
- Programa contra el contrato, no contra la implementación de otro frente.
- Si dependes de algo que otro frente no ha entregado, usa un **stub** con el nombre `Stub<Nombre>` y bórralo en el PR que conecta la pieza real.
- Para cambiar el contrato: PR que toca **solo** ese archivo, con la aprobación de los tres frentes, y una fila nueva en su historial. Luego cada frente adapta su código en PRs aparte.
- Nunca implementes la pieza de otro frente para desbloquearte. Usa el stub y avisa en el mensaje diario.

## R4. Propiedad de carpetas

| Carpeta o archivo | Dueño |
|---|---|
| `services/core-api/src/Flit.Gateway/**` | A |
| `services/core-api/src/Flit.Modules.Identity/**` (nuevo) | A |
| `services/core-api/src/Flit.Modules.Security.Application/Auth/**` | A |
| `services/core-api/src/Flit.Infrastructure/Security/**` | A |
| `services/core-api/src/Flit.Api/Authorization/**` y `Flit.Api/Middleware/Domain*` | A. El cambio de `DomainContext` a producto lo hace B con revisión de A |
| `packages/auth/**`, `frontend/lib/auth/**`, `frontend/lib/api/client.ts`, `frontend/lib/api/base-url.ts`, `frontend/middleware.ts` | A |
| `frontend-hub/app/(auth)/**`: login, recuperación, activación | A |
| `services/core-api/src/Flit.Modules.Platform/**` (nuevo: productos, suscripciones, me/apps, manifiesto) | B |
| RBAC: `Flit.Infrastructure/Persistence/Configurations/Security/**`, `Persistence/Entities/Security/**`, `SecurityModuleRepository.cs` | B |
| `Flit.Admin.Domain/Companies/Domains/**`, `Flit.Infrastructure/Domains/**` | B |
| `packages/ui/**`, `packages/shell/**` | B |
| `frontend-hub/**` salvo `app/(auth)` | B |
| `frontend/components/atom/Shell.tsx`, `frontend/components/atom/dock/**`, `frontend/lib/nav/**`, `frontend/components/atom/modules/RbacAdmin.tsx` | B |
| `services/shared/**` (SDK y contratos .NET) | C |
| `services/core-api/src/Flit.Modules.Consultas/**` (nuevo), `Flit.Infrastructure/Consultations/**`, `Flit.Tramites.Application/UseCases/Consultations/**` | C |
| Publicador genérico de eventos en `Flit.Infrastructure/Messaging/` (archivos nuevos) | C |
| `templates/**`, `services/core-demo/**`, `frontend-demo/**` | C |
| `contracts/asyncapi/**` | C |
| `docker-compose*.yml`, `.github/**`, `deploy/**`, `pnpm-workspace.yaml`, `package.json` raíz, `docs/despliegue-y-puertos.md`, `CODEOWNERS` | Líder |
| `docs/suite/plan-maestro.md`, `docs/suite/README.md`, `docs/suite/contrato-plataforma-v1.md` | Líder, con aprobación de los tres frentes |
| `docs/suite/frentes/frente-<x>-*.md` | Cada frente edita **solo el suyo** |

- En la Fase 3, `services/core-comparendos`, `frontend-comparendos`, `services/core-diagnostico` y `frontend-diagnostico` pertenecen a sus desarrolladores.
- El líder crea `CODEOWNERS` en la semana 1 con esta tabla, para que GitHub pida la revisión del dueño automáticamente.

## R5. Archivos compartidos: protocolo

Estos archivos los tocan varios frentes. Cambios mínimos y siempre con este método:

| Archivo | Regla |
|---|---|
| `services/core-api/src/Flit.Api/Program.cs` | Cada frente agrega **una línea** que llama a su propio método de extensión (`app.MapPlatformEndpoints()`, `app.MapIdentityEndpoints()`, …), dentro del bloque marcado `// === FLIT Suite ===`. No se reordenan líneas existentes |
| `services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs` | Igual: una línea que llama a `Add<Modulo>Infrastructure()`, definido en un archivo propio (`IdentityInfrastructureExtensions.cs`, `PlatformInfrastructureExtensions.cs`, `ConsultasInfrastructureExtensions.cs`) |
| `services/core-api/src/Flit.Infrastructure/Persistence/FlitDbContext.cs` | Los `DbSet` nuevos van en un bloque por frente con comentario `// Suite <frente>`. Las configuraciones van en archivos propios; `ApplyConfigurationsFromAssembly` ya las recoge |
| `services/core-api/Directory.Packages.props` | Paquetes nuevos (OpenIddict, RabbitMQ, Redis, …) en un PR pequeño y exclusivo, con la auditoría de dependencias externas que exige la regla 18 |
| `appsettings*.json` | Cada frente agrega su propia sección (`Suite:Oidc`, `Platform`, `Consultas`). No edita secciones ajenas |
| `contracts/openapi/core-api.v1.yaml` | No se agregan rutas de plataforma aquí. Van en `contracts/openapi/platform.v1.yaml` |
| `frontend/package.json` | Dependencias nuevas en PR pequeño; avisar en el mensaje diario |

## R6. Turno de migraciones de `core-api`

`core-api` tiene un solo `FlitDbContextModelSnapshot.cs`: dos migraciones en paralelo siempre chocan.

1. Anuncia en el canal: **"Tomo turno de migración: HU<id> <descripción>"**. Si otro lo tiene, espera.
2. `git fetch && git merge origin/develop` justo antes de generar la migración.
3. Genera con el nombre `yyyyMMddHHmmss_HU<id>_<Descripcion>` usando `pnpm migrate:core-api` o `dotnet ef migrations add`.
4. Abre el PR de la migración **sola o con su configuración EF mínima**, y fusiónalo ese mismo día.
5. Anuncia **"Libero turno de migración"**.
6. Si aun así chocas en el snapshot: borra tu migración, sincroniza con `develop` y genérala de nuevo. **Nunca** se resuelve a mano un conflicto en el snapshot.

Los servicios nuevos (`core-demo`, luego `core-comparendos`, `core-diagnostico`) tienen su propio DbContext y no necesitan turno.

## R7. Banderas

- Todo cambio visible para usuarios o que altere la autenticación va detrás de una bandera del contrato (§9), **apagada por defecto** en DEV, QA y PDN.
- Solo el dueño de la bandera la enciende, por PR a la configuración del ambiente y con aviso en el canal.
- Para encender en QA o PDN se necesita la aprobación del líder.
- Una bandera se retira cuando lleva un sprint encendida en PDN sin incidentes. Lo hace su dueño, en un PR aparte.

## R8. Pruebas

- Cada PR trae pruebas de lo que cambia (skill `dev-tester`).
- **Línea base de fallos.** Hoy fallan 86 de 4 794 pruebas del frontend en `develop@e8b7ca65`. Ningún PR puede aumentar ese número. El líder publica la lista en `docs/suite/linea-base-pruebas.md` en la semana 1 y la actualiza cuando se corrijan.
- Cambios en autenticación, dominio o sesión (frentes A y B) deben pasar `tests/Flit.Integration.Tests/MarcaBlanca` y la suite de paridad de la HU #12429.
- Antes de pedir revisión:

  ```bash
  pnpm run build:core-api
  pnpm test
  pnpm typecheck
  ```

  `pnpm test` corre las pruebas del frontend y las de `core-api`; estas últimas necesitan el build previo.

## R9. Azure DevOps

- El líder crea una Épica **"FLIT Suite — Plataforma"** con Features por frente. Máximo 8 HUs por Feature, así que A y B tienen dos Features cada uno (ver cada plan).
- Cada tarea del plan (`A-01`, `B-03`, …) es una HU o se divide en HUs `[BACKEND]` y `[FRONTEND]`.
- Rige la regla vigente: la HU pasa a `Active` **con confirmación humana explícita** antes de tocar archivos, y a `Resolved` solo después del merge en DEV.
- Las HUs se crean en el **siguiente** sprint, nunca en el activo.

## R10. Rituales de sincronización

- **Mensaje diario (asíncrono, antes de las 9:30)**, con esta plantilla:

  ```
  Frente <A|B|C|L> · <nombre>
  Ayer: <PRs fusionados / avance>
  Hoy: <HU y tarea del plan>
  Toco archivos compartidos: <no | cuáles>
  Turno de migración: <no | lo necesito | lo tengo>
  Bloqueos: <ninguno | qué y de quién>
  Cambios de contrato propuestos: <no | enlace al PR>
  ```

- **Revisión semanal (30 minutos, último día del sprint)**: demo en DEV de lo fusionado, revisión del tablero de dependencias de `docs/suite/README.md` y de la puerta de salida. El líder actualiza el tablero ese día.
- **Bloqueo de más de 4 horas** por otro frente: stub y aviso inmediato en el canal, no al día siguiente.

## R11. Avance de los planes

- Cada frente marca sus casillas y escribe en la **Bitácora** de su plan dentro del mismo PR que entrega la tarea. Como cada uno edita solo su archivo, no hay conflictos.
- `plan-maestro.md`, `README.md` y el contrato se cambian en PRs propios del líder.

## R12. Agentes de IA

- Cada frente trabaja con su skill (`flit-suite-a-identidad`, `flit-suite-b-hub`, `flit-suite-c-sdk`, `flit-suite-l-infra`). La skill carga su plan, estas reglas y el contrato.
- El agente **no** modifica carpetas de otro frente ni archivos compartidos fuera del protocolo R5 sin instrucción humana explícita.
- El agente se detiene en los gates humanos del repo: activación de HU en ADO, merge, encendido de banderas, cambios de contrato.

## R13. Ambientes compartidos

- DEV es compartido. Avisa antes de fusionar algo que cambie autenticación, hosts o configuración, aunque vaya detrás de una bandera.
- Nadie cambia QA ni PDN salvo el líder o con su aprobación.
- La salida de `ASPNETCORE_ENVIRONMENT=Development` (tarea A-01) se hace primero en DEV y se observa un sprint antes de QA.
