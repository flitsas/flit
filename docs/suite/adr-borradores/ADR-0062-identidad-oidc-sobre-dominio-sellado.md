# ADR-0062: Identidad OIDC sobre el dominio sellado de ADR-0060, sesión por host y token por producto

**Fecha**: 2026-09-23  
**Status**: BORRADOR (pasa a Propuesto al versionarse en `docs/decisions/`)  
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana — regla FLIT 15), arquitectura, seguridad  
**Tags**: seguridad, identidad, OIDC, OpenIddict, SSO, BFF, marca-blanca, MFA, SAML  
**Extiende**: `ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo` (D2 y D3). No lo reemplaza.  
**Base de código**: `origin/develop@e8b7ca65`

## Contexto

La suite (ADR-0061) exige que una cuenta entre a varios productos en hosts distintos, con roles por producto, y deja abierto MFA, SAML y proveedores externos. En esta etapa todos los ambientes comparten la raíz `flitsas.online` con la convención `<ambiente>.<producto>.flitsas.online`. Las redes de Marca Blanca usan dominios propios.

Lo que ya existe (ADR-0060, verificado en código):

- El gateway sella `X-Flit-Domain` con el `Host` real; la API construye `DomainContext` (`Flit` | `Network(cabeza)`).
- `LoginHandler` acota el login por host: en el dominio de una red solo autentica a su cabeza e hijas; en FLIT, un usuario de red con dominio activo recibe `403 NETWORK_DOMAIN_REQUIRED`. Hash siempre verificado, respuestas uniformes.
- El token lleva `dom` (`flit` o el host de la red) y `DomainBindingMiddleware` rechaza su uso bajo otro dominio.
- ADR-0060 rechazó explícitamente tomar el dominio de un parámetro, de `Origin` o de `Referer`.
- `admin.tenant_domains` admite un dominio por red.

Lo que falta o está roto:

- Los tres ambientes corren con `ASPNETCORE_ENVIRONMENT=Development`; el gateway no valida el JWT (`RequireAssertion(_ => true)`, `DevelopmentNoJwtProxyConfigFilter`). La API acepta tokens sin firma si no hay llave.
- JWT de 12 h con `aud=flit-api` y todos los permisos; sin refresh.
- Cookie `flit_token` sin `HttpOnly` ni `Secure`, más copia en `localStorage`.

## Decisión

1. **Prerrequisito**: sacar los ambientes de `Development`, validar firma y audiencia en gateway y API en todos los ambientes.
2. **Servidor OIDC con OpenIddict** como módulo de `core-api`. Reutiliza usuarios, Argon2, suspensiones y el `LoginHandler` actual como verificación de credenciales.
3. **El login se sirve en el hub**: `flitsas.online` en PDN, `dev.` y `qa.flitsas.online` en los otros ambientes, y el dominio principal de cada red de Marca Blanca. En ese host el borde enruta `/connect/*` y `/.well-known/*` a `core-api` y el resto a `frontend-hub`. El dominio sigue saliendo **solo del sello**, como exige ADR-0060. El emisor (`iss`) es ese host.
4. **Cada producto es un cliente OIDC** (authorization code + PKCE). El token lleva `aud=<producto>`, `dom`, `tenant_id` y los claims actuales de empresa, más **solo los roles y permisos de ese producto**. Vida ≈15 min; refresh rotado y revocable en Redis.
5. **Cada app es un BFF**: cookie de sesión `HttpOnly; Secure; SameSite=Lax` **de su propio host**; el token nunca llega al JavaScript. Se retiran `flit_token` legible y la copia en `localStorage` tras un sprint de convivencia.
6. **SSO por redirección**: la sesión abierta en el hub permite abrir cualquier producto sin volver a escribir la contraseña. Un producto sin sesión envía al usuario al login del hub y lo devuelve.
7. **`DomainContext` gana producto** (`Product`): en hosts FLIT, la raíz o `<ambiente>.<raíz>` es el hub y `<ambiente>.<producto>.<raíz>` es un producto; en redes, lo indica `tenant_domains`.
8. **`admin.tenant_domains` gana `purpose`** (`HUB` o código de producto) y la unicidad pasa a `(tenant_id, purpose)`. Cada host reutiliza la verificación TXT, el job de revalidación y la señal de certificado de ADR-0060.
9. `NetworkUrlBaseResolver` apunta los enlaces de invitación y recuperación al hub de la red del usuario.
10. **Extensiones previstas**: MFA TOTP con política por empresa; Microsoft, Google y SAML como métodos de login del servidor de identidad. Los productos no cambian al añadirlos.

## Alternativas consideradas

### Opción 1: Cookie compartida `Domain=.flitsas.online` con el JWT actual más un token por producto

**Pros:**
- Cambio pequeño y sin componentes nuevos.
- Encaja con `dom = "flit"`, que ya es igual en todos los hosts FLIT.

**Cons:**
- DEV, QA y PDN comparten la raíz `flitsas.online`: la cookie de sesión de PDN viajaría a los servidores de DEV y QA.
- Las redes con varios productos necesitarían que todos sus hosts compartan un dominio base propio.
- Sin camino estándar para clientes futuros (móvil, integradores) ni para federación.

**Esfuerzo:** S  
**Riesgos:** Exposición de sesiones de producción en ambientes de menor control.

### Opción 2: OpenIddict dentro de `core-api`, login en el host sellado de cada red — elegida

**Pros:**
- Respeta D2 y D3 de ADR-0060: el dominio sigue saliendo del sello y el acotamiento por red no cambia.
- Cookies por host: ningún ambiente recibe sesiones de otro.
- Estándar para cualquier cliente futuro; MFA y federación en un solo lugar.
- Sin migrar usuarios ni contraseñas.

**Cons:**
- El equipo construye las pantallas de login, recuperación y MFA.
- Curva de aprendizaje de OpenIddict.
- Varios emisores (el hub de cada ambiente y de cada red) que los productos deben aceptar.

**Esfuerzo:** L  
**Riesgos:** Configuración OIDC incorrecta; se mitiga con espiga técnica y pruebas del flujo completo.

### Opción 3: Keycloak

**Pros:**
- MFA, SAML, proveedores sociales y temas de login listos.

**Cons:**
- Rehace fuera de `core-api` el acotamiento por red, la anti-enumeración y el sello que ADR-0060 acaba de construir y probar.
- Duplica usuarios, empresas y roles, o exige sincronizarlos; migrar Argon2 requiere un proveedor a medida.
- Otro runtime y otra base de datos que operar.

**Esfuerzo:** L  
**Riesgos:** Dos fuentes de verdad de usuarios y roles; regresión en la paridad de Marca Blanca.

## Tradeoff aceptado

Se elige la **Opción 2**. Se acepta construir pantallas de identidad propias a cambio de conservar lo que ADR-0060 ya resolvió y de no exponer sesiones de producción en otros ambientes. Keycloak se reconsidera si la federación empresarial supera lo que el equipo puede mantener.

## Consecuencias

### Lo que se gana

- SSO entre productos; tokens fuera del navegador.
- Cambios de rol o de habilitación de un producto efectivos en minutos.
- Ligadura `dom` con respaldo criptográfico real una vez validada la firma.

### Lo que se pierde

- El bearer directo desde el navegador y el token de 12 h con todos los permisos.

### Cambios operacionales

- Redis obligatorio; llaves de firma rotables publicadas por JWKS; Data Protection compartido entre réplicas.
- El hub de cada ambiente y de cada red sirve también los endpoints OIDC; ninguna cookie se emite con `Domain` de la raíz.
- Suite de paridad de Marca Blanca (#12429) como regresión obligatoria en cada cambio de identidad.

## ADRs relacionados

- `ADR-0060-marca-blanca-identidad-dominio-y-tema-de-correo` — extendido.
- ADR-0061 — estructura de la suite.
- ADR-0063 — la emisión del token aplica la habilitación del producto.

## Notas para agentes

- **Backend Agent**: validar `iss` contra los hosts de identidad registrados y `aud` contra el producto propio. Nunca desactivar la validación de firma. `DomainContext` sigue construyéndose solo desde el sello.
- **Frontend Agent**: usar `packages/auth`; prohibido guardar tokens en `localStorage` o cookies legibles.
- **QA Agent**: SSO entre productos; empresa con el producto apagado; usuario de red entrando por FLIT; refresh y expiración; suite #12429 intacta.
- **Security Agent**: PKCE obligatorio, rotación de refresh, revocación al suspender usuario o empresa.
- **Infra Agent**: `ASPNETCORE_ENVIRONMENT` correcto por ambiente; en el host del hub, `/connect/*` y `/.well-known/*` hacia `core-api`.
