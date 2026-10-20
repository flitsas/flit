# Reglas de trabajo — FLIT Suite

> **Versión 2 (2026-09-25).** La suite la construye **un solo desarrollador, Samuel Cardenas**, acordado
> con el líder técnico (ver el [README](README.md)). La versión 1 organizaba a tres frentes en paralelo;
> sus reglas de coordinación (propiedad de carpetas, turno de migraciones, mensaje diario) se retiraron
> porque generaban más espera que avance. El nombre del archivo se conserva para no romper enlaces.
>
> Complementa, sin reemplazar, `.cursor/rules/00-flit-conventions.mdc` y el flujo
> `.cursor/workflows/implement-story.md`, con una excepción explícita: el tamaño de los PRs (R1).

## Resumen

1. Todo PR va a `develop`, con merge commit.
2. **Un PR por Feature de ADO**, no por HU. Una rama por Feature.
3. **Cada commit empieza con el id de su HU** (`HU12958: …`).
4. Sin límite de 800 líneas para los PRs de la suite.
5. Si un Feature depende de otro que aún no se fusiona, su rama sale de la rama de ese Feature. No se espera el merge.
6. `develop` entra a la rama del Feature con frecuencia, mínimo una vez al día de trabajo.
7. Los archivos que comparte el resto del equipo se tocan con el protocolo R5.
8. El contrato de plataforma manda.
9. Todo lo visible va detrás de una bandera apagada por defecto.
10. Ningún PR sube el número de pruebas que fallan.

---

## R1. Ramas, PRs y merges

- **Unidad de PR: el Feature de ADO.** Todas las HUs de un Feature van en la misma rama y el mismo PR. Si un Feature es muy grande, se puede partir en dos PRs por etapas del propio Feature, nunca por HU.
- **Nombre de rama:** `feature/AB-<id Feature>-suite-<descripcion-corta>`. Ejemplo: `feature/AB-12888-suite-productos-y-roles`.
- **Commits:** cada uno empieza con el id de su HU, `HU<id>: descripción`. Una HU puede tener varios commits, pero ningún commit mezcla dos HUs. Así cada HU conserva su trazabilidad dentro del PR, y el campo Commits de la HU en ADO apunta a los suyos.
- **Tamaño:** sin límite de líneas. **Es una excepción a la regla 9 de `00-flit-conventions.mdc` y al rechazo automático de `review-pr.md`, válida solo para los PRs de la suite.** Compensa lo anterior: la descripción del PR va ordenada por HU y los commits se pueden revisar uno por uno.
- **PR en borrador desde el primer push.** CI corre en cada push y el líder puede mirar cuando quiera. Pasa a «listo para revisión» cuando el Feature está completo.
- **Dependencias entre Features:** si el Feature siguiente necesita el anterior y este no se ha fusionado, su rama sale de la rama del anterior. Cuando el anterior entra a `develop`, la rama siguiente se pone al día con `develop`.
- **Revisión y merge:** las revisiones que ya exige el repo (code review, seguridad, humano). Las partes sensibles (servidor OIDC, validación del token, sesión) las revisa el líder.
- **PRs abiertos antes de esta versión** (#444, #445, #446, #447) siguen como están.

## R2. Sincronización con `develop`

- `develop` entra a la rama del Feature con frecuencia, mínimo una vez por día de trabajo, porque el resto del equipo sigue fusionando cambios de Trámites:

  ```bash
  git fetch origin
  git merge origin/develop
  ```

- Con un solo desarrollador se puede rebasar la rama propia, empujando con `--force-with-lease`. Nunca se reescribe una rama ajena.
- Antes de pasar el PR a «listo», se vuelve a sincronizar y se corren las pruebas.

## R3. El contrato manda

- El contrato de plataforma es [`contrato-plataforma-v1.md`](contrato-plataforma-v1.md).
- Se programa contra el contrato. Las piezas que todavía no existen se reemplazan con un **stub** (`Stub<Nombre>`), que se borra en el PR que conecta la pieza real.
- Un cambio de contrato va con su fila en el historial del documento y se avisa al líder.

## R4. Carpetas

- Retirada en la versión 2: hay un solo desarrollador y `CODEOWNERS` se borró (#447).
- Los frentes A, B y C se conservan como **áreas de trabajo** para ordenar las tareas y los planes de `frentes/`, no como dueños de carpetas.

## R5. Archivos compartidos con el resto del equipo

El resto del equipo sigue trabajando en Trámites sobre los mismos archivos. Para no chocar con ellos:

| Archivo | Regla |
|---|---|
| `services/core-api/src/Flit.Api/Program.cs` | Una línea por módulo de la suite, dentro del bloque `// === FLIT Suite ===`, que llama a su propio método de extensión. No se reordenan líneas existentes |
| `services/core-api/src/Flit.Infrastructure/InfrastructureExtensions.cs` | Igual: una línea que llama a `Add<Modulo>Infrastructure()`, definido en un archivo propio |
| `services/core-api/src/Flit.Infrastructure/Persistence/FlitDbContext.cs` | Las configuraciones van en archivos propios (`ApplyConfigurationsFromAssembly` ya las recoge). Si hace falta un `DbSet`, va en un bloque `// Suite` |
| `services/core-api/Directory.Packages.props` | Paquetes nuevos (OpenIddict, RabbitMQ, Redis, …) con la auditoría de dependencias externas que exige la regla 18 |
| `appsettings*.json` | Secciones propias (`Suite:Oidc`, `Platform`, `Consultas`). No se editan secciones de Trámites |
| `contracts/openapi/core-api.v1.yaml` | Las rutas de plataforma no van aquí, sino en `contracts/openapi/platform.v1.yaml` |

## R6. Migraciones de `core-api`

- Retirado el turno de la versión 1. Las migraciones entran en el orden en que se fusionan.
- Nombre: `yyyyMMddHHmmss_HU<id>_<Descripcion>`, generado con `dotnet ef migrations add`.
- Si al traer `develop` aparece una migración más nueva que la propia, la propia se borra y se genera de nuevo sobre `develop`, para que quede después. **Nunca** se resuelve a mano un conflicto en `FlitDbContextModelSnapshot.cs`.
- Los servicios nuevos (`core-demo`, `core-comparendos`, `core-diagnostico`) tienen su propio DbContext.

## R7. Banderas

- Todo cambio visible para usuarios o que altere la autenticación va detrás de una bandera del contrato (§9), **apagada por defecto** en DEV, QA y PDN.
- Encender en DEV: PR a la configuración del ambiente, con aviso al equipo.
- Encender en QA o PDN: con la aprobación del líder.
- Una bandera se retira cuando lleva un sprint encendida en PDN sin incidentes.

## R8. Pruebas

- Cada PR trae pruebas de lo que cambia.
- **Línea base de fallos:** ningún PR aumenta el número de pruebas del frontend que fallan en `develop`.
- Los cambios en autenticación, dominio o sesión deben pasar `tests/Flit.Integration.Tests/MarcaBlanca` y la suite de paridad de la HU #12429.
- Antes de pasar un PR a «listo»:

  ```bash
  pnpm run build:core-api
  pnpm test
  pnpm typecheck
  ```

## R9. Azure DevOps

- Épica **«FLIT Suite — Plataforma»** (#12885) con Features por área. Cada tarea del plan (`A-01`, `B-03`, …) es una HU dentro de su Feature.
- La HU pasa a `Active` con confirmación humana antes de tocar archivos. Al cerrar el desarrollo queda en `Active` con Commits y Evidences.
- El PR del Feature lista sus HUs, y cada HU en ADO lleva los commits que la implementan.

## R10. Seguimiento

- **Revisión semanal con el líder:** demo en DEV de lo fusionado y avance del orden de trabajo del [README](README.md).
- Retirado el mensaje diario entre frentes de la versión 1.

## R11. Avance de los planes

- Las casillas y la bitácora de cada plan de `frentes/` se actualizan en el mismo PR que entrega las tareas.
- `plan-maestro.md`, `README.md` y el contrato se cambian en el PR que los necesite, con aviso al líder.

## R12. Agentes de IA

- Las skills `flit-suite-a-identidad`, `flit-suite-b-hub` y `flit-suite-c-sdk` cargan el plan de su área, estas reglas y el contrato. Ya no hay restricción de carpetas por frente.
- El agente se detiene en los gates humanos del repo: activación de HU en ADO, apertura del PR, merge, encendido de banderas y cambios de contrato.

## R13. Ambientes compartidos

- DEV es compartido. Hay que avisar antes de fusionar algo que cambie autenticación, hosts o configuración, aunque vaya detrás de una bandera.
- Nadie cambia QA ni PDN salvo el líder o con su aprobación.
- La salida de `ASPNETCORE_ENVIRONMENT=Development` (A-02) se hace primero en DEV y se observa un sprint antes de QA.
