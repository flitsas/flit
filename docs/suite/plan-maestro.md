# FLIT Suite — Diagnóstico y plan de producto central (v3.2)

> v3.2 · 2026-09-23 · **propuesta, nada implementado**
> · v3.2: **el hub y el login viven en la raíz `flitsas.online`** (decisión del negocio); modelo de navegación con menú de productos común y menú propio por producto (§4.6).
> · v3.1: equipo de tres personas con reparto de la plataforma (§5.0), nombres completos de
> carpeta, Marca Blanca (ADR-0060) como base ya implementada en `develop`, y decisión sobre el
> host de Trámites (§4.3).
> · **Base verificada: `origin/develop@e8b7ca65`.** Las versiones 1 y 2 se hicieron sobre una copia
> local de `develop` que estaba 293 commits atrás y no incluía la épica de Marca Blanca. Esta versión
> vuelve a verificar todo contra el código vigente.
> · Cambios frente a v2: plataforma primero y productos después;
> todos los hosts sobre `flitsas.online` en esta etapa; la identidad **extiende** lo construido en
> ADR-0060 (Marca Blanca) en vez de reemplazarlo; consultas externas como capacidad compartida;
> recomendación de repositorio con la convención de carpetas propuesta por el equipo.
> · **Trabajo diario:** [README de la suite](README.md) (quién hace qué y cómo empezar),
> [reglas de trabajo en paralelo](reglas-trabajo-paralelo.md), [contrato de plataforma v1](contrato-plataforma-v1.md)
> y los planes por frente en [`frentes/`](frentes/).
> · Borradores de ADR 0061–0065 en [`docs/suite/adr-borradores/`](adr-borradores/README.md),
> pendientes de aprobación humana (regla FLIT 15).

---

## 0. Decisiones de negocio confirmadas

| Tema | Decisión | Efecto en el diseño |
|---|---|---|
| Orden | **Primero la plataforma**; cuando esté lista, arrancan los productos | Fases secuenciales con una puerta de salida explícita (§5.4) |
| Productos | Flotas, Diagnóstico y Comparendos son productos completos y nuevos | Servicios propios; no se reutiliza trámites como base funcional |
| Equipo | Tres personas: una para Trámites, una para Diagnóstico, una para Comparendos | Las tres construyen la plataforma antes de sus productos (§5.0) |
| Primeros productos | Comparendos y Diagnóstico, un desarrollador cada uno | Plantilla y SDK de producto son entregables de la plataforma |
| Adquisición | Activación por SuperAdmin | Suscripción auditada; sin facturación en línea |
| Independencia | Un cliente puede tener Flotas sin Trámites | Trámites pasa a ser un producto más |
| Cuenta | Una cuenta entra a los productos habilitados a su empresa | Identidad única + suscripción por empresa + rol por producto |
| Roles | Por producto | `product_code` en módulos y roles |
| Login | Interno; abierto a MFA, SAML y proveedores externos | Servidor OIDC propio |
| Marca blanca | Implementada en `develop` (épica #12237, ADR-0060); **es la base de partida** | La suite se construye **sobre** su sello de host, su `DomainContext` y su verificación de dominios |
| Dominios | Esta etapa: **todo en `flitsas.online`**. `flitsas.com` sigue siendo el sitio corporativo | Tabla de hosts §4.2 |
| Hub | **En la raíz `flitsas.online`**: ahí se inicia sesión, se ven los productos con acceso y se cambia de producto | Hub y login comparten host; `dev.` y `qa.flitsas.online` pasan a ser el hub de esos ambientes (§4.2, §4.3) |
| Convención de ambiente | `dev.tramites.flitsas.online` | `<ambiente>.<producto>.flitsas.online`, sin prefijo en PDN |
| Integraciones | Algunas fuentes externas se comparten entre productos | Consultas externas como capacidad de plataforma (§4.7) |
| Fechas | No hay fecha objetivo | El plan usa duraciones relativas |
| Infra | k3s + Argo CD en PDN en los próximos días; hay presupuesto para Redis y broker | Productos nuevos nacen en k3s |
| Datos | Vehículo y persona separados por producto; aislamiento por schema; reportes por producto y algunos consolidados | §4.8 y §4.9 |
| Repositorio | Monorepo con nombres completos | `services/core-comparendos`, `services/core-diagnostico`, `frontend-comparendos`, `frontend-diagnostico` (§3) |
| Host de Trámites | Hoy en `dev.flitsas.online`, `qa.flitsas.online` y, en PDN, la raíz `flitsas.online` | Pasa a `<ambiente>.tramites.flitsas.online`; la raíz de cada ambiente pasa a ser el hub y reparte las rutas viejas (§4.3) |

---

## 1. Diagnóstico sobre `origin/develop@e8b7ca65`

### 1.1 Lo que ya sirve para la suite

- **Resolución de host confiable (Marca Blanca, ADR-0060).** El gateway borra cualquier `X-Flit-Domain` entrante y lo sella con el `Host` real (`Flit.Gateway/Transforms/DomainSealTransform.cs`). La API lo convierte en un único `DomainContext` (`Flit` o `Network(cabeza)`) en `Flit.Api/Middleware/DomainContextMiddleware.cs`. Es el punto natural para añadir "producto".
- **Login acotado por host** con anti-enumeración y tiempo constante (`LoginHandler`), claim `dom` en el token y `DomainBindingMiddleware` que rechaza un token usado bajo otro dominio.
- **Dominios de cliente con ciclo completo**: `admin.tenant_domains` con verificación TXT, estados `pending → verified → active`, job de revalidación (ADR-0059) y señal de certificado emitido. Pipeline ACME por host diseñado en `deploy/edge/` (plantillas y runbook; su instalación en el VPS figura pendiente en el runbook).
- **Marca antes del login** resuelta en servidor (`app/layout.tsx`, `lib/brand/*`) y **base de la API decidida por host en runtime** (`lib/api/base-url.ts`). CORS dinámico en el gateway.
- **Enlaces de correo por red** (`NetworkUrlBaseResolver`) y tema de correo por red.
- **Registro de consultas externas** con cadenas de respaldo y overrides por empresa (`IConsultationProvider`, `ConsultationProviderChainResolver`). Ya se expone sin trámite de por medio vía gRPC a core-ict (`IctConsultationService`) y vía adaptadores de generación documental.
- **Precedente de servicio separado**: `core-ict` (solución, imagen, schema y token de servicio propios).
- Monolito modular con schemas por módulo; YARP ya enruta por host (`ict-host-route`).

### 1.2 Lo que falta o hay que corregir

| Hallazgo | Evidencia | Por qué importa |
|---|---|---|
| **Los tres ambientes, PDN incluido, corren con `ASPNETCORE_ENVIRONMENT=Development`**; por eso el gateway activa `DevelopmentNoJwtProxyConfigFilter` y su policy `JwtRequired` es `RequireAssertion(_ => true)` | `docker-compose.prod.yml:130,187,329,467`; `Flit.Gateway/Program.cs:88,125-130` | Ningún ambiente valida el token en el gateway. ADR-0060 reconoce que la ligadura `dom` es solo una regla de producto mientras esto siga así |
| La API acepta tokens sin firma si no hay llave configurada | `Flit.Api/Authorization/ApiSecurityExtensions.cs` | Cualquiera que llegue a la API puede fabricar un token |
| JWT de 12 h, `aud=flit-api`, sin refresh token, todos los permisos dentro | `RsaJwtTokenIssuer.cs` | No distingue productos; quitar un producto o rol exige re-login |
| Cookie `flit_token` sin `HttpOnly`, `Secure` ni `Domain`, más copia en `localStorage` | `frontend/lib/auth/session.ts` | Token legible por JavaScript; sin sesión entre hosts |
| **No existe producto ni suscripción.** Los booleans `tramites/comparendos/resoluciones_module_enabled` **no se aplican en ningún endpoint** | `TenantSettings.cs:135-149`; solo los lee `DashboardActiveModulesEndpoints.cs` | Hoy "habilitar un módulo" solo cambia una tarjeta del dashboard |
| RBAC sin dimensión de producto; 1 correo = 1 empresa | `RbacConfigurations.cs`; ADR-0060 D3 | Hace falta `product_code` en módulos y roles |
| `tenant_domains` admite **un solo dominio por red** (`uq_tenant_domains_tenant_id`) y `DomainContext` no conoce productos | DDL 116; `DomainContext.cs` | Una red con varios productos necesita un host por producto |
| Frontend: SPA por `?m=` (14 módulos), `Shell.tsx` de 872 líneas importa componentes de productos, sin `packages/` | `app/page.tsx`, `pnpm-workspace.yaml` | No se puede servir un producto por host sin separar |
| `NEXT_PUBLIC_API_BASE_URL` y `NEXT_PUBLIC_FLIT_HOSTS` horneados en build | `frontend/Dockerfile:25-34`, `cd.yml:241` | Una imagen por ambiente, no por host |
| Registro manual de módulos: `Program.cs` 419 líneas, `InfrastructureExtensions.cs` 1331, un `FlitDbContext` con 128 `DbSet` y 256 migraciones | — | Productos nuevos dentro de core-api chocarían entre sí |
| Aislamiento de tenant por lista de rutas; RLS sin `FORCE`; la app conecta como dueña | `TenantEnforcementMiddleware.cs:153-257` | Un producto nuevo no hereda aislamiento |
| Contratos de consultas viven en `Flit.Tramites.*`; sin medición de consumo por empresa | `IConsultationProvider.cs` | Para compartirlas hay que moverlas a plataforma y medirlas |
| OpenAPI a mano (15.9k líneas), `codegen` muerto | `contracts/openapi/core-api.v1.yaml` | Varios frontends con clientes a mano no escala |
| **Arreglo de PDN solo en `release`**: el commit `5ae9578f` hace que el frontend reconozca la raíz `flitsas.online` como host FLIT y usa `api.flitsas.online`. `develop` y `staging` no lo tienen: siguen con `pdn.flitsas.online`, `api.pdn.flitsas.online` y una lista de hosts sin la raíz | `git show 5ae9578f`; `cd.yml` y `lib/brand/hosts.ts` en cada rama | La próxima promoción de `develop` a `release` puede traer conflicto en esas líneas. Si se resuelve con la versión de `develop`, el login de PDN vuelve a fallar como el 18 de septiembre |
| **86 de 4 794 pruebas del frontend fallan en `develop@e8b7ca65`**, también ejecutadas de forma aislada (asistente de trámites, tablas de administración, biometría, modal de nuevo trámite) | `vitest run` en `frontend/` | Las pruebas son la red de seguridad para mover el Shell, la sesión y la navegación. Con fallos previos no se distingue una regresión nueva |
| Contraseña SMTP literal en `docker-compose.yml:36` | — | Rotar y sacar del historial |
| Sin manifiestos k3s, sin Redis, sin RabbitMQ; nginx del VPS fuera del repo | `deploy/edge/` solo trae plantillas | Hay que montarlos en la Fase 0 |

---

## 2. HU #10664: qué fue y por qué no se revierte

En julio (commit `8de54606`, Feature #10504) se eliminó la tabla que decía qué módulos tenía cada empresa. El motivo quedó en el commit: esa habilitación solo se aplicaba en el menú y la API dejaba operar el módulo igual. Desde entonces el acceso depende solo de los roles.

La suite no necesita revertirla. Necesita un concepto por encima: **producto**, que es lo que la empresa contrata. La lección de esa HU se vuelve regla: la suscripción se valida al emitir el token y en cada API, no solo en el menú (borrador ADR-0063).

---

## 3. Recomendación de repositorio

**Decisión: un solo repositorio, con la convención `services/core-*` y `frontend-*` y nombres completos de producto.**

Por qué monorepo en esta etapa:

- La plataforma y los productos van a cambiar juntos durante meses: SDK de autenticación, contratos de eventos, componentes de UI. En un monorepo un cambio de contrato y sus consumidores van en el mismo PR.
- Con un desarrollador por producto, publicar y versionar paquetes entre repos cuesta más de lo que aporta.
- Reglas `.cursor`, CI, convenciones de ADO y revisiones ya viven aquí; `core-ict` demuestra que un servicio independiente convive bien en este repo.
- La independencia que pide el negocio es de **despliegue y datos**, no de repositorio: cada servicio tiene su imagen, su aplicación en Argo CD y su schema.

Estructura propuesta:

```
services/
  core-api/            plataforma + trámites (existente)
  core-ict/            existente
  python-ml/           existente
  core-comparendos/    Comparendos (nuevo, desde plantilla)
  core-diagnostico/    Diagnóstico (nuevo, desde plantilla)
  shared/              librerías .NET compartidas: Flit.Platform.Sdk, Flit.Platform.Contracts
frontend/              Trámites (existente)
frontend-hub/          hub y login en flitsas.online (nuevo, primero en construirse)
frontend-comparendos/  Comparendos
frontend-diagnostico/  Diagnóstico
packages/              librerías frontend compartidas: ui, shell, auth, config
templates/flit-product/  plantilla de servicio + app
deploy/                edge (existente) y manifiestos o referencias a flit-gitops
```

Criterios de la estructura:

1. **Un `frontend-hub` propio.** La administración de plataforma (empresas, usuarios, roles, suscripciones, marca) sale de `frontend/admin` hacia el hub. Si se queda dentro de trámites, trámites seguiría siendo "la app" y los demás productos dependerían de él.
2. **Librerías compartidas fuera de los productos**: `services/shared/` y `packages/`. Ningún producto copia código de otro.
3. **Nombres completos**: el código del producto (`comparendos`, `diagnostico`, `tramites`) es el mismo en la carpeta, el host, el schema, la audiencia del token, los roles y la aplicación de Argo CD.

Complementos: `CODEOWNERS` por carpeta para que cada dev sea dueño de su producto, y CI filtrado por ruta para que un cambio en `core-comparendos` no compile todo.

**Cuándo separar repos:** cuando un producto tenga equipo propio de varias personas, un ciclo de liberación o requisitos de cumplimiento distintos, o cuando el CI del monorepo se vuelva lento pese a los filtros.

---

## 4. Arquitectura objetivo

### 4.1 Plataforma y productos

| Capa | Responsabilidad | Dónde vive |
|---|---|---|
| **Plataforma** | Identidad OIDC, empresas y jerarquía, productos y suscripciones, RBAC por producto, dominios y marca (lo de ADR-0060), consultas externas compartidas, reportes consolidados, auditoría | `core-api` (módulos Security y Admin existentes + módulos nuevos), expuesta como `/api/v1/platform/**`. UI en `frontend-hub` |
| **Trámites** | Lo que hace FLIT hoy | Se queda en `core-api` y `frontend/`; cambia a `tramites.flitsas.online` |
| **Comparendos, Diagnóstico, luego Flotas** | Dominio propio | `services/core-*` + `frontend-*`, schema y usuario de BD propios |

La plataforma no se separa de `core-api` en esta etapa: ahí ya viven identidad, jerarquía y Marca Blanca.

### 4.2 Dominios y hosts (esta etapa, todo en `flitsas.online`)

| Superficie | PDN | QA | DEV |
|---|---|---|---|
| Sitio corporativo | `flitsas.com` (sin cambios) | — | — |
| **Hub y login** | `flitsas.online` | `qa.flitsas.online` | `dev.flitsas.online` |
| Trámites | `tramites.flitsas.online` | `qa.tramites.flitsas.online` | `dev.tramites.flitsas.online` |
| Comparendos | `comparendos.flitsas.online` | `qa.comparendos.flitsas.online` | `dev.comparendos.flitsas.online` |
| Diagnóstico | `diagnostico.flitsas.online` | `qa.diagnostico.flitsas.online` | `dev.diagnostico.flitsas.online` |
| API para integradores | `api.flitsas.online` | `api.qa.flitsas.online` (existe) | `api.dev.flitsas.online` (existe) |
| Recursos de correo y logos públicos | `assets.flitsas.online` | `qa.assets.flitsas.online` | `dev.assets.flitsas.online` |
| Red de marca blanca | Dominio principal de la red = su hub y su login (p. ej. `cliente.com`); un host por producto contratado (`tramites.cliente.com`) | pruebas: `marcablancaqa.flitsas.online` | `marcablancadev.flitsas.online` |

- **Hub en la raíz, por decisión del negocio.** El patrón queda: raíz o `<ambiente>.flitsas.online` es el hub; `<ambiente>.<producto>.flitsas.online` es un producto. La plataforma deduce el producto del host.
- **Reglas técnicas que impone la raíz:**
  - `NEXT_PUBLIC_FLIT_HOSTS` debe incluir la raíz exacta `flitsas.online`. Hoy el patrón `*.flitsas.online` no la cubre en el frontend (`lib/brand/hosts.ts`) y la trataría como dominio de una red. El backend ya la considera reservada (`Domains:Reserved`).
  - **Ninguna cookie se emite con `Domain=flitsas.online`.** Se enviaría a todos los ambientes y productos, incluidos los servidores de DEV. Todas las cookies son del host exacto.
  - La raíz necesita su propio certificado.
- **Al pasar a `flitsas.com`**: la raíz la ocupa hoy el sitio corporativo. En ese momento habrá que mover el sitio corporativo (por ejemplo a `www.flitsas.com`) o publicar el hub en otro host. Como el host del hub se configura por ambiente y no se escribe en el código, el cambio será de configuración y redirecciones.
- Cada host `<ambiente>.<producto>` lleva su propio certificado, emitido automáticamente (acme.sh hoy, cert-manager en k3s).
- Cada app llama a su API en el mismo origen (`/api/...`) a través de su servidor Next (BFF), el mismo modelo que ADR-0060 adoptó para Marca Blanca.

### 4.3 Trámites sale de la raíz de cada ambiente

**Hecho verificado.** Trámites se publica hoy en `dev.flitsas.online`, `qa.flitsas.online` y, en PDN, en la raíz `flitsas.online`, con la API de PDN en `api.flitsas.online`. Esto lo fijó el commit `5ae9578f` (2026-09-18) **solo en la rama `release`**; `develop` todavía dice `pdn.flitsas.online` (ver §1.2). Con el hub en la raíz, cada uno de esos tres hosts pasa a ser el hub de su ambiente, y Trámites pasa a `<ambiente>.tramites.flitsas.online`.

**Reparto de las rutas actuales** (el hub las atiende con redirecciones que conservan ruta y parámetros):

| Rutas que hoy sirve Trámites | Destino |
|---|---|
| `/login`, `/auth/*`, `/reset-password`, `/invite/activate`, `/profile/*` | Se quedan en el hub: son funciones de plataforma. Los enlaces de invitación y recuperación ya enviados siguen funcionando sin redirección en DEV y QA |
| `/`, `/?m=dashboard` | Inicio del hub |
| `/?m=<módulo de trámites>`, `/tramites/*`, `/portal/[token]`, `/biometric/[token]`, `/log-qx/*`, `/manual/*` | `<ambiente>.tramites.flitsas.online` con la misma ruta |
| `/admin/*` de plataforma: compañías, usuarios, RBAC, auditoría, marca y dominio | Pantallas equivalentes del hub |
| `/admin/*` de Trámites: organismos de tránsito, documental, improntas, Quipux, tipos de trámite, mandatos, FUR, causales, generación documental | `<ambiente>.tramites.flitsas.online` |
| `/email-assets/*` | Se sigue sirviendo para los correos ya enviados; lo nuevo sale de `assets.` |

En PDN el reparto es el mismo sobre `flitsas.online`. Las redirecciones de rutas propias de Trámites son **permanentes** (308) porque el hub nunca las va a usar. La raíz `/` no se redirige: pasa a ser el inicio del hub.

**Momento:** Fase 2, a la vez que Trámites pasa a OIDC. La cookie actual es del host, así que el cambio pide iniciar sesión una vez; hacerlo junto con OIDC evita pedirlo dos veces.

**Configuración:**

- `Invitations:ActivateUrlBase` y `PasswordRecovery:ResetUrlBase` apuntan al host del hub de cada ambiente. En DEV ya apuntan a `dev.flitsas.online`, que será el hub.
- `CORS_ORIGIN` y `front_domain` del CD se separan en hub y Trámites.
- Los valores por defecto `https://dev.flitsas.online/...` escritos en código (composers de correo, `WelcomeRegistrationEmailTemplate`, `PublicBrandingOptions`) salen a configuración por ambiente.
- Sin cambios: host de la API, webhooks de Kyverum y dominios de Marca Blanca.

### 4.4 Identidad: OIDC que extiende ADR-0060, con el login en el hub

- **Servidor OIDC (OpenIddict) como módulo de `core-api`.** Reutiliza usuarios, Argon2, suspensiones, el `LoginHandler` con su anti-enumeración y el `DomainContext` sellado.
- **El login está en el hub**: `flitsas.online/login` en PDN, `dev.flitsas.online/login` y `qa.flitsas.online/login` en los otros ambientes. En ese mismo host el borde enruta `/connect/*` y `/.well-known/*` a `core-api` y el resto a `frontend-hub`. Hub e identidad comparten host sin necesitar cookies de dominio.
- **Redes de Marca Blanca**: el login está en el dominio principal de la red (`cliente.com/login`), que es su hub. El sello sigue siendo la única fuente del dominio y el acotamiento por red funciona como hoy. Un usuario de red que intente entrar por `flitsas.online` recibe el `NETWORK_DOMAIN_REQUIRED` actual.
- **Cada producto es un cliente OIDC.** Si el usuario llega directo a `tramites.flitsas.online` sin sesión, el producto lo envía al login del hub y lo devuelve al producto. Su token lleva `aud=<producto>`, el claim `dom` actual y solo los roles y permisos de ese producto. Vida ≈15 min, refresh rotado en Redis.
- **Cada app es un BFF**: cookie `HttpOnly; Secure` **del propio host** y token solo en el servidor. Se retiran la cookie legible y la copia en `localStorage`.
- **Por qué no una cookie compartida `Domain=.flitsas.online`**: DEV, QA y PDN comparten la raíz. La cookie de sesión de PDN viajaría a los servidores de DEV. El SSO lo da la redirección al login del hub.
- **`DomainContext` gana producto**: raíz o `<ambiente>.<raíz>` es el hub; `<ambiente>.<producto>.<raíz>` es un producto. En redes, `tenant_domains` lo indica.
- **`tenant_domains` gana `purpose`** (`HUB` o código de producto) y la unicidad pasa de `tenant_id` a `(tenant_id, purpose)`. Cada host de red reutiliza la verificación TXT y la señal de certificado de ADR-0060.
- **Abierto a futuro**: MFA TOTP con política por empresa; Microsoft, Google y SAML como métodos de la pantalla de login del hub.
- **Prerrequisito**: sacar los ambientes de `Development`, validar el JWT en el gateway y exigir firma en la API.

### 4.5 Suscripciones y RBAC por producto

```
platform.products                     (code, name, icon, status)
platform.tenant_product_subscriptions (tenant_id, product_code, status ACTIVE|SUSPENDED|CANCELLED,
                                       starts_at, ends_at, activated_by, notes)
security.modules  + product_code
security.roles    + product_code      (un rol pertenece a un producto)
security.user_role_assignments        sin cambios (usuario, rol, empresa)
admin.tenant_domains + purpose        (ver §4.4)
```

- La suscripción se aplica en **tres puntos**: al emitir el token, en cada API (`RequireProduct` con caché en Redis) y en el launcher (`GET /api/v1/platform/me/apps`).
- Las hijas de una Concesión o Marca Blanca solo pueden tener productos que su cabeza tenga activos.
- Cada producto publica un **manifiesto** (módulos, permisos, roles por defecto) al arrancar.
- Los booleans actuales se migran a suscripciones y se retiran.

### 4.6 Experiencia de navegación

Modelo definido por el negocio:

1. **Entrada por el hub.** El usuario abre `flitsas.online`, inicia sesión y ve el inicio con las tarjetas de los productos a los que tiene acceso: suscripción activa de su empresa y al menos un rol suyo en ese producto. Los demás no aparecen.
2. **Menú principal de productos.** Es un selector tipo rejilla, idéntico en el hub y en todos los productos y siempre en el mismo lugar de la barra superior. Lista "Inicio" (el hub) y los productos con acceso, y marca el actual. Cambiar de producto lleva al host de ese producto; el SSO evita volver a iniciar sesión. **Es la única forma de cambiar de producto.**
3. **Cada producto tiene su propio menú.** Es su dock, definido por el producto y filtrado por los roles del usuario en ese producto. El menú de productos y el menú del producto nunca se mezclan.
4. **Menú de cuenta** común: perfil, cambio de contraseña y cerrar sesión. Cerrar sesión cierra la sesión en toda la suite.
5. **El hub también tiene su menú**: Inicio, Empresa, Usuarios y roles por producto (AdminCompany), Marca y dominio (cabeza de Marca Blanca), Productos por empresa y catálogos globales (SuperAdmin), Auditoría.

```
 Barra común (packages/shell) en el hub y en cada producto
┌──────────────────────────────────────────────────────────────────┐
│ [logo de la marca]  Trámites                  [▦ Productos] [Cuenta]│
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│                    contenido del producto                        │
│                                                                  │
│        ┌──────────────────────────────────────────────┐          │
│        │ Trámites · Identidad · Reportes · Admin. OT  │  ← menú propio del producto
│        └──────────────────────────────────────────────┘          │
└──────────────────────────────────────────────────────────────────┘

 ▦ Productos                       Inicio del hub (flitsas.online)
┌─────────────────────────┐       ┌───────────┐ ┌───────────┐ ┌───────────┐
│ ⌂ Inicio                │       │ Trámites  │ │Comparendos│ │Diagnóstico│
│ ● Trámites   (actual)   │       └───────────┘ └───────────┘ └───────────┘
│   Comparendos           │        solo los productos con acceso
│   Diagnóstico           │
└─────────────────────────┘
```

**Qué sale del menú actual de Trámites hacia el hub** (inventario fino en la Fase 2): Compañías y sus hijas, Usuarios, RBAC, Auditoría, Marca y dominio, y el catálogo de jobs. **Se queda en Trámites**: Trámites, Preasignación, Identidad, Reportes, Historial de placa, Administración OT, Organismos de tránsito, Documental, Improntas, Quipux, Tipos de trámite, Mandatos, FUR, Causales de rechazo, Generación documental, LOG QX e ICT.

**Piezas compartidas:**

- `packages/shell`: barra superior con marca, menú de productos y menú de cuenta; recibe el catálogo de navegación de cada producto y dibuja su dock (Contract A de `GUIA-DOCK-INFERIOR-FLOTANTE.md`). Deja de importar componentes de productos concretos.
- `packages/ui`: tokens y átomos extraídos de `frontend/components/atom`.
- `packages/auth`: login, callback, refresh, logout y proxy `/api` para Next.js.
- El menú de productos y el inicio del hub leen `GET /api/v1/platform/me/apps`.
- En una red de Marca Blanca todo se ve igual, con la marca de la red y bajo su dominio.

### 4.7 Consultas externas compartidas

- Mover los contratos de consultas (`IConsultationProvider`, registro, cadenas, overrides por empresa) de `Flit.Tramites.*` a un módulo de plataforma.
- Exponerlas a los productos como API de servicio con token de cliente, siguiendo el precedente de `IctConsultationService`.
- Añadir **medición de consumo por empresa y producto**, hoy inexistente.
- Los secretos de Verifik, Kyverum, Fasecolda e Intempo quedan en un solo servicio.

### 4.8 Datos separados e integración

- Cada producto es dueño de su modelo de vehículo y persona, en su schema, con usuario de BD propio.
- Integración por claves naturales comunes (`Placa`, `Vin`, `DocumentoIdentidad`) en `Flit.Platform.Contracts`, por eventos en RabbitMQ con outbox y por APIs de consulta entre servicios.
- Ningún servicio lee por SQL el schema de otro.

### 4.9 Reportes

- Por producto, dentro de cada producto.
- Consolidados en el hub: cada producto publica hechos reportables; la plataforma los guarda en un schema `reporting` de solo lectura.

### 4.10 Infraestructura

- k3s + Argo CD (`flit-gitops`): namespace por ambiente, aplicación por servicio y ambiente.
- Ingress con `Host` preservado y `/api` hacia el gateway, como ya define `deploy/edge/`. En k3s, cert-manager reemplaza acme.sh y se conserva la señal de certificado hacia la API.
- PostgreSQL: un clúster, un schema y un usuario por servicio. Los productos nuevos migran su propio schema (ajuste a ADR-0014; `core-ict` ya es la excepción).
- Redis: sesiones BFF, refresh tokens, caché de suscripciones.
- RabbitMQ: eventos entre productos y hacia reportes consolidados.

---

## 5. Plan por fases: plataforma primero

Duraciones relativas, sin fechas, con el equipo de tres personas. Sprints de una semana, PRs ≤ 800 líneas a `develop`.

### 5.0 Reparto del equipo durante la plataforma

Cada persona toma el frente de plataforma más cercano a su producto, para llegar a la Fase 3 conociendo lo que va a usar.

| Persona | Frente de plataforma | Por qué |
|---|---|---|
| Desarrollador de **Trámites** | Identidad OIDC, salida de `Development` y validación del JWT, `packages/auth`, migración de Trámites a la suite y a su host nuevo | Conoce `core-api`, Security, Marca Blanca y el frontend actual |
| Desarrollador de **Comparendos** | Productos y suscripciones, RBAC por producto, `DomainContext` con producto, `frontend-hub`, `packages/ui` y `packages/shell` | Su producto será el primer cliente del hub, del switcher y de los roles por producto |
| Desarrollador de **Diagnóstico** | Consultas externas compartidas con medición, SDK .NET, plantilla `flit-product`, eventos y RabbitMQ, producto de prueba | Diagnóstico será el mayor consumidor de consultas externas y de la plantilla |
| Líder técnico / infraestructura | k3s, Argo CD, ingress, certificados, Redis, RabbitMQ, DNS, aprobación de ADRs | Ya tiene a cargo la migración a k3s |

Revisión cruzada: cada PR de plataforma lo revisa al menos uno de los otros dos desarrolladores.

El detalle de cada frente, con tareas, dependencias y criterios de terminado, está en [`frentes/`](frentes/): [A](frentes/frente-a-identidad-y-tramites.md), [B](frentes/frente-b-productos-y-hub.md), [C](frentes/frente-c-consultas-sdk-y-plantilla.md) y [líder](frentes/frente-l-lider-e-infraestructura.md).

### 5.1 Fase 0 — Fundaciones (≈2–3 semanas)

1. Aprobar ADRs 0061–0065. Se parte de Marca Blanca tal como está en `develop` (ADR-0060).
2. **Sacar los tres ambientes de `Development`**, activar la validación del JWT en el gateway y exigir firma en la API. Tiene su propio riesgo: probar primero en DEV con los flujos de Marca Blanca.
3. Rotar y sacar del repo la contraseña SMTP. Dejar en verde las 86 pruebas del frontend que hoy fallan en `develop`.
4. ✅ **Arreglo `5ae9578f` traído de `release`** en la rama `feature/nueva-suite-flit`, combinado con las exclusiones de Marca Blanca (HU #12761). Llega a `develop` con el PR de esa rama y debe fusionarse antes de la próxima promoción a `release`.
5. k3s: namespaces por ambiente, ingress, cert-manager, Redis, RabbitMQ, usuario de BD por servicio.
6. DNS de los hosts de §4.2; redirecciones desde los hosts actuales de trámites.
7. Espiga técnica de OpenIddict sobre el `LoginHandler` y el `DomainContext` actuales.

### 5.2 Fase 1 — Núcleo de plataforma (≈4–6 semanas)

1. Servidor OIDC con login en el host del hub y en el dominio principal de cada red, refresh en Redis, token por producto.
2. Schema `platform`: productos y suscripciones; pantallas SuperAdmin para activar y suspender productos.
3. `product_code` en módulos y roles; migración de los roles actuales a `tramites` o `plataforma`.
4. `DomainContext` con producto; `tenant_domains` con `purpose`.
5. `GET /api/v1/platform/me/apps` y policy `RequireProduct`.
6. Módulo de consultas compartidas con medición de consumo.

### 5.3 Fase 2 — Hub, kits y Trámites en la suite (≈4–6 semanas)

1. `packages/ui`, `packages/shell`, `packages/auth`, `packages/config`; `services/shared/*`.
2. `frontend-hub` en `flitsas.online`: login, inicio con productos, menú de productos, cuenta, empresa, usuarios y roles por producto, suscripciones, marca.
3. Trámites entra a la suite: `<ambiente>.tramites.flitsas.online` con redirecciones 308 desde los hosts actuales, recursos de correo en `assets.`, login por OIDC y app switcher (§4.3).
4. Plantilla `templates/flit-product` y un **producto de prueba** desplegado de punta a punta en DEV, para validar la plantilla antes de entregarla.
5. Cliente TypeScript generado desde OpenAPI para los servicios nuevos.

### 5.4 Puerta de salida: "plataforma lista"

Los productos arrancan cuando todo esto se cumple en QA:

- [ ] Gateway y API validan firma y audiencia en todos los ambientes.
- [ ] Un usuario entra a `flitsas.online`, inicia sesión, ve solo sus productos, abre Trámites sin volver a autenticarse, cambia de producto desde el menú de productos y ve el menú propio de cada uno.
- [ ] El SuperAdmin activa y suspende un producto para una empresa, y el efecto se nota en minutos sin re-login.
- [ ] El AdminCompany asigna roles por producto desde el hub.
- [ ] Una red de Marca Blanca entra por su dominio con su marca, como hoy.
- [ ] El producto de prueba, creado con la plantilla, se despliega con Argo CD en `dev.<producto>.flitsas.online` con login, shell, suscripción y schema propios.
- [ ] Un producto puede llamar a una consulta externa compartida y el consumo queda medido.

### 5.5 Fase 3 — Comparendos y Diagnóstico (en paralelo)

Cada desarrollador parte de la plantilla: ADR de alcance del producto, modelo de dominio, schema, API, UI con los paquetes compartidos, manifiesto de roles, eventos publicados. Primero DEV, luego QA y PDN.

### 5.6 Fase 4 — Evolución

MFA TOTP, Microsoft, Google y SAML; reportes consolidados; Flotas desde la plantilla; mover `frontend/` a `frontend-tramites` si se quiere uniformidad; extraer la identidad a su propio servicio si la carga lo pide.


---

## 6. Riesgos

| Riesgo | Mitigación |
|---|---|
| Salir de `Development` y encender la validación del JWT rompe flujos que hoy funcionan por la omisión | Hacerlo primero en DEV con la suite de paridad de Marca Blanca y los tests de login; bandera por ambiente |
| La identidad OIDC contradice decisiones de ADR-0060 | El borrador ADR-0062 lo extiende sin reemplazar: el login sigue en el host sellado de cada red |
| Mover la administración de plataforma de `frontend/` al hub duplica pantallas durante la transición | Mover por secciones, con redirecciones desde las rutas viejas |
| Un dev por producto: nadie más conoce el código | Plantilla común, participación en la plataforma, revisión cruzada, `CODEOWNERS` |
| El desarrollador de Trámites reparte su tiempo entre la plataforma y el soporte de Trámites; el roadmap de Trámites se frena | Acordar con el PO qué trabajo de Trámites se congela durante las Fases 1 y 2; priorizar solo errores de PDN |
| Enlaces ya enviados apuntan a los hosts actuales de Trámites | Reparto de rutas de §4.3; `/email-assets` sigue servido en los hosts viejos |
| Hub en la raíz: una cookie emitida por error con `Domain=flitsas.online` llegaría a todos los ambientes | Regla en `packages/auth` y prueba automática que falla si alguna respuesta emite cookies con `Domain` |
| Al pasar a `flitsas.com` la raíz la ocupa el sitio corporativo | Host del hub por configuración; decidir en ese momento entre mover el sitio corporativo o el hub |
| Clúster Postgres compartido | Usuario y límite de conexiones por servicio |
| Consultas compartidas se vuelven cuello de botella | Caché existente por empresa, límites por producto, métricas |

---

## 7. Preguntas abiertas

- **P1.** ¿El PO acepta congelar el roadmap de Trámites durante las Fases 1 y 2, salvo errores de producción? Sin eso, la plataforma se alarga.
- **P2.** ¿Hay enlaces a rutas de Trámites en `flitsas.online` publicados fuera de FLIT, por ejemplo en sistemas de organismos de tránsito o de clientes? Seguirán funcionando por redirección, pero conviene saber a quién avisar.
