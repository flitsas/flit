# Frontera del servicio de identidad y contrato de tokens

HU #13223 (Feature #13218, Epic #13217). Plan aprobado: https://claude.ai/artifact/PtR84VYp9FLC61VSmBa5wU

Qué se va a `core-identity`, qué se queda en `core-api`, cómo se hablan y qué contrato siguen los productos. Para el
estado actual de la suite, ver [mapa-y-banderas.md](mapa-y-banderas.md).

## 1. Objetivo y lo que no se busca

**Objetivo:** que el login y el OIDC sigan funcionando cuando `core-api` se cae, se reinicia o se despliega, y que un
`core-api` sin memoria no arrastre a la identidad.

**No se busca en esta entrega:** separar la base de datos, separar el código en repositorios distintos ni mover la
administración de empresas y organismos. La identidad se separa **por proceso**: dos procesos sobre el mismo código y
la misma base.

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

**El mismo programa con dos papeles.** `core-identity` es el mismo `Flit.Api.dll` de `core-api`, arrancado con
`Flit__HostRole=identity`. Con ese papel:

- **No** corre migraciones ni el seeder (siguen siendo de `core-api`).
- **No** corre procesos en segundo plano de negocio: solo los dos de OIDC (`OidcClientSync`, `OidcPruningService`).
- **Solo** mapea los endpoints de identidad (sección 4).
- Usa el mismo registro de servicios y el mismo orden de middlewares que `core-api`: es el mismo `Program.cs`, así que
  no puede desviarse (dominio sellado, `DomainBindingMiddleware`, políticas, límite de tasa de la marca).

El **gateway decide** a cuál proceso manda las rutas de identidad con `Gateway:IdentityCluster:Enabled`. Apagada, todo
va a `core-api` como hoy; volver atrás es apagarla.

Opciones evaluadas (con dos revisiones independientes, de arquitectura y de planificación, que llegaron a lo mismo):

| Opción | Resuelve la caída del login | Riesgo | Esfuerzo |
|---|---|---|---|
| **A. Mismo programa con papel `identity`** (elegida) | Sí | Bajo: no se mueve código de identidad; no hay composición que se desvíe | Bajo |
| A'. Proyecto anfitrión nuevo (`Flit.Identity.Api`) | Sí | Medio: el código a reusar es `internal`; mover endpoints, middlewares y autorización a una librería | Medio |
| A''. Partir `AddPostgresInfrastructure` (1.430 líneas) en piezas | Sí | Medio: orden de registro; dependencias escondidas (canal Renting del correo, auditoría, dominios) | Medio |
| B. Servicio con su propio `DbContext` | Sí | Medio: dos mapeos de las mismas tablas | Medio |
| C. Servicio y base separados | Sí | Alto: 98 llaves foráneas, triggers, vistas, RLS y 12 escrituras cruzadas | Alto |

A no cierra el camino a lo demás: cuando se quiera adelgazar `core-identity`, se parte el registro con una prueba que
congele la lista de servicios.

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
- **Migraciones solo en `core-api`.** `core-identity` expone `/health/ready`, que falla si la base tiene migraciones
  pendientes: no atiende con un esquema que no conoce.
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

## 8. Lo que hay que hacer en el código

**Etapa 2 (preparar, sin cambiar a `core-api`):**

1. `Flit__HostRole` en `Program.cs`: con `identity`, saltar migraciones, seeder y gRPC; quitar los procesos en segundo
   plano de `Flit.*` salvo los dos de OIDC (filtrando descriptores, nunca `RemoveAll<IHostedService>`, que quitaría el
   propio servidor web); no agregar el middleware de telemetría de uso (su escritor no corre).
2. `MapIdentityEndpoints()`: las rutas de la sección 4, usadas por los dos papeles; `core-api` además mapea todo lo
   demás.
3. `/health/ready` con conexión a la base y sin migraciones pendientes.

**Etapa 3-4 (conectar):**

4. Gateway: las rutas de la sección 4 llevan una marca `FlitIdentity` y conservan su política actual; un filtro de
   configuración las manda a `core-identity-cluster` solo con `Gateway:IdentityCluster:Enabled`. Apagado, el gateway
   se comporta igual que hoy.
5. Compose: servicio `core-identity` (puerto `4004`/`5004`/`6004`) con la misma imagen, perfil `identity` (no arranca si no se pide), ancla de
   variables compartidas, `/health/ready` como healthcheck y sin `depends_on` de `core-api` (tiene que poder arrancar
   aunque `core-api` esté caído).
6. Pruebas:
   - Arranque del papel `identity`: valida el contenedor y solo tiene los procesos de OIDC.
   - Paridad de rutas: identidad expone exactamente la sección 4 y cada ruta existe igual en `core-api`.
   - Un token emitido por identidad sirve en `core-api`; tras cerrar sesión en identidad, `core-api` responde
     `SESSION_EXPIRED`.
   - El login de siempre en identidad da un JWT que `core-api` acepta.
   - Con `core-api` apagado, login, autorización y token siguen funcionando en identidad.
   - Gateway: con la bandera encendida y apagada, cada ruta va a su destino y conserva su política.

## 9. Lo que va con la VPS y el pipeline (Feature #13220, con Jorman)

| Tema | Qué hace falta | Por qué |
|---|---|---|
| Despliegue por servicio | Quitar el `docker compose down` global; `up -d --no-deps <servicio>`; identidad se actualiza después de que `core-api` migró y quedó sano | Hoy cada merge reinicia todo: el login se caería en cada despliegue |
| Etiqueta propia | `CORE_IDENTITY_TAG`, por defecto la de `core-api` | Poder dejar identidad en una versión mientras `core-api` cambia |
| Memoria | Límite de memoria a `core-api` y `oom_score_adj` para que identidad sea lo último que el sistema mate | Un `core-api` sin memoria en el mismo VPS puede llevarse cualquier proceso |
| Conexiones a la base | Tope del pool de `core-api` y conexiones reservadas para identidad | Si `core-api` agota `max_connections`, identidad no puede entrar a la base |
| CORS del gateway | Se queda leyendo los dominios activos de `core-api` (`/internal/domains`, ruta de negocio) | Si el gateway se reinicia con `core-api` caído, cae a la lista fija de orígenes; el hub y Trámites no se afectan porque llaman a la API desde su servidor |

## 10. Riesgos que quedan y cómo se cubren

| Riesgo | Cobertura |
|---|---|
| Cachés por proceso: apagar un producto (30 s), un dominio o emisor (60 s - 5 min) o una marca se invalida solo en el proceso que lo hizo | Vida corta; se documenta. Invalidar por la base queda para después |
| Los dos procesos con servidor OIDC | Comparten todo en la base; `OidcClientSync` está hecho para instancias concurrentes; misma configuración por ancla |
| Rotar la llave de firma | Mismo `SigningKeyId` en los dos por ancla. Aceptar varias llaves a la vez queda para cuando se rote por primera vez |
| Telemetría de uso de las rutas de identidad | Se pierde mientras la bandera esté encendida. Aceptado: son rutas de login, no de negocio |
| `core-identity` carga todos los servicios de `core-api` (más memoria) | Aceptado a cambio de no mover código; se adelgaza después con la prueba que congela el registro |
| `Security.Application` arrastra `Admin.Application` | Mismo programa: no aplica hasta que se separe el código |
