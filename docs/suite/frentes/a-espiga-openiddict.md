# Espiga A-04 · OpenIddict sobre el login actual

> **HU #12897** · Feature #12886 · FLIT Suite, área A · 2026-09-25 · Samuel Cardenas
>
> **Código de la espiga:** rama local `spike/AB-12897-openiddict` (commit `c4fae09f`). **No se fusiona**:
> sirvió para responder estas preguntas con evidencia. La implementación real es la A-05.

## Qué se probó

Un prototipo de servidor OIDC con **OpenIddict 7.7.1** dentro de `core-api`, activado por la bandera
`Suite:Oidc:Spike`. Tiene:

- **Endpoints:** `/connect/authorize`, `/connect/token`, `/.well-known/openid-configuration` y `/.well-known/jwks`.
- **Flujos:**
  - authorization code **con PKCE obligatorio**;
  - refresh token;
  - client credentials.
- **Dos clientes:** `tramites` (público, code + PKCE) y `svc-demo` (servicio, scope `platform.manifest`).
- **Login del hub** con cookie de sesión propia (`HttpOnly`, `SameSite=Lax`). Las credenciales las verifica el
  `LoginHandler` actual.
- **Token por producto:** `aud` es el producto pedido. Lleva los roles y permisos de ese producto, que resuelve
  `IProductAccessResolver` (B-05), y dura 15 minutos.

Se probó con cuatro pruebas de punta a punta contra la API real (WebApplicationFactory + PostgreSQL migrado),
todas en verde:

| Prueba | Qué demuestra |
|---|---|
| El emisor sale del host sellado | Con `X-Flit-Domain: dev.flitsas.online` el descubrimiento dice `iss = https://dev.flitsas.online/`; con `qa.flitsas.online`, `https://qa.flitsas.online/` |
| Código con PKCE, token por producto, firmado y validable | Login → authorize → código → token. El token tiene `aud=tramites`, el rol de Trámites del usuario y vence en 15 min. Se valida la firma con las llaves del JWKS. El refresh token funciona |
| Producto apagado | Con `tramites` apagado para la empresa, authorize responde `access_denied` con `PRODUCT_NOT_ENABLED` y **no hay token** |
| Token de servicio | `svc-demo` obtiene por client credentials un token con `sub=svc-demo` y scope `platform.manifest` |

## Las cuatro preguntas

### 1. ¿Dónde guarda OpenIddict sus datos?

**En `FlitDbContext`, schema `identity`, con cuatro tablas:**
- `oidc_applications` (clientes);
- `oidc_authorizations`;
- `oidc_scopes`;
- `oidc_tokens`.

Basta con `modelBuilder.UseOpenIddict<Guid>()` y un `ToTable(..., "identity")` por entidad. `dotnet ef migrations add`
genera una migración normal con esas tablas, sus FK e índices, en snake_case como el resto.

- **Son tablas globales**, sin `tenant_id`: los clientes son productos, no empresas. No llevan RLS.
- **`oidc_tokens` crece** con cada código y refresh token. En la A-05 hace falta una limpieza periódica de tokens y
  autorizaciones vencidos (un job, como los que ya existen).
- **Recomendación:** migración de EF generada. El esquema lo define la librería, y reescribirlo a mano en un DDL
  embebido solo agrega riesgo al actualizarla.

### 2. ¿Se puede tomar el emisor del host sellado?

**Sí.** En OpenIddict el emisor es la URL base de la petición, y los endpoints se reconocen comparando la URL pedida
contra esa base. Un manejador del evento `ProcessRequestContext`, ubicado justo después de `ResolveRequestUri`,
reescribe **las dos** (`BaseUri` y `RequestUri`) con el host de `IDomainContextAccessor`, es decir, del sello
`X-Flit-Domain` que pone el gateway.

- **El dominio sigue saliendo solo del sello,** como exige ADR-0060. Nunca de un parámetro, `Origin` ni `Referer`.
- **Trampa encontrada:** reescribir solo la base rompe el reconocimiento de endpoints (404 en el descubrimiento y
  «sin petición OIDC» en authorize). Hay que reescribir las dos.
- **Para validar tokens,** cada servicio tiene que aceptar la lista de emisores válidos (hubs FLIT por ambiente y
  dominios de red activos). Es el `GET /api/v1/platform/issuers` del contrato §6 (dueño: área A).

### 3. ¿Cómo se reutiliza `LoginHandler`?

**Funciona tal cual:** conserva todas las reglas de hoy.
- hash en tiempo constante y 401 uniforme;
- acceso acotado a la red del dominio;
- `NETWORK_DOMAIN_REQUIRED`;
- suspensión y roles inactivos;
- auditoría de login.

Pero `LoginHandler` hace dos cosas: **verifica** las credenciales y **emite el JWT viejo**. En la espiga el JWT viejo
se emitía y se descartaba.

**Para la A-05:** extraer la verificación a un servicio propio, por ejemplo `CredentialVerifier`, que devuelva el
snapshot del usuario y el dominio.
- `LoginHandler` pasa a ser «verificar + emitir el JWT viejo», sin cambio de comportamiento.
- El login del hub hace «verificar + abrir la sesión OIDC».

Las pruebas actuales de `LoginHandler` cubren el primer camino.

### 4. ¿Cuánto cuesta el JWKS?

**Nada relevante:** 0,30 ms por petición, medido dentro del proceso en tres corridas de 200 peticiones. Además, los
servicios que validan tokens guardan el JWKS en caché (el `ConfigurationManager` de IdentityModel), así que no lo
piden en cada petición.

**Lo que sí importa es la llave de firma.** La espiga usa llaves efímeras, que cambian en cada arranque. En la A-05
hace falta:
- una llave RSA persistente, tomada del almacén de secretos y no del repositorio;
- la misma en todas las instancias;
- rotación publicando dos llaves durante el cambio.

Esto se cruza con A-01/A-03: hoy la API solo valida la firma si tiene configurada su llave pública.

## Otros hallazgos

- **Paquetes.** OpenIddict 7.7.1 exige subir:
  - EF Core y `Microsoft.Extensions.*` de 10.0.10 (y algunos 10.0.0) a 10.0.11;
  - `Microsoft.IdentityModel.*` y `System.IdentityModel.Tokens.Jwt` de 8.2.1 a 8.19.2.

  Son versiones de parche y menores. Van primero, en su propio commit, con la auditoría de dependencias externas
  (regla 18 y reglas R5).
- **Endpoints obligatorios.** OpenIddict 7 no expone el descubrimiento ni el JWKS si no se declaran
  (`SetConfigurationEndpointUris`, `SetJsonWebKeySetEndpointUris`).
- **Token legible.** Hace falta `DisableAccessTokenEncryption()` para que el access token sea un JWT firmado que los
  productos puedan validar; si no, sale cifrado.
- **Producto = cliente.** Que el `client_id` sea el código del producto deja el `aud` resuelto sin mapeos.
- **El acceso se decide al autorizar.** Un producto apagado o un usuario sin rol no reciben código: el contrato §2 se
  cumple en la emisión, no solo en la API.
- **El SuperAdmin** pasa sin resolver acceso y lleva `SuperAdmin` en `role` (contrato §2.1).

## Decisión

**Se adopta OpenIddict 7.x como servidor OIDC dentro de `core-api` (A-05), así:**

1. **Paquetes primero:** subir los de EF, Extensions e IdentityModel a las versiones que pide OpenIddict, con la suite
   completa en verde.
2. **Almacenes:** en `FlitDbContext`, schema `identity`, tablas `oidc_*`, con migración de EF generada. Más un job de
   limpieza de tokens vencidos.
3. **Emisor por host sellado** con el manejador probado aquí, que reescribe base y petición. Emisores válidos
   publicados en `/platform/issuers`.
4. **`CredentialVerifier`** extraído de `LoginHandler` y compartido por el login viejo y el del hub.
5. **Llave de firma persistente** desde secretos, compartida entre instancias y con rotación.
6. **Clientes = productos**, con code + PKCE obligatorio. Servicios con client credentials y scopes (`platform.manifest`,
   `platform.consultas`). Access token de 15 minutos y refresh rotado.

**Se descarta:** Duende IdentityServer (licencia comercial) y un servidor OIDC propio (costo y riesgo de seguridad sin
beneficio frente a OpenIddict).
