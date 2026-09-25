# FLIT Suite — Construcción de la plataforma

Convertimos FLIT en una suite de productos al estilo de Google: un **hub en `flitsas.online`**, donde el usuario inicia sesión, ve los productos que su empresa contrató y cambia entre ellos, y **productos en su propio subdominio** (`tramites.`, `comparendos.`, `diagnostico.`), cada uno con su menú. **Primero construimos la plataforma; después arrancan Comparendos y Diagnóstico.**

> **Cambio de equipo (2026-09-25, acordado con el líder técnico):** toda la construcción de la suite —las tareas de los frentes A, B y C— la hace **un solo desarrollador, Samuel Cardenas**. Tres desarrolladores en paralelo generaban más dependencias entre sí que avance. Los frentes se conservan como **áreas de trabajo** para ordenar las tareas, no como personas, y las reglas de coordinación entre frentes (propiedad de carpetas, turno de migraciones) dejan de aplicar. El orden de trabajo y la forma de trabajar se ajustan en un PR siguiente.

## Documentos

| Documento | Para qué |
|---|---|
| [Plan maestro](plan-maestro.md) | Diagnóstico, arquitectura objetivo y fases. El porqué de todo |
| [Reglas de trabajo en paralelo](reglas-trabajo-paralelo.md) | Cómo no pisarnos: ramas, carpetas, migraciones, banderas, rituales |
| [Contrato de plataforma v1](contrato-plataforma-v1.md) | Formas compartidas: token, endpoints, eventos, paquetes. Se cierra en la semana 1 |
| [Frente A — Identidad y Trámites](frentes/frente-a-identidad-y-tramites.md) | Identidad, sesión y Trámites en la suite |
| [Frente B — Productos y hub](frentes/frente-b-productos-y-hub.md) | Productos, roles por producto y hub |
| [Frente C — Consultas, SDK y plantilla](frentes/frente-c-consultas-sdk-y-plantilla.md) | Eventos, consultas compartidas, SDK y plantilla |
| [Frente L — Líder e infraestructura](frentes/frente-l-lider-e-infraestructura.md) | Plan del líder técnico |
| [Borradores de ADR 0061–0065](adr-borradores/README.md) | Decisiones pendientes de aprobación |

## Quién hace qué

| Frente | Responsable | Construye en la plataforma | Entrega a los demás | Después |
|---|---|---|---|---|
| **A · Identidad y Trámites** | Samuel Cardenas | Salida de `Development` y validación real del token; servidor OIDC con login en el hub; token por producto y refresh; `@flit/auth`; Trámites en `tramites.flitsas.online` | Token y emisores (B, C); `@flit/auth` (B, C) | Roadmap de Trámites |
| **B · Productos y hub** | Samuel Cardenas | Productos y su habilitación por empresa; roles por producto; `RequireProduct`; `DomainContext` con producto; `@flit/ui` y `@flit/shell`; el hub con inicio, menú de productos y administración de plataforma | Acceso a productos (A); `@flit/ui` y `@flit/shell` (A, C); manifiesto y `me/apps` (C) | Comparendos |
| **C · Consultas, SDK y plantilla** | Samuel Cardenas | Eventos con outbox y RabbitMQ; SDK .NET; consultas externas compartidas con medición; plantilla de producto; producto de prueba `demo` | Publicador de eventos (A, B); plantilla (B y C en la Fase 3) | Diagnóstico |
| **L · Líder** | Jorman Copete (líder técnico) | PR inicial, workspace, `CODEOWNERS`, ADO, k3s, Redis, RabbitMQ, DNS, certificados, CD, aprobaciones y rituales | Todo lo que desbloquea a A, B y C | Coordinación |

## Fases y tareas por frente

| Fase | Frente A | Frente B | Frente C | Líder |
|---|---|---|---|---|
| **0 · Fundaciones** (≈2–3 semanas) | A-00 contrato · A-01 inventario de ambientes · A-02 DEV sin `Development` · A-03 token validado · A-04 espiga OpenIddict | B-00 contrato · B-01 inventario plataforma contra Trámites · B-02 `@flit/ui` v0 | C-00 contrato · C-01 publicador de eventos · C-02 contratos .NET | L-01 PR inicial · L-02 arranque · L-03 workspace · L-04 ADO · L-05 línea base · L-06 ADRs · L-07 Redis y RabbitMQ · L-11 SMTP |
| **1 · Núcleo** (≈4–6 semanas) | A-05 servidor OIDC · A-06 login en el hub · A-07 token por producto · A-08 Marca Blanca sobre OIDC | B-03 schema `platform` · B-04 roles por producto · B-05 acceso a productos · B-06 endpoints y `RequireProduct` · B-07 booleans a habilitación · B-08 `DomainContext` · B-09 esqueleto del hub | C-03 SDK · C-04 mover consultas · C-05 API de consultas · C-06 medición | L-08 k3s · L-09 DNS y certificados · L-10 CD por app |
| **2 · Hub y Trámites en la suite** (≈4–6 semanas) | A-09 `@flit/auth` · A-10 Trámites con sesión nueva · A-11 Trámites en su host · A-12 URLs de correo · A-13 cierre global · A-14 retirar sesión vieja | B-10 `@flit/shell` · B-11 inicio y menú de productos · B-12 administración en el hub · B-13 Trámites con el shell | C-07 plantilla · C-08 producto `demo` · C-09 OpenAPI y cliente · C-10 guía | L-12 QA y PDN · L-13 rituales · L-14 puerta de salida |
| **3 · Productos** | Roadmap de Trámites | Comparendos desde la plantilla | Diagnóstico desde la plantilla | Coordinación |

## Tablero de dependencias

El líder lo actualiza en la revisión semanal. Estado: ⏳ pendiente · 🟡 en curso · ✅ entregado.

| Entrega | Dueño | La esperan | Estado |
|---|---|---|---|
| PR `feature/nueva-suite-flit` en `develop` (plan + arreglo PDN) | L | Todos | ✅ PR #429 |
| Contrato v1 cerrado | L + A + B + C | Todos | ✅ PR #433, sin reunión |
| Interfaz `IProductAccessResolver` y `ProductCodes` (contrato §4) | B | A | ✅ PR #436 |
| Workspace con `packages/*` y `frontend-*`, `CODEOWNERS` y bloques compartidos | L | B, C | ✅ PR #437 (`CODEOWNERS` retirado al pasar a un solo desarrollador) |
| Inventario plataforma contra Trámites y decisión D1 | B | A | 🟡 PR #444 |
| Schema `platform`: productos y su habilitación por empresa | B | A | 🟡 PR #446 |
| Token validado en gateway y API (DEV) | A | Todos | ⏳ |
| `@flit/ui` v0 | B | C | 🟡 PR #445 |
| `Flit.Platform.Contracts` | C | Todos | ⏳ |
| Redis y RabbitMQ en DEV | L | A, B, C | ⏳ |
| Esqueleto de `frontend-hub` | B | A | ⏳ |
| Publicador de eventos | C | A, B | ⏳ |
| Servidor OIDC y `/platform/issuers` en DEV | A | B, C | ⏳ |
| `IProductAccessResolver` real | B | A | ⏳ |
| `DomainContext.ProductCode` | B | A | ⏳ |
| Manifiesto y `me/apps` | B | C | ⏳ |
| k3s DEV con Argo CD | L | C | ⏳ |
| `@flit/auth` v1 | A | B, C | ⏳ |
| `@flit/shell` v1 | B | A, C | ⏳ |
| Plantilla `flit-product` | C | B, C (Fase 3) | ⏳ |
| Producto `demo` en DEV | C | Puerta de salida | ⏳ |

## Migraciones de `core-api`

Con un solo desarrollador ya no hay turno (la regla R6 queda sin efecto). Las migraciones entran en el
orden en que se fusionan: si al rebasar una rama sobre `develop` aparece una migración más nueva que la
propia, la propia se regenera para quedar después y el snapshot se actualiza sobre `develop`.

## Cómo empieza cada uno

### 1. Preparar el repositorio (todos, una vez)

```bash
git fetch origin
git switch feature/nueva-suite-flit
git pull
```

Cuando el líder fusione esa rama en `develop` (tarea L-01), se parte de `develop` (reglas R1):

```bash
git switch develop
git pull --ff-only
```

Instalar dependencias:

```bash
pnpm install --frozen-lockfile
```

```bash
pnpm run install:dotnet
```

### 2. Dejar disponible la skill de tu frente

Las skills viven en `.cursor/skills/`, que se versiona. **Cursor** las ve directamente. **Claude Code** lee `.claude/`, que no se versiona. Para copiarlas, usa el script del repo si tienes PowerShell:

```bash
pwsh .cursor/sync-to-claude.ps1
```

O cópialas a mano:

```bash
mkdir -p .claude/skills && cp -R .cursor/skills/flit-suite-* .claude/skills/
```

| Frente | Skill |
|---|---|
| A · Identidad y Trámites | `flit-suite-a-identidad` |
| B · Productos y hub | `flit-suite-b-hub` |
| C · Consultas, SDK y plantilla | `flit-suite-c-sdk` |
| L · Líder | `flit-suite-l-infra` |

### 3. Crear la rama de tu primera HU

Cada tarea del plan es una HU en ADO (reglas R9). Con el número de la HU:

```bash
git switch -c feature/AB-<HU>-suite-<a|b|c|l>-<descripcion-corta>
```

### 4. Invocar tu plan

En **Claude Code**, escribe el comando de tu skill con la tarea y la HU. Ejemplos:

```text
/flit-suite-a-identidad tarea A-01, HU #<número>
```

```text
/flit-suite-b-hub tarea B-01, HU #<número>
```

```text
/flit-suite-c-sdk tarea C-02, HU #<número>
```

```text
/flit-suite-l-infra tarea L-01
```

En **Cursor**, en el chat del agente:

```text
Usa la skill flit-suite-a-identidad. Tarea A-01, HU #<número>.
```

Sin tarea, la skill propone la primera casilla sin marcar de tu plan cuyas dependencias ya estén entregadas.

**Qué hace la skill en cada sesión:**

1. Lee las reglas, el contrato y tu plan.
2. Verifica la rama y la sincroniza con `develop`.
3. Te muestra la tarea con su criterio de terminado y sus dependencias.
4. Se detiene para que confirmes la activación de la HU en ADO. Es el gate humano del flujo `implement-story`.
5. Implementa solo en las carpetas de tu frente, con stubs para lo que otros no han entregado y detrás de banderas.
6. Corre las pruebas y las compara con la línea base.
7. Marca la casilla y escribe la bitácora en tu plan.
8. Prepara el PR y tu mensaje diario. **No fusiona sin tu confirmación.**

### 5. Primeras tareas de cada uno

| Frente | Semana 1 |
|---|---|
| A | A-00 contrato (en la reunión de arranque) y A-01 inventario de ambientes |
| B | B-00 contrato y B-01 inventario plataforma contra Trámites |
| C | C-00 contrato y C-02 `Flit.Platform.Contracts` |
| L | L-01 PR inicial, L-02 arranque, L-03 workspace, L-04 ADO y L-05 línea base |

## Puerta de salida

Comparendos y Diagnóstico arrancan cuando se cumple en QA la lista del [plan maestro §5.4](plan-maestro.md#54-puerta-de-salida-plataforma-lista). La verifica el líder (L-14).
