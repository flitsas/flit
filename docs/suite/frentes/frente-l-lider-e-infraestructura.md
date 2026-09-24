# Frente L — Líder técnico, infraestructura y coordinación

> **Responsable:** líder técnico. **Skill:** `flit-suite-l-infra`.
> **Prefijo de rama:** `feature/AB-<HU>-suite-l-…`.
>
> Leer antes de empezar: [README de la suite](../README.md), [reglas](../reglas-trabajo-paralelo.md),
> [contrato v1](../contrato-plataforma-v1.md) §1, §9 y §11, [plan maestro](../plan-maestro.md)
> §4.2, §4.10 y §5, y `deploy/edge/`.

## Objetivo

Que ningún frente se bloquee por infraestructura, permisos o coordinación. Es dueño de lo compartido: workspace, CI/CD, ambientes, DNS, certificados, k3s, Redis, RabbitMQ, ADO, `CODEOWNERS` y la aprobación de ADRs y banderas en QA y PDN.

## Lo que entregas y cuándo

| Entrega | Para | Cuándo |
|---|---|---|
| PR de `feature/nueva-suite-flit` a `develop` fusionado | Todos | Día 1 |
| Reunión de arranque y contrato v1 cerrado | Todos | Semana 1 |
| Workspace con `packages/*` y `frontend-*`, `CODEOWNERS`, bloques `// === FLIT Suite ===` | Todos | Semana 1 |
| Épica, Features y HUs del siguiente sprint en ADO | Todos | Semana 1 |
| Línea base de pruebas publicada | Todos | Semana 1 |
| Puertos asignados para hub, `demo`, Comparendos y Diagnóstico | B, C | Semana 1 |
| Redis y RabbitMQ en DEV | A, B, C | Fin de la Fase 0 |
| k3s DEV con Argo CD, ingress y cert-manager | C (`demo`), B (hub) | Inicio de la Fase 1 |
| Hosts DEV del contrato §1 con certificado | Todos | Mitad de la Fase 1 |
| CD por app (hub, `demo`) | B, C | Fin de la Fase 1 |

---

## Estado

- [ ] L-01 Fusionar `feature/nueva-suite-flit` en `develop`
- [ ] L-02 Reunión de arranque y cierre del contrato v1
- [ ] L-03 Workspace, `CODEOWNERS`, bloques compartidos y puertos
- [ ] L-04 Azure DevOps: Épica, Features y HUs
- [ ] L-05 Línea base de pruebas y plan para las 86 que fallan
- [ ] L-06 Aprobar los ADR 0061–0065
- [ ] L-07 Redis y RabbitMQ
- [ ] L-08 k3s DEV con Argo CD, ingress y cert-manager
- [ ] L-09 DNS, certificados y enrutamiento del hub
- [ ] L-10 CD por aplicación
- [ ] L-11 Contraseña SMTP fuera del repositorio
- [ ] L-12 Ambientes QA y PDN: `Development`, banderas y promoción
- [ ] L-13 Rituales y tablero de dependencias
- [ ] L-14 Verificar la puerta de salida

---

## Tareas

### L-01 · Fusionar `feature/nueva-suite-flit` · día 1 · S

- **Qué:** abrir el PR a `develop` con el plan, las reglas, el contrato, los planes por frente, las skills y el arreglo de PDN `99bd3669` (traído de `5ae9578f`).
- **Por qué primero:** el arreglo debe estar en `develop` antes de la próxima promoción a `release`. Además, todos parten de ahí (R1).
- **Hecho cuando:** está fusionado con merge commit. Si la revisión del plan se demora, se separa el arreglo en su propio PR con `git cherry-pick 99bd3669`.

### L-02 · Arranque y contrato v1 · semana 1 · S

- **Qué:** reunión de 90 minutos con los tres frentes: recorrer el plan maestro y las reglas, cerrar el contrato v1 (tareas A-00, B-00 y C-00), confirmar la asignación de tareas y el orden de migraciones de la Fase 1.
- **Hecho cuando:** el contrato v1 está fusionado con la aprobación de los tres y cada frente tiene su primera HU lista en ADO.

### L-03 · Workspace, `CODEOWNERS`, bloques y puertos · semana 1 · S

- **Qué:**
  - `pnpm-workspace.yaml`: agregar `packages/*` y `frontend-*`.
  - `CODEOWNERS` según la tabla R4, con los usuarios de GitHub de cada frente.
  - Un PR mínimo que agrega los bloques vacíos `// === FLIT Suite ===` en `Flit.Api/Program.cs` e `InfrastructureExtensions.cs`, para que cada frente agregue una línea sin chocar (R5).
  - Puertos locales y de ambiente para el hub, `demo`, Comparendos y Diagnóstico en `docs/despliegue-y-puertos.md`.
- **Hecho cuando:** los tres frentes pueden crear sus paquetes y apps sin tocar esos archivos.

### L-04 · Azure DevOps · semana 1 · M

- **Qué:** Épica "FLIT Suite — Plataforma" con Features A1, A2, B1, B2, C1, C2 y L, según cada plan de frente. HUs del siguiente sprint con criterios Gherkin, puntos Fibonacci y tag `DOR`, divididas por capa cuando aplique.
- **Regla:** máximo 8 HUs por Feature; HUs en el siguiente sprint, nunca en el activo.
- **Hecho cuando:** cada frente tiene al menos las HUs de su Fase 0 en estado `New` con `DOR`.

### L-05 · Línea base de pruebas · semana 1 · S

- **Qué:** publicar `docs/suite/linea-base-pruebas.md` con las 86 pruebas del frontend que fallan en `develop@e8b7ca65`. Se obtiene con `cd frontend && npx vitest run --reporter=json`. Repartir su corrección por área o dedicar una HU; ya existe una tarea sugerida para eso.
- **Hecho cuando:** CI o la revisión de PR compara contra la línea base (R8) y el número solo baja.

### L-06 · Aprobar los ADR · semanas 1–2 · S

- **Qué:** revisar los borradores de `docs/suite/adr-borradores/`. Los aprobados se mueven a `docs/decisions/` en estado `Propuesto` y se aceptan en un PR aparte (regla FLIT 15).
- **Hecho cuando:** ADR-0061 a ADR-0065 están en `docs/decisions/`.

### L-07 · Redis y RabbitMQ · Fase 0 · M

- **Qué:** Redis para sesiones, refresh tokens y cachés, y RabbitMQ con usuario por servicio, colas de reintento y de mensajes muertos, y alertas de profundidad de cola. En DEV primero; en k3s si ya está listo o en el VPS mientras tanto.
- **Hecho cuando:** A, B y C tienen cadenas de conexión de DEV en la configuración del ambiente.

### L-08 · k3s DEV · Fase 0–1 · L

- **Qué:** namespace por ambiente, ingress con `Host` preservado, cert-manager con un certificado por host, Argo CD con aplicaciones por servicio en `flit-gitops`, y secretos por servicio. Usuario de PostgreSQL por servicio con permisos solo sobre su schema.
- **Decisión que tomar aquí:** si DEV pasa completo a k3s antes de la Fase 2 o si conviven el VPS con compose y k3s. Con convivencia, el borde enruta por host hacia uno u otro.
- **Hecho cuando:** C puede desplegar `demo` en `dev.demo.flitsas.online` con Argo CD.

### L-09 · DNS, certificados y enrutamiento del hub · Fase 1 · M

- **Qué:** los hosts DEV del contrato §1. En el host del hub, `/connect/*`, `/.well-known/*` y `/api/*` van a `core-api` o al gateway, y el resto a `frontend-hub`. `NEXT_PUBLIC_FLIT_HOSTS` ya incluye la raíz: lo trajo el arreglo `99bd3669`.
- **Hecho cuando:** los hosts de DEV responden con certificado válido y la raíz de DEV sirve el hub cuando B enciende `Suite:Hub:Enabled`.

### L-10 · CD por aplicación · Fase 1 · M

- **Qué:** `cd.yml` construye y despliega `frontend-hub`, `core-demo` y `frontend-demo` como imágenes propias, con filtros de ruta. CORS y dominios separados para el hub y Trámites. Configuración del frontend en runtime.
- **Con:** C, para el workflow de la plantilla.
- **Hecho cuando:** un cambio solo en `frontend-hub` no reconstruye Trámites.

### L-11 · Contraseña SMTP · Fase 0 · S

- **Qué:** rotar la contraseña de `docker-compose.yml:36` y reemplazarla por una variable de entorno. Reescribir el historial es opcional y afecta a todos: si se hace, que sea en un punto acordado con todas las ramas fusionadas.
- **Hecho cuando:** la contraseña vieja ya no sirve y no aparece en `HEAD`.

### L-12 · QA y PDN · Fases 1–2 · M

- **Qué:** aprobar y ejecutar la salida de `Development` en QA y luego en PDN después de un sprint estable en DEV (A-02). Aprobar el encendido de banderas por ambiente (R7). Mantener `develop` → `staging` → `release` con el arreglo `99bd3669` presente.
- **Hecho cuando:** ningún ambiente desplegado corre con `Development`.

### L-13 · Rituales y tablero · continuo · S

- **Qué:** moderar el mensaje diario (R10), coordinar el turno de migraciones (R6) y actualizar cada semana el tablero de dependencias de `docs/suite/README.md`. Revisión semanal en DEV.
- **Pendiente con el PO:** congelar el roadmap de Trámites, salvo errores de PDN, durante las Fases 1 y 2.

### L-14 · Puerta de salida · fin de la Fase 2 · S

- **Qué:** verificar en QA cada punto del plan maestro §5.4 y registrar la evidencia.
- **Hecho cuando:** todas las casillas están marcadas. Desde ahí, B y C arrancan Comparendos y Diagnóstico con la plantilla, y A vuelve al roadmap de Trámites.

## Bitácora

| Fecha | Tarea | PR | Nota |
|---|---|---|---|
| | | | |
