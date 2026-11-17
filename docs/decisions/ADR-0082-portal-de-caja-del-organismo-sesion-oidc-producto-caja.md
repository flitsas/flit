# ADR-0082: La caja del organismo inicia sesión por OIDC con un producto `caja` y el BFF autoriza por rol y organismo

**Fecha**: 2026-11-09
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15), Product Owner (Épica #14210, Feature #14453).
**Tags**: arquitectura, seguridad, identidad, backend, frontend, persona-natural, caja, habeas-data
**Épica / Feature**: Épica #14210 · Feature #14453 · HU #14454 a #14462
**Rama**: `feature/AB-14447-pago-punto-caja` (base `origin/develop`)
**Relacionado**: ADR-0063 (RBAC por producto, DDL 119/120), ADR-0070 (gRPC entre servicios con token de servicio), ADR-0075 (BFF del portal y fachada gRPC), ADR-0081 (marca del portal por organismo)
**Diseño y contratos**: `docs/design/EPICA-14210-caja-ot-diseno-y-contratos.md`
**Supersedes**: ninguno. No contradice ADRs Aceptados.

## Contexto

La persona del punto sigue sin cuenta. La caja del organismo (OT) es lo único del portal Persona Natural que exige login: busca un trámite por su código público (`PN-AAAA-NNNNNN`), ve el total que calcula el servidor, confirma el cobro con un medio y ve la lista del día. El administrador del OT crea y desactiva usuarios de caja y anula cobros desde su hub. Decisiones del usuario ya tomadas (no se reabren): la caja es un usuario de Identidad de la compañía del OT, con rol nuevo `ot_caja` atado a un organismo; varios por punto; sesión de 12 horas o hasta cerrar sesión; no entra a módulos de FLIT 2.0; cambios en core-api solo aditivos y apagados por defecto.

Hechos verificados en el código:

1. **Todos los frontends de la suite usan OIDC code + PKCE** con `client_id = código del producto = aud` (`OidcClientSync`, `ProductCodes.All`, `@flit/auth`). La sesión del hub dura 12 h (`HubSessionHours = 12`); el access token, 15 min; el refresh, 14 días. Cada token se reemite releyendo al usuario (`OidcPrincipalFactory`): una suspensión o un cambio de rol se aplican en el siguiente refresh.
2. **core-api solo sirve las audiencias `plataforma` y `tramites`.** Un token de otro producto solo entra a `/api/v1/platform/me/apps` (`OidcTokenAcceptance.OtherProductAudiences`). Un producto nuevo queda fuera de FLIT 2.0 sin código adicional.
3. **Hueco real**: `OtModuleAuthorizationHandler` deja pasar a CUALQUIER usuario de un tenant `entity_type = TRANSIT_OFFICE`, sin mirar el rol. El hub (`plataforma`) no exige rol para emitir token. Un usuario del tenant del OT con solo `ot_caja` podría obtener un token `plataforma` (o el JWT heredado de `/api/v1/auth/login`) y llegar a la API del OT. El rol nuevo no basta: hay que cerrar este camino.
4. El BFF (`core-personanatural`) valida JWT con `AddFlitPlatformAuthentication` (una sola audiencia: `personanatural`, usada por los tokens de servicio). Su sesión de punto es una cookie `pn_punto` (`CookiePunto`, DataProtection, HttpOnly, Lax, 12 h) que la fija el organismo, no una identidad de usuario. El proxy de Next (`/api/v1/persona-natural/[...path]`) guarda las cookies y adjunta `Bearer`.
5. core-api llama al BFF por gRPC con el cliente `svc-tramites` (scope `platform.personanatural.leer`, `RadicacionService`), con el registro condicionado a `PersonaNatural:Consumidor:Habilitado`. El BFF llama a core-api con `svc-personanatural` (`PersonaNaturalService`). El cobro ya vive en el BFF (`CobroSolicitud`, Feature #14447: estados `habilitado | pagado | anulado`, `pagado_por`, `anulado_por`, `anulacion_motivo`).
6. El hub del OT ya tiene el patrón de alcance: `AdminPortalCobroEndpoints.ResolverAlcanceAsync` (`ot_admin` toma el organismo de su token y ajeno da 403; SuperAdmin envía `?transitOfficeId=`).

## Alternativas consideradas

### Decisión 1 — Cómo inicia sesión la caja

#### Opción A: OIDC por producto nuevo `caja` (code + PKCE con `@flit/auth`), sesión en el servidor de Next, BFF valida el JWT (elegida)

`platform.products('caja')`; el cliente OIDC `caja` lo crea `OidcClientSync` solo; `Suite:Hosts:Overrides:caja` apunta al origen canónico del portal. `frontend-personanatural` suma rutas `/auth/*` de `@flit/auth` y un middleware solo para `/o/{codigo}/caja`. El token vive cifrado en la cookie de sesión de `@flit/auth` (HttpOnly, Lax, sin Domain), con tope absoluto de 12 h; el navegador nunca lo ve. El proxy de Next adjunta `Authorization: Bearer <access token>` solo en `/caja/*`. El BFF acepta la audiencia `caja` y, en cada endpoint, exige rol `ot_caja` y confirma con core-api (RPC) que el usuario sigue activo y a qué organismo pertenece.

**Pros:**
- Reutiliza el 100% del patrón de la suite: PKCE, `state`, rotación de sesión en el callback, refresh, cierre de sesión que revoca la autorización (`oi_au_id`) y bloqueo/auditoría de `CredentialVerifier`.
- Un usuario desactivado pierde el refresh en la siguiente emisión, y el RPC por petición lo corta de inmediato.
- Sin credenciales en el BFF ni en el Next: la contraseña solo la ve Identidad.
- core-api rechaza el token `caja` en todo FLIT 2.0 por construcción (hecho 2).
- Sin sesión propia que mantener en el BFF.

**Contras:**
- El formulario de login es el del hub, no el del organismo (marca de FLIT, no del OT). Se mitiga mostrando la marca del OT tras entrar.
- El callback OIDC está fijo en el origen canónico: en un dominio propio del OT (`transit_office_domains`) la ruta `/caja` redirige al canónico. La caja trabaja siempre en el origen canónico.
- Exige `Suite:Oidc:Enabled=true` en el ambiente (por defecto está `false` en `appsettings.json`; verificar DEV/QA/PDN).
- Cambio en el SDK compartido: `ExtraAudiences` en `PlatformAuthOptions` (aditivo, vacío por defecto).
- Suma una dependencia de workspace (`@flit/auth`) a `frontend-personanatural` (sin paquete externo nuevo).

**Esfuerzo:** M. **Riesgos:** el hub emite token `plataforma` a un usuario solo-caja (se cierra con el guardián de la Decisión 2); desfase del refresh de 14 días frente a las 12 h (se acota con el tope absoluto de la cookie y el chequeo de `auth_time`).

#### Opción B: Login propio en el portal; el BFF verifica credenciales por un gRPC nuevo de Identidad y abre una sesión propia

Formulario en `/o/{codigo}/caja/ingresar`; el Next envía correo y contraseña al BFF; el BFF llama a un RPC nuevo `VerificarCredencialesCaja` en core-identity; el BFF crea una sesión servidor-lado (tabla + cookie DataProtection) de 12 h.

**Pros:**
- Pantalla de ingreso con la marca del organismo y en el dominio propio del OT.
- No depende de que OIDC esté encendido.
- Revocación inmediata propia (fila de sesión).

**Contras:**
- Camino de autenticación nuevo y paralelo al de la suite: un oráculo de contraseñas para cualquier titular de un token de servicio, que hay que blindar (bloqueo, auditoría, límites) y mantener.
- La contraseña pasa por Next y por el BFF: amplía el alcance de Habeas Data y de las revisiones de seguridad de ambos.
- Sesión, expiración, rotación y revocación propias, que hoy resuelve OpenIddict.
- Sin SSO ni cierre de sesión de suite; el equipo compartido queda con dos sesiones distintas.
- Reimplementa lo que ya tiene `@flit/auth` y rompe la regla de no reinventar patrones.

**Esfuerzo:** L. **Riesgos:** fuerza bruta distribuida contra el RPC; divergencia de reglas de contraseña/bloqueo entre Identidad y el BFF.

#### Opción C: Reusar el producto `tramites` con el rol `ot_caja` y llevar el token de Trámites al portal

**Pros:** cero producto nuevo; cero cambio en `ProductCodes`; el login ya existe en `tramites.flitsas.online`.

**Contras:**
- Abre FLIT 2.0: el token `tramites` es aceptado por toda la API de core-api, y el tenant del OT pasa `OtModulePolicy`. Contradice la decisión del usuario («NO entra a los módulos de FLIT 2.0»).
- El portal público compartiría audiencia con el sistema de gestión.
- Un solo rol por producto por empresa: choca con los roles OT que ya usan `tramites`.

**Esfuerzo:** S. **Riesgos:** exposición de módulos de FLIT 2.0 a un perfil de cobro. Descartada.

### Decisión 2 — Cómo nace el rol y cómo se garantiza que no entre a FLIT 2.0

#### Opción 2.1: Rol `ot_caja` en el producto `caja`, invitación dedicada, y guardián «solo-caja» en core-api e Identidad (elegida)

DDL 199: producto `caja`, módulo `caja`, permiso `caja.cobrar`, rol `ot_caja` (`target_entity_type = TRANSIT_OFFICE`, `product_code = 'caja'`, sistema). El administrador del OT y el SuperAdmin usan endpoints dedicados `/api/v1/admin/ot/portal-caja/users` que invocan los mismos handlers de invitación con el rol fijo; el rol se excluye del selector y de la asignación genérica de usuarios OT. Guardián: `OtModuleAuthorizationHandler` niega a quien solo tenga `ot_caja`; un middleware limita al token «solo-caja» a una lista mínima de rutas; Identidad no emite tokens `plataforma` ni `tramites` a un usuario solo-caja y bloquea el JWT heredado.

**Pros:** reusa invitación, activación, suspensión y listado de usuarios OT; el «solo-caja» se impone en tres capas; todo aditivo (sin usuarios `ot_caja` no cambia nada).
**Contras:** toca Identidad (`OidcPrincipalFactory`, `LoginHandler`) además de core-api; el hub deja de mostrar la tarjeta Plataforma a esos usuarios.
**Esfuerzo:** M.

#### Opción 2.2: Rol `ot_caja` dentro del producto `tramites`, sin producto nuevo

**Pros:** sin producto, sin cliente OIDC, sin cambio en `ProductCodes`.
**Contras:** el token es `tramites`; hereda el hueco del tenant OT en toda la API; es la Opción C de la Decisión 1.
**Esfuerzo:** S. Descartada.

#### Opción 2.3: No crear rol; marcar al usuario con una bandera `is_caja` en una tabla propia del BFF

**Pros:** nada en Identidad.
**Contras:** el usuario sigue siendo un usuario común del OT con acceso a lo que su rol permita; desactivarlo exige coordinar dos sistemas; contradice la decisión «rol nuevo `ot_caja`». Descartada.

### Decisión 3 — Cómo el hub lista y anula cobros que viven en el BFF

#### Opción 3.1: core-api como fachada; RPC nuevo `CajaAdminService` en el BFF, llamado con `svc-tramites` (elegida)

El navegador del hub sigue hablando solo con core-api. Los endpoints `/api/v1/admin/ot/portal-caja/cobros` resuelven el organismo con `ResolverAlcanceAsync` (token del `ot_admin`; ajeno da 403) y llaman al BFF por gRPC con un scope nuevo `platform.personanatural.caja`. El BFF solo atiende ese servicio a `svc-tramites` y confía en el organismo que viene de core-api. La anulación y su auditoría quedan en el BFF, que es el dueño del dato.

**Pros:** mismo patrón que `RadicacionService` (cliente, scope, deadline, reintentos); ningún token de usuario viaja al BFF para el hub; la autorización de rol/organismo queda en un solo lugar (core-api); cambio aditivo apagado por `PortalCaja:Habilitado`.
**Contras:** dos saltos (hub, core-api, BFF); un contrato proto más; core-api queda acoplada a la disponibilidad del BFF para esa pantalla (fallo claro 503 `portal_no_disponible`).
**Esfuerzo:** M.

#### Opción 3.2: El hub llama al BFF directo con el token del `ot_admin`

**Pros:** un salto menos; sin proto nuevo.
**Contras:** obliga al BFF a validar tokens `tramites` y a conocer roles de FLIT 2.0; expone el BFF al origen del hub (CORS); rompe «el front del hub solo habla con core-api». Descartada.

#### Opción 3.3: Réplica del cobro en core-api por eventos y anulación contra la réplica

**Pros:** el hub lee su propia base.
**Contras:** consistencia eventual en un dato de dinero; la anulación sigue necesitando al BFF; duplica el modelo. Descartada (tamaño L sin beneficio).

## Decisión

1. **Producto `caja` + OIDC (Opción A).** `ProductCodes.Caja = "caja"`; `Suite:Hosts:Overrides:caja` = origen canónico del portal Persona Natural. `frontend-personanatural` usa `@flit/auth` para `/auth/*` y protege `/o/{codigo}/caja` con un middleware. La caja opera solo en el origen canónico.
2. **El BFF acepta la audiencia `caja` y autoriza en cada endpoint de caja** con la política `CajaPolicy`: `product = caja`, rol `ot_caja` y, por RPC a core-api (`PersonaNaturalService.ResolverUsuarioCaja`, sin caché), que el usuario siga activo, con el rol vigente, y cuál es su organismo. El organismo NUNCA llega del cliente: sale del RPC. El código del trámite de otro organismo responde `403 otro_organismo` sin datos.
3. **Rol `ot_caja` en el producto `caja`** con un único permiso `caja.cobrar`. Creación y desactivación por endpoints dedicados de core-api (invitación por correo: la contraseña la define el usuario al activar). El rol no es asignable por la invitación genérica de usuarios OT.
4. **Guardián solo-caja** (obligatorio en #14454): `OtModuleAuthorizationHandler` niega el rol único `ot_caja`; middleware `SoloCajaGuardMiddleware` lista blanca mínima; Identidad niega tokens `plataforma`/`tramites` y el JWT heredado a quien solo tenga `ot_caja` (código `CAJA_ONLY`).
5. **Hub como fachada (Opción 3.1).** `flit.personanatural.v1.CajaAdminService` (`ListarCobros`, `AnularCobro`) en el BFF, scope `platform.personanatural.caja` solo para `svc-tramites`.
6. **Todo apagado por defecto.** BFF `PersonaNatural:Caja:Habilitado=false` (no mapea endpoints ni servicio gRPC); core-api `PortalCaja:Habilitado=false` (404 en los endpoints nuevos); middleware y handler solo tienen efecto con usuarios `ot_caja`, que no existen antes.

## Consecuencias

**Positivas:** sin camino de autenticación nuevo; FLIT 2.0 cerrado a la caja en tres capas; el desactivado pierde el acceso en el siguiente request; el hub y la caja comparten un solo dueño del dato de cobro (el BFF).

**Negativas / costos:**
- Se cambian cuatro lugares compartidos, todos aditivos: `ProductCodes` (y su prueba), SDK `PlatformAuthOptions.ExtraAudiences`, `OtModuleAuthorizationHandler`, Identidad (`OidcPrincipalFactory`, `LoginHandler`). Cada uno debe correr su suite completa antes del commit (regla del portal: no afectar FLIT 2).
- El login de la caja muestra la pantalla del hub (marca FLIT).
- Un producto más en el hub y en `tenant_products`: el OT debe tener `caja` encendido (la invitación lo asegura).

**Pendientes que requieren al Líder Técnico o al PO (no se deciden aquí):**
- **Ventana de anulación.** El criterio «solo mientras el trámite no haya llegado a FLIT 2.0» choca con «al confirmar se dispara la radicación»: si la radicación es inmediata, la ventana es casi nula. Propuesta: la radicación del trámite pagado en caja se difiere `PersonaNatural:Caja:VentanaAnulacionMinutos` (sugerido 15) y la anulación es posible mientras la solicitud esté `pagada` o `en_cola` sin consumir. Requiere decisión del PO e impacto en #14212.
- **OIDC habilitado por ambiente.** Confirmar con Infra que `Suite:Oidc:Enabled=true` en DEV, QA y PDN y el valor de `Suite:Hosts` para `caja`.
- **Login con marca del OT.** Si el PO exige pantalla de ingreso con la marca del organismo, la Opción B pasa a ser la alternativa y este ADR se reemplaza con un ADR nuevo con `Supersedes`.

## Seguridad y Habeas Data (resumen; detalle en el documento de diseño)

Enumeración de IDs con tope por usuario y por IP; sesión sin fijación (PKCE + `state` + rotación en callback); el access token jamás llega al navegador; CSP del portal sin cambios (`connect-src 'self'`); la respuesta de búsqueda no incluye nombres, documentos ni placa; la prefactura (puede contener datos personales) usa el almacén de adjuntos del portal, se lee solo por el administrador del OT y sigue la retención existente; auditoría de caja sin datos personales, con IP en huella HMAC.

## Decisiones posteriores (2026-11-10)

El usuario eligió la Opción A, aceptó tocar Identidad, el SDK y `OtModuleAuthorizationHandler` (revisión del Líder Técnico en el PR) y fijó una ventana de anulación de 15 minutos antes de radicar el cobro en caja. Detalle en `docs/design/EPICA-14210-caja-ot-diseno-y-contratos.md` §10. El ADR sigue en **Propuesto** hasta que lo acepte el Líder Técnico.
