# Frontera del servicio de identidad y contrato de tokens

HU #13223 (Feature #13218, Epic #13217). Plan aprobado: https://claude.ai/artifact/PtR84VYp9FLC61VSmBa5wU

Qué se va a `core-identity`, qué se queda en `core-api`, cómo se hablan y qué contrato siguen los productos. Para el
estado actual de la suite, ver [mapa-y-banderas.md](mapa-y-banderas.md).

## 1. Objetivo y lo que no se busca

**Objetivo:** que el login y el OIDC sigan funcionando cuando `core-api` se cae, se reinicia o se despliega, que un
`core-api` sin memoria no arrastre a la identidad, y que un cambio en uno no reconstruya ni redespliegue al otro.

**No se busca en esta entrega:** separar la base de datos, separar el código en repositorios distintos ni mover la
administración de empresas y organismos. La identidad es un **servicio aparte** (código, programa, imagen y CD propios)
sobre la misma base.

## 2. Lo que encontró el inventario

| Hallazgo | Cifra | Consecuencia |
|---|---|---|
| Tablas de identidad (`identity`, `security`, `platform`, `data_protection_keys`) | 22, todas en el único `FlitDbContext` | No hay un contexto propio que separar sin rehacer el mapeo |
| Llaves foráneas de negocio hacia `identity.tenants` / `identity.users` | 96 en `core-api` + 2 en `core-ict` | Separar la base es otro proyecto |
| Triggers que cruzan esquemas (`platform` ↔ `identity` ↔ `admin`) | 5, más `trg_audit_log` y RLS | Las reglas viven en la base: siguen funcionando con un solo Postgres |
| Archivos fuera de identidad que la leen | ~45 | Siguen leyendo igual: misma base |
| Sitios fuera de identidad que la **escriben** | 12 | Creación y edición de empresas y organismos (con tablas `admin` en la misma transacción), invitaciones del OT, usuario de servicio de ICT |
| Procesos en segundo plano registrados | 18 (15 de `AddPostgresInfrastructure`, 1 de `AddAdminInfrastructure`, 2 de OIDC) | Identidad solo debe correr los 2 de OIDC |
| Tablas de identidad con seguridad por fila (RLS) activa | 5 (`user_role_assignments`, `user_invitations`, `invitation_roles`, `user_temp_suspensions`, `platform.tenant_products`) | Hoy no filtran porque `core-api` entra como dueño; un usuario de base nuevo vería cero filas |
| Dependencias de `Security.Application` | `Admin.Application` (auditoría), esquema `admin` (marca, dominios, perfiles OT), pipeline de correo | Identidad carga esos proyectos; no se separan en esta entrega |

## 3. Decisión de arquitectura

**Un servicio separado de verdad** (decisión de Samuel, 1 oct 2026, sobre lo aprobado con el CTO y el líder técnico):
`core-identity` vive en `services/core-identity`, con su propio programa, Dockerfile, imagen y job de CD con filtro de
rutas. Comparte con `core-api` solo librerías de dominio y de infraestructura de identidad, nunca código de negocio.

| Proyecto | Dónde | Qué tiene | Quién lo usa |
|---|---|---|---|
| `Flit.Identity.Api` | `services/core-identity/src` | El programa: contexto de datos propio, solo los procesos de OIDC, mismo orden de middlewares que `core-api` | core-identity |
| `Flit.Identity.Web` | `services/core-identity/src` | Servidor OIDC, endpoints de auth, platform y marca pública, límite de tasa | core-identity (y core-api solo en la transición) |
| `Flit.Identity.Application` | `services/core-identity/src` | Login, recuperar/cambiar/restablecer contraseña, activar cuenta | core-identity (y core-api solo en la transición) |
| `Flit.Suite.AspNetCore` | `services/core-api/src` | Validación de tokens, dominio sellado, aceptación de tokens del hub, hosts de productos | los dos |
| `Flit.Identity.Infrastructure` | `services/core-api/src` | Entidades, configuraciones, repositorios y adaptadores de identidad (correo, llaves, marca); `IdentityDbContext` e `IIdentityDb` | los dos |
| `Security.*`, `Platform`, `Admin.Domain/Application`, `Queries.Domain` | `services/core-api/src` | Dominio compartido (usuarios, roles, productos, Marca Blanca) | los dos |

El árbol de `core-identity` son exactamente esos 11 proyectos. `Flit.Identity.Tests` falla si aparece `Flit.Infrastructure`,
`Flit.Api` o algo de Trámites, OT, reportes, consultas, Quipux o ICT, y si el Dockerfile no copia todo el árbol.

**Datos.** `core-identity` usa `IdentityDbContext`, que solo conoce las tablas de identidad y **no tiene migraciones**:
las corre `core-api` con `FlitDbContext`, que aplica las mismas configuraciones. Una prueba compara los dos modelos
columna por columna. Los repositorios de identidad dependen de `IIdentityDb`: en `core-api` es el mismo `FlitDbContext`
(un solo contexto, como siempre) y en `core-identity` es `IdentityDbContext`.

**Configuración.** `core-identity` usa el mismo `appsettings.json` de `core-api` (enlazado, no copiado) y el compose le
pasa el mismo bloque de variables con un ancla: emisor, llaves de firma, clientes OIDC y correo no pueden divergir.

**Transición.** Mientras identidad se estabiliza, `core-api` sigue atendiendo las mismas rutas y es el respaldo del
gateway. En el corte (HU #13235) deja de atenderlas y deja de referenciar `Flit.Identity.Web` y `Flit.Identity.Application`:
desde ahí un cambio en el login no reconstruye `core-api`.

Cómo se llegó aquí: primero se probó «el mismo programa con dos papeles» (resolvía la supervivencia del login pero no
la independencia de build) y se reemplazó. Dos revisiones independientes coincidieron en no separar la base de datos
(98 llaves foráneas, triggers, RLS y 12 escrituras cruzadas).

## 4. Qué atiende `core-identity`

Solo lo que el login necesita para sobrevivir a una caída de `core-api` (lo que llaman el hub y `@flit/auth`):

| Ruta | Para qué |
|---|---|
| `/connect/*`, `/.well-known/*` | Servidor OIDC: login, autorización, tokens, cierre de sesión, llaves |
| `/api/v1/auth/*` | Login de siempre, recuperar y cambiar contraseña, activar invitación, `me` |
| `/api/v1/platform/*` | `me/apps` (inicio del hub), emisores, manifiesto, productos por empresa |
| `/api/v1/public/branding*` | Marca de la pantalla de login (Marca Blanca) |

Todo lo demás sigue en `core-api`, incluidos `/api/v1/security/*` y `/api/v1/superadmin/*` (administración de
usuarios y roles). Pasan a identidad junto con B-12 o en el Epic siguiente; moverlos ahora obliga a partir
`MapSuperAdminEndpoints` y amplía lo que se despliega con identidad sin mejorar la supervivencia del login.

## 5. Cómo se hablan

**No se llaman entre sí.** Los dos leen y escriben la misma base con el mismo usuario.

- **El servidor OIDC queda encendido en los dos procesos.** Comparten llaves de firma, anillo de Data Protection (la
  cookie `flit_hub`) y tokens, todo en la base. El gateway decide quién atiende; por eso apagar la bandera es una
  vuelta atrás real. Apagar el servidor en `core-api` es un paso posterior, cuando todo esté estable.
- **Validar un token en `core-api`:** igual que hoy, sin red: llave de `security.jwt_signing_keys` y sesiones cerradas
  en `identity.oidc_authorizations` (caché de 15 s). Si `core-identity` se cae, `core-api` sigue validando.
- **Productos nuevos:** validan con el JWKS público del hub, no leen la base de identidad.
- **Misma configuración en los dos:** el compose comparte el bloque de variables de `core-api` con un ancla de YAML.
  Si difirieran el `SigningKeyId`, los clientes OIDC o las URL de retorno, uno rechazaría los tokens del otro o
  `OidcClientSync` los pisaría al arrancar.

## 6. Base de datos

- **Mismo usuario de base en esta entrega.** Un usuario nuevo sin `BYPASSRLS` vería cero asignaciones de rol, cero
  invitaciones y cero productos (sección 2). La decisión 2 del plan se ajusta: el usuario propio de identidad llega
  junto con el mínimo privilegio, cuando se muevan las 12 escrituras cruzadas.
- **Migraciones solo en `core-api`.** `core-identity` expone `/health/ready`, que falla si a la base le faltan
  migraciones que trae su código (por ejemplo, identidad nueva antes de que `core-api` migre). Al revés, código viejo
  sobre un esquema más nuevo, sí atiende: es lo normal en un despliegue por servicio y las migraciones son aditivas.
- **El gateway consulta esa salud.** Las rutas del login van a un grupo con dos destinos: `core-identity` primero y
  `core-api` de respaldo (atiende las mismas rutas). Si identidad se cae o no está lista, el login sigue en `core-api`;
  si `core-api` se cae, sigue en identidad. Así el login tampoco se cae mientras identidad se reinicia.
- **Escrituras cruzadas que se quedan en `core-api`:**

| # | Sitio | Qué escribe |
|---|---|---|
| 1-3 | `CompanyWriteRepository` | crear, activar y editar empresa (+ auditoría `admin`, misma transacción) |
| 4-5 | `CompanyHierarchyRepository` | crear hija, cambiar padre |
| 6-7 | `TransitOfficeTenantWriteRepository` | crear organismo (+ perfil OT), activar |
| 8 | `DbHierarchySwitches` | interruptores de jerarquía (de negocio, guardados en `identity`) |
| 9, 12 | `AdminCompanyChildrenInvitationsEndpoints` | invitaciones y usuarios de hijas |
| 10 | `IctOrchestrationService` | INSERT del usuario de servicio de ICT |
| 11 | `AdminOtEndpoints` | usuarios del OT vía handlers de Security |

## 7. Contrato de tokens (para productos)

| Elemento | Valor |
|---|---|
| Emisor (`iss`) | URL pública del hub del ambiente (`https://flitsas.online/`, `https://qa.flitsas.online/`…) o un dominio de Marca Blanca activo |
| Descubrimiento | `<emisor>.well-known/openid-configuration` |
| Llaves | `<emisor>.well-known/jwks.json` (RS256) |
| Audiencia (`aud`) | código del producto (`tramites`, `comparendos`…) |
| Vida | 15 min (el máximo de sesión se acuerda en el Feature #13221) |
| Claims | `sub`, `tenant_id`, `tenant_type`, `is_group_parent`, `entity_type`, `dom`, `product`, `role`, `role_code`, `role_id`, `roles[]`, `permissions[]`, `oi_au_id` |
| Sesión cerrada | Un producto que necesite cortar antes del vencimiento revisa `oi_au_id`; si no, el token vale hasta 15 min |

Sin cambios respecto a [contrato-plataforma-v1.md](contrato-plataforma-v1.md) §2: separar el proceso es invisible para
los productos.

## 8. Cómo quedó en el código

1. **Persistencia** (`Flit.Identity.Infrastructure`): entidades y configuraciones movidas sin cambiar espacios de
   nombres (una migración de prueba antes y después es idéntica); `IIdentityDb`, `IdentityDbContext` y
   `NpgsqlConventions`; repositorios y adaptadores de identidad con su registro (`IdentityInfrastructureExtensions`).
   `CoreApiServiceRegistrationSnapshotTests` congela el registro de servicios de `core-api` (1131 registros): no cambió.
2. **Web compartido** (`Flit.Suite.AspNetCore`): `AddFlitTokenValidation`, `AddFlitSessionExpiredResponses`, política
   SuperAdmin, dominio sellado, `RequestTenantResolver`, `AddFlitOidcAcceptance<TContext>` (almacenes de OpenIddict +
   aceptación de tokens; sin ellos no corre la revisión de sesiones cerradas).
3. **Servicio** (`services/core-identity`): `Flit.Identity.Application`, `Flit.Identity.Web`, `Flit.Identity.Api` y
   `Flit.Identity.Tests`. `/health/ready` comprueba que existan todas las tablas y columnas de su modelo (no migra).
4. **Gateway**: las rutas de la sección 4 llevan la marca `FlitIdentity` y conservan su política; con
   `Gateway:IdentityCluster:Enabled` van a `core-identity-cluster` (`core-identity` primero, `core-api` de respaldo
   durante la transición, chequeo de `/health/ready` cada 2 s).
5. **Compose**: `core-identity` con su imagen, en el perfil `identity`, ancla de variables, sin `depends_on` de
   `core-api`. **CD**: job `changes` con filtros de rutas y job `build-core-identity`; sin cambios, la imagen anterior
   se reetiqueta con el commit. Despliegue por servicio detrás de `FLIT_DEPLOY_ROLLING`.
6. **Pruebas que cruzan los dos servicios**: el login completo funciona solo con `core-identity`; sus tokens (OIDC y de
   siempre) sirven en `core-api`; cerrar sesión en uno corta en el otro; la sesión del hub sirve en los dos; cada ruta
   de `core-identity` existe igual en `core-api` (transición).

## 9. Lo que va con la VPS y el pipeline (Feature #13220, con Jorman)

| Tema | Qué hace falta | Por qué |
|---|---|---|
| Despliegue por servicio | Quitar el `docker compose down` global; `up -d --no-deps <servicio>`; identidad se actualiza después de que `core-api` migró y quedó sano | Hoy cada merge reinicia todo: el login se caería en cada despliegue |
| Imagen y build propios | `core-identity` con su imagen; el CD la reconstruye solo si cambió su árbol (si no, la reetiqueta) | Un cambio en Trámites no reconstruye ni reinicia identidad |
| Memoria | Límite de memoria a `core-api` y `oom_score_adj` para que identidad sea lo último que el sistema mate | Un `core-api` sin memoria en el mismo VPS puede llevarse cualquier proceso |
| Conexiones a la base | Tope del pool de `core-api` y conexiones reservadas para identidad | Si `core-api` agota `max_connections`, identidad no puede entrar a la base |
| CORS del gateway | Se queda leyendo los dominios activos de `core-api` (`/internal/domains`, ruta de negocio) | Si el gateway se reinicia con `core-api` caído, cae a la lista fija de orígenes; el hub y Trámites no se afectan porque llaman a la API desde su servidor |

## 10. Riesgos que quedan y cómo se cubren

| Riesgo | Cobertura |
|---|---|
| Cachés por proceso: apagar un producto (30 s), un dominio o emisor (60 s - 5 min) o una marca se invalida solo en el proceso que lo hizo | Vida corta; se documenta. Invalidar por la base queda para después |
| Los dos procesos con servidor OIDC | Comparten todo en la base; `OidcClientSync` está hecho para instancias concurrentes; misma configuración por ancla |
| Rotar la llave de firma | Mismo `SigningKeyId` en los dos por ancla. Aceptar varias llaves a la vez queda para cuando se rote por primera vez |
| Telemetría de uso de las rutas de identidad | core-identity no la registra (su escritor es de negocio). Aceptado: son rutas de login |
| El dominio compartido (`Security.*`, `Admin.*`, `Platform`) reconstruye los dos servicios cuando cambia | Esperado: lo usan los dos. Sacar la auditoría y Marca Blanca de `Admin.Application` lo achica (Epic posterior) |
| Alguien agrega una referencia de negocio a core-identity | `BuildClosureTests` lo detecta en CI |
