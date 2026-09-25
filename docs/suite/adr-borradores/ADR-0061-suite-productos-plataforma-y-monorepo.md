# ADR-0061: Suite de productos — plataforma compartida, productos como servicios propios y monorepo

**Fecha**: 2026-09-23  
**Status**: BORRADOR (pasa a Propuesto al versionarse en `docs/decisions/`)  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), PO, arquitectura  
**Tags**: arquitectura, suite, subdominios, monorepo, k3s, core-api, frontend  
**Amends**: ADR-0014 (dueño único de migraciones), solo para los schemas de productos nuevos  
**Base de código**: `origin/develop@e8b7ca65`

## Contexto

FLIT es hoy un producto (trámites vehiculares) servido por un SPA Next.js y el monolito modular `core-api`. El negocio decidió convertirlo en una suite al estilo Google Workspace:

- **Hub y login en la raíz `flitsas.online`** (`dev.` y `qa.flitsas.online` en esos ambientes): ahí se inicia sesión, se ven los productos con acceso y se cambia de producto con un menú de productos común. Cada producto tiene su propio menú. Productos en `<ambiente>.<producto>.flitsas.online`. En esta etapa todo vive en `flitsas.online`; `flitsas.com` sigue siendo el sitio corporativo.
- Trámites se publica hoy en `dev.flitsas.online`, `qa.flitsas.online` y, en PDN, en la raíz `flitsas.online` con API en `api.flitsas.online` (commit `5ae9578f`, hoy solo en `release`).
- Comparendos, Diagnóstico y luego Flotas son productos completos y nuevos. Un cliente contrata cualquier subconjunto; lo activa el SuperAdmin.
- **Primero se construye la plataforma**; después arrancan Comparendos y Diagnóstico. El equipo son tres desarrolladores (Trámites, Comparendos, Diagnóstico) que construyen juntos la plataforma.
- PDN pasa a k3s + Argo CD. Hay presupuesto para Redis y RabbitMQ.

Hechos del código:

- `core-api` registra módulos a mano (`Program.cs` 419 líneas, `InfrastructureExtensions.cs` 1331), un `FlitDbContext` con 128 `DbSet` y 256 migraciones.
- `core-ict` es un servicio separado con solución, imagen, schema `ict` y token de servicio propios.
- ADR-0060 (Marca Blanca) ya resuelve el host de forma confiable (sello `X-Flit-Domain` en el gateway, `DomainContext` en la API) y trata `*.flitsas.online` como hosts FLIT.
- El frontend conmuta 14 módulos por `?m=` y `Shell.tsx` importa componentes de productos concretos. `pnpm-workspace.yaml` solo lista `frontend`.

## Decisión

1. Separar **plataforma** (identidad, empresas y jerarquía, productos y su habilitación por empresa, RBAC por producto, dominios y marca, consultas externas compartidas, reportes consolidados) de **productos**.
2. La plataforma y Trámites **se quedan en `core-api`**. La plataforma se expone como `/api/v1/platform/**` con contrato estable; su UI vive en un `frontend-hub` nuevo.
3. Cada producto nuevo es un **servicio propio** creado desde `templates/flit-product`: solución .NET en `services/core-<producto>`, app Next.js en `frontend-<producto>`, schema y usuario de BD propios, migraciones propias, imagen y aplicación Argo CD propias. Se usa el **nombre completo** del producto en todas partes: `core-comparendos`, `core-diagnostico`, `frontend-comparendos`, `frontend-diagnostico`, schema, audiencia del token y host.
4. **Monorepo.** Librerías compartidas en `services/shared/` (.NET) y `packages/` (frontend), por referencia de proyecto y workspace pnpm. `CODEOWNERS` por carpeta y CI filtrado por ruta.
5. Cada app Next.js actúa como BFF: el navegador llama a `/api` en su mismo origen.
6. Hosts por producto con certificado por host (acme.sh en el VPS hoy, cert-manager en k3s).
7. **Trámites pasa a `<ambiente>.tramites.flitsas.online`**. La raíz de cada ambiente (`flitsas.online`, `qa.flitsas.online`, `dev.flitsas.online`) pasa a ser el hub y redirige las rutas propias de Trámites conservando ruta y parámetros. Los recursos de correo pasan a `assets.flitsas.online`.
8. **Navegación en dos niveles**: `packages/shell` dibuja la barra común con el menú de productos y el menú de cuenta; cada producto aporta su propio catálogo de navegación. Ninguna cookie se emite con `Domain=flitsas.online`.

## Alternativas consideradas

### Opción 1: Productos como módulos dentro de `core-api`

**Pros:**
- Un solo backend y un solo despliegue.
- Reutiliza DI, `FlitDbContext`, integraciones y middlewares.

**Cons:**
- Dos desarrolladores nuevos compitiendo en el mismo snapshot de migraciones y el mismo archivo de DI.
- Un cambio en Comparendos redespliega Trámites.
- Heredan el aislamiento por lista de rutas y el RLS sin `FORCE`.
- Contradice "aplicaciones independientes que se adquieren por separado".

**Esfuerzo:** M  
**Riesgos:** Acoplamiento creciente y conflictos constantes en `FlitDbContextModelSnapshot.cs`.

### Opción 2: Productos como servicios propios en el monorepo; plataforma en `core-api` — elegida

**Pros:**
- Sigue el precedente probado de `core-ict`.
- Cada dev es dueño de su servicio, schema y despliegue.
- Contratos compartidos y consumidores cambian en el mismo PR.
- La plataforma no se reescribe; se construye sobre lo que ya existe, incluida Marca Blanca.

**Cons:**
- Más imágenes, pipelines y aplicaciones Argo CD.
- Exige plantilla y SDK antes de que arranquen los productos (resuelto al hacer la plataforma primero).

**Esfuerzo:** L  
**Riesgos:** CI lento si no se filtra por ruta.

### Opción 3: Un repositorio por producto con SDK publicado

**Pros:**
- Independencia total de ciclos y permisos.
- Escala mejor con equipos por producto.

**Cons:**
- Cada cambio del SDK exige publicar versión y actualizar varios repos.
- Duplica reglas `.cursor`, CI y convenciones.
- Con un dev por producto, la coordinación cuesta más que el beneficio.

**Esfuerzo:** L  
**Riesgos:** Versiones del SDK divergentes.

## Tradeoff aceptado

Se elige la **Opción 2**: más piezas que operar a cambio de independencia real de despliegue y datos, apoyada en k3s + Argo CD. Se reconsidera la Opción 3 cuando un producto tenga equipo propio de varias personas, ciclo de liberación o cumplimiento distintos, o cuando el CI del monorepo sea lento pese a los filtros.

## Consecuencias

### Lo que se gana

- Productos independientes en host, código, datos y despliegue.
- Camino repetible para Flotas y futuros productos.
- Trámites entra a la suite sin reescribirse.

### Lo que se pierde

- La simplicidad de un solo backend.
- ADR-0014 deja de ser absoluto para schemas de productos nuevos.

### Cambios operacionales

- `pnpm-workspace.yaml`: `frontend`, `frontend-*`, `packages/*`.
- Workflows por servicio y app con filtros de ruta; aplicaciones Argo CD por servicio y ambiente en `flit-gitops`.
- Configuración del frontend en runtime por host, extendiendo `lib/api/base-url.ts`.
- `flitsas.online`, `qa.flitsas.online` y `dev.flitsas.online` pasan a servir el hub; sus rutas de Trámites redirigen a `<ambiente>.tramites.flitsas.online`. Se sigue sirviendo `/email-assets` para los correos ya enviados.
- `NEXT_PUBLIC_FLIT_HOSTS` incluye la raíz exacta `flitsas.online`.
- Los valores por defecto `https://dev.flitsas.online/...` escritos en código salen a configuración por ambiente.

## ADRs relacionados

- `ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo` — sello de host y `DomainContext`; esta suite los extiende.
- ADR-0014 (sin archivo en repo) — se ajusta para schemas de productos nuevos.
- ADR-0017 — gateway YARP.
- ADR-0062 a ADR-0065 (borradores de esta suite).

## Notas para agentes

- **Backend Agent**: producto nuevo = plantilla; nunca agregar endpoints de productos nuevos a `Flit.Api`. Filtro global de tenant desde el DbContext base del SDK.
- **Frontend Agent**: producto nuevo = `frontend-<producto>` con `packages/{ui,shell,auth,config}`. No copiar de `frontend/components/atom`; extraer a `packages/ui`.
- **QA Agent**: pruebas de integración contra Postgres real desde el primer sprint de cada producto.
- **Security Agent**: usuario de BD por servicio sin acceso a otros schemas.
- **Infra Agent**: namespace por ambiente; certificado por host; Redis y RabbitMQ en k3s.
