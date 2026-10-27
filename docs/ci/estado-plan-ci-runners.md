# Plan CI/CD de flitsas/flit: estado para retomar

> Última actualización: **2026-10-24 (UTC)**. Documento vivo: actualizarlo al cerrar cada sesión de trabajo.
> Plan original y revisión: `Downloads\plan-ci-runners-flit\` del equipo de Jorman (`Plan-CI-runners-propios-flit.md`, `Revision-plan-2026-10-23.md`, anexos A–E).
> Operación de las VPS: repo `flitsas/flit-gitops`, `docs/manual-operacion-vps.md` §8 y `docs/argocd-notificaciones/`.

## 1. Resumen

| Fase | Estado | Resultado |
|---|---|---|
| 0. Gestión de código | ✅ Hecha | `.github/` idéntico en develop, staging y release. Ruleset `proteger-produccion` (id 23519351) en las 3 ramas + rama por defecto |
| 1. Carriles en GitHub | ✅ Hecha (#692) | core-api en 6 jobs paralelos: `admin`, `integration-0..3`, `resto` (+ `aot-publish`) |
| 1b. Arnés de Admin.Tests | ✅ Hecha (#702 → develop) | Carril `admin` 31,4 → **10,4 min**, mismas 4.827 pruebas |
| 2. Checks obligatorios | ✅ Hecha (#693–#696) | `core-api` y `Lint, type-check, build` obligatorios en develop/staging/release; `core-api.yml` sin `paths:` (job `cambios`) |
| 3. El CD espera a Argo CD | ✅ Hecha y verificada | `deploy-gitops` espera el commit status `argocd/flit2-<amb>` en lugar de `sleep 180` (#699–#701). Verificado en el CD real de develop: «Argo CD: flit2-dev Synced + Healthy (1 min)» |
| 4. Pool de runners propios (177) | ⏳ **Pendiente** | Depende de la ampliación de la VPS 177. Correcciones listas en la rama `ci/core-api-pool` (sin fusionar) |
| 5. Dockerfile de core-api | ✅ Hecha (#703 → develop; cd.yml nivelado #704/#705) | Build de la imagen 5,5 → **2,7 min** |

Tiempo del check `core-api` en un PR: **41–44 min → ~11 min** (camino crítico: Integration).

## 2. Detalle de lo hecho

### Fase 0–2 (CI)
- `.github/workflows/core-api.yml` y `.github/scripts/ci-core-api-pruebas.sh` (reparto por clase de Integration; `LoginTimingTests` corre sola y primero).
- Ruleset 23519351: deletion, non_fast_forward, pull_request (0 aprobaciones), required_status_checks (`core-api`, `Lint, type-check, build`, integration_id 15368).
- Snapshots de DI de staging/release corregidos (#694/#695).

### Fase 1b (Admin.Tests, #702)
- Fixture `HostPorClase` (`services/core-api/tests/Flit.Admin.Tests/HostPorClase.cs`): un host por clase en lugar de uno por prueba (que además nunca se liberaba).
- Clases: OidcProductToken, OidcServer, ActivarIdentidadManual, RegenerarEnlaceManual, MandateSignerIdentityResend, MensajesMuertos, JwtIssuedTokenValidation (`[Trait("lenta","si")]`), PermissionEnforcementState (3 hosts en vez de 21).
- No se hizo (a propósito): host único por ensamblado (cruzaría rate limiters/cachés) ni apagar hosted services en una fábrica base. El snapshot de DI sigue con fábrica completa.

### Fase 3 (Argo CD → commit statuses)
- GitHub App `argocd-flit` (app 4489091, instalación 151296511) con **Commit statuses: Read and write**.
- Argo CD de la **177** (DEV/QA) y de la **169** (PDN): HelmChart en `/var/lib/rancher/k3s/server/manifests/argocd.yaml` (fuera de git; copias en flit-gitops `docs/argocd-notificaciones/helmchart-argocd-{177,169}.yaml`) con notificaciones `github` y sondeo de 60 s. En la 169 se fijó `version: 10.3.2`.
- Suscripciones en `clusters/vps-177/flit2-{dev,qa}.yaml` y `clusters/vps-169/flit2-pdn.yaml` (flit-gitops #72, #73).
- Secreto de org `GITOPS_TOKEN` (PAT fine-grained) con **Commit statuses: Read-only** además de Contents RW.
- Variable de repo `ARGO_STATUS_PDN=true` (PDN también espera a Argo).
- Si el status no llega en 10 min o no se puede leer, el CD avisa y sigue con la comprobación de salud: nunca frena un despliegue.
- Trampa: un envío fallido queda marcado como enviado → `kubectl -n argocd annotate app <app> notified.notifications.argoproj.io-`.

### Fase 5 (Dockerfile core-api, #703)
- Un solo `RUN` con los 4 `dotnet publish` y caché NuGet de BuildKit; salida `/app/...` idéntica (542 archivos).
- `cd.yml`: sin `cache-from/to type=gha` en core-api (exportarla costaba ~2 min y nunca se aprovechaba). Nivelado a staging/release (#704/#705).
- El Dockerfile llega a staging/release por la **nivelación normal**.
- El build de .NET ya no era determinista (dos builds viejos difieren en dll/pdb); la equivalencia se valida por lista de archivos.

### Otros cambios del día
- `ict.flitsas.com` como segunda URL de integración ICT en PDN (flit-gitops #62).
- VPS 177: limpieza de disco **79 % → 32 %** (imágenes viejas de k3s + reversa docker de DEV/QA); kubelet `image-gc-high-threshold=70` / `low=50` (flit-gitops #75).
- Capacidad de DEV/QA revisada: ya está al mínimo (1 réplica, requests bajos, sin límite de CPU). El tiempo de despliegue se va en compilar y bajar imágenes, no en el rollout.

## 3. Fase 4: pool de runners propios en la 177 (PENDIENTE)

**Estado de la infraestructura**
- Runners `vps-177-r3..r8` (usuario `runner`, docker rootless, etiquetas `flit-ci,vps-177`) en el grupo de org **`flit-ci` (id 3)**, limitado al repo flit.
- Slice `user-111` provisional: `CPUQuota=600%`, `CPUWeight=20`, `MemoryHigh=12G`, `MemoryMax=14G` (`/etc/systemd/system/user-111.slice.d/`). Respaldo del original (4c/12G) en `/root/50-runners.conf.antes-flit-ci.*`.
- r1/r2 (grupo Default) atienden otros repos y no se tocan.

**Rama `ci/core-api-pool`** (rebasada sobre develop el 2026-10-24, sin PR):
- `workflow_dispatch` con input `runner`; `runs-on: ${{ inputs.runner || vars.RUNNER || 'ubuntu-latest' }}`.
- Postgres/RabbitMQ por `.github/scripts/ci-servicios.sh` (puerto dinámico, reintento, etiquetas `flit-ci.*`).
- `.github/scripts/ci-limpiar-runner.sh`: limpia procesos/contenedores huérfanos del propio runner al inicio y al final (`if: always()`).
- En self-hosted: `UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`, .NET en el tool cache del runner.
- dotnet-ef: `--global` en GitHub, `--tool-path` en self-hosted, un reintento si muere por señal.
- `PlatformProductsTests`: AC2 vuelve a retirar `demo` (dependencia de orden).

**Mediciones**
| Fecha | Corrida | Configuración | Resultado |
|---|---|---|---|
| 2026-10-24 | 40057307482 | 6 runners, 7c/18G, código viejo | admin 35 min; carga 158; RAM 25 GB → detenido |
| 2026-10-24 | 40061239419 | 4 runners, 6c/14G, código viejo | cortado a 65 min; carga 75–265; DEV/QA 0 reinicios; huérfanos |
| 2026-10-24 19:30 UTC | 40125613423 | 4 runners, 6c/14G, **con Fase 1b + correcciones** | integration-0/2/3 caídos a los 21 min con «The self-hosted runner lost communication with the server» (runner sin CPU/RAM); carga máx. **336**, RAM 21 GB; DEV/QA **0 reinicios**; cancelado. Sin huérfanos tras el apagado (la limpieza nueva funciona) |

**Conclusión:** con 8 vCPU / 31 GB la 177 **no puede** con 4 jobs de core-api a la vez, ni siquiera con la Fase 1b. Sin ampliar, la única opción sería 1–2 runners (más lento que GitHub, ~11 min). No vale la pena: la Fase 4 queda esperando la ampliación.

**Pasos para terminar la Fase 4**
1. (Usuario) Ampliar la 177. Referencia: 16–24 vCPU y 64 GB.
2. Subir topes del slice y encender r7/r8 (`systemctl --user enable --now actions-runner@rN` como `runner`).
3. Medir: `gh workflow run core-api.yml --ref ci/core-api-pool -f runner=flit-ci`, con monitor de carga y reinicios de DEV/QA (`/root/monitor-pool.sh` en la 177).
4. Si el tiempo es comparable a GitHub (~11 min) sin afectar DEV/QA:
   - pasar el repo flit a **privado** (confirmar el momento con el usuario; antes rotar SMTP);
   - PR de `ci/core-api-pool` a develop/staging/release; variable `RUNNER=flit-ci`;
   - adaptar `cd.yml` (driver docker de buildx, sin caché gha en el pool).
5. Mientras el repo sea público: **grupo `flit-ci` con `allows_public_repositories=false`** y r3–r8 apagados.

## 4. Pendientes fuera de las fases
- Rotar la clave SMTP expuesta (el usuario dijo «más adelante»; hacerlo antes de pasar a privado).
- Revocar el PAT `ghp_` en claro en `scan-piloto.sh` (177).
- Frontend: `output: "standalone"` de Next + etapa de runtime mínima (787 MB → ~200 MB; aplica a frontend, frontend-hub y frontend-comparendos).
- Revisar la caché gha de los otros 9 builds de `cd.yml`.
- Prueba intermitente: `OidcClientSyncConcurrenciaTests.SincronizacionesSimultaneas_DelMismoCliente_NoFallanYLoDejanCreado` (core-identity).
- 177, sin confirmar por el usuario: volúmenes docker de la reversa (~5 GB) y bases `flitpdn` / `flit1pdn_old_20260929` (~4,4 GB).
- Equipo local: borrar el contenedor `flit-admin-pg` y las imágenes `eq-core-api:*` cuando Docker Desktop esté encendido; worktrees `D:/FLIT/Code/flit-wt-{argo,pool,admin,docker}`.

## 5. Estado al cierre de la sesión
- Medición 40125613423: terminada (cancelada), resultado en §3.
- r3–r8: **apagados y deshabilitados**. r1/r2 activos (otros repos).
- Grupo `flit-ci`: `allows_public_repositories=false`.
- 177 sin procesos ni contenedores del pool; DEV/QA responden 200; 0 reinicios de pods.
- Monitor de la medición: `/root/monitor-pool.sh` (log `/root/monitor-pool-*.log`); apagado con `/root/apagar-pool.sh`. Ojo: `pkill -f monitor-pool` mata también la sesión SSH que lo invoca (su línea de comandos contiene el texto).
- **Para retomar mañana:** nada de la Fase 4 hasta que la 177 se amplíe. Lo siguiente con valor sin depender de eso: imágenes del frontend en modo standalone (§4).
