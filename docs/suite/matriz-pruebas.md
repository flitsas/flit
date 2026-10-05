# Matriz de pruebas de la suite

Feature #13221 (Epic #13217). Lo que hay que probar antes de encender la suite en un ambiente, sobre la forma final
(identidad aparte). Se prueba en local con `127.0.0.1` ([local.md](local.md), sección 4) y después en DEV. La columna «Local» de A y B se recorrió
en el navegador con la topología real: hub y Trámites → gateway (bandera encendida) → core-identity / core-api.

Estado: **OK** probado y funciona · **Falla** probado y no funciona (ver hallazgo) · **—** sin probar.

## A. Los 9 casos base

| # | Caso | Local (identidad aparte) | DEV |
|---|---|---|---|
| A1 | Hub sin sesión → portada | OK (2026-10-01, core-identity aparte + gateway) | — |
| A2 | Login con un solo producto → entra directo | OK (2026-10-01, core-identity aparte + gateway) | — |
| A3 | Inicio del hub con `?inicio=1` sin volver a iniciar sesión | OK (2026-10-01, core-identity aparte + gateway) | — |
| A4 | Abrir Trámites sin sesión → login del hub → vuelve a Trámites | OK (2026-10-01, core-identity aparte + gateway) | — |
| A5 | Con sesión en el hub, abrir Trámites → entra sin contraseña | OK (2026-10-01, core-identity aparte + gateway) | — |
| A6 | Producto apagado → 403 «Tu empresa no tiene Trámites» → «Ir a mis productos» | OK (2026-10-01, core-identity aparte + gateway) | — |
| A7 | AdminCompany ve «Administración» y «Usuarios» | OK (2026-10-01, core-identity aparte + gateway) | — |
| A8 | Cerrar sesión en Trámites o en el hub cierra toda la suite | OK (2026-10-01, core-identity aparte + gateway) | — |
| A9 | SuperAdmin con varios productos → una tarjeta por producto | OK (2026-10-01, core-identity aparte + gateway) | — |

## B. Identidad aparte

| # | Caso | Local | DEV |
|---|---|---|---|
| B1 | `core-api` apagado → login, autorización y token funcionan; Trámites abre sin datos | OK (2026-10-01, core-identity aparte + gateway) | — |
| B2 | `core-api` vuelve → los datos aparecen sin volver a iniciar sesión | OK (2026-10-01, core-identity aparte + gateway) | — |
| B3 | `core-identity` apagado con la bandera encendida → el gateway pasa el login a `core-api` en segundos | OK (2026-10-01, el login vuelve por core-api en ~3 s) | — |
| B4 | Bandera del gateway apagada → todo como antes (vuelta atrás) | OK (2026-10-01, core-identity no recibió peticiones) | — |
| B5 | Desplegar `core-api` (por servicio) con gente dentro → el login no se cae | — | — |
| B6 | Login de siempre (`FLIT_SESSION_MODE=legacy`) con la bandera encendida | — | — |

## C. Sesión y tiempo

| # | Caso | Local | DEV |
|---|---|---|---|
| C1 | El token vence (15 min) y se renueva solo en la siguiente acción | — | — |
| C2 | Sesión abierta de un día para otro | — | — |
| C3 | Varias pestañas; cerrar sesión en una | — | — |
| C4 | Dos navegadores con el mismo usuario; cerrar sesión en uno no cierra el otro | — | — |
| C5 | **Cambiar de usuario en el mismo navegador** | Falla (H1) | — |

## D. Cambios con la persona dentro

| # | Caso | Local | DEV |
|---|---|---|---|
| D1 | Apagar Trámites a la empresa con usuarios conectados (≤ 30 s en la API, ≤ 15 min en el token) | — | — |
| D2 | Cambiar el rol o los permisos de un usuario conectado | — | — |
| D3 | Desactivar o suspender un usuario conectado | — | — |
| D4 | SuperAdmin cambia de empresa | — | — |

## E. Usuarios y casos

| # | Caso | Local | DEV |
|---|---|---|---|
| E1 | Admin OT (`otadmin@flit.local`) | — | — |
| E2 | Revisor y gestor de Trámites | — | — |
| E3 | Marca Blanca: login con la marca del dominio | — | — |
| E4 | Recuperar contraseña: el correo lleva al hub del ambiente | — | — |
| E5 | Activar una invitación | — | — |
| E6 | Celular (ancho de teléfono) | — | — |

## F. Fallas provocadas

| # | Caso | Local | DEV |
|---|---|---|---|
| F1 | Cambiar `FLIT_SESSION_SECRET` → todos vuelven a iniciar sesión, sin bucles | — | — |
| F2 | Cookies grandes en el mismo dominio | — | — |
| F3 | El hub cae justo al volver del login | — | — |
| F4 | Postgres no responde → `/health/ready` en 503 | — | — |

## G. Apagada

| # | Caso | Local | DEV |
|---|---|---|---|
| G1 | Todas las banderas apagadas: Trámites con su login de siempre, nadie nota nada | — | — |

## Hallazgos

| # | Caso | Qué pasa | Estado |
|---|---|---|---|
| H1 | C5 | Tras iniciar sesión como otro usuario (pasando por Trámites), el hub sigue mostrando al usuario anterior. Causa: la sesión de un producto (cookie de 14 días con su refresh) vive más que la sesión del hub (`flit_hub`, 12 h). Cuando `flit_hub` vence, el cierre de sesión ya no conoce las autorizaciones viejas y no las revoca. Independiente de la separación de identidad. Se resuelve con la decisión de duración de sesión (Feature #13221): que la sesión de un producto no dure más que la del hub. | Abierto |
| H2 | F (API caída) | Con `core-api` apagado, Trámites abre con la sesión pero sus pantallas salen en rojo con errores sueltos. Falta una pantalla clara de «Trámites no disponible». | Abierto |
| H3 | Cambio de usuario (2026-10-02) | Al salir desde una app y entrar con otro usuario, la otra app conservaba su cookie de la sesión cerrada: Trámites mostraba «Tu sesión expiró» apenas el hub lo abría, y el inicio del hub, con el token vencido, decía «Tu empresa todavía no tiene productos habilitados» con el usuario anterior. Corregido: ante un 401, la app pide una sesión nueva al hub sin mostrar nada (`reauthenticate` de `@flit/auth/client`; si vuelve a fallar en menos de 10 s —un bucle—, sí muestra el aviso), y el hub y el menú ▦ ya no confunden «no pude cargar» con «no hay productos». Probado en el navegador en los dos sentidos (SuperAdmin ↔ admin empresa). | Corregido |
| H4 | Renovación del token (2026-10-02) | Cuando a un token le quedaba menos de un minuto, cada llamada simultánea de la página lo renovaba con el mismo refresh; el servidor tomaba el segundo uso como robo y revocaba toda la sesión («Tu sesión expiró» al azar, cada 15 min). Visto en el log: «16 tokens … were revoked to prevent a potential token replay attack». Corregido: una sola renovación por refresh token en cada proceso del front (`renewOnce`, `packages/auth/src/store.ts`), reusada 30 s. Probado con tokens de 1 minuto: varias llamadas comparten la renovación y ninguna da 401. Si un front llega a correr en más de una réplica, subir `Suite:Oidc:RefreshTokenReuseLeewaySeconds`. | Corregido |
| H5 | Comparendos y Diagnóstico (2026-10-02) | Sus tarjetas llevaban a hosts que todavía no existen. Ahora llevan a `/proximamente/<código>` del hub (`Suite:Hosts:ComingSoon`); su cliente de login conserva su host real. El producto `demo` se retiró del catálogo (migración `20261002120000_Suite_RetirarProductoDemo`). | Corregido |
| H6 | Barra del hub (2026-10-02) | En el hub el menú de usuario solo traía «Salir», el rol salía como código («SuperAdmin») y los botones no mostraban la mano. Corregido: mismas opciones y rótulo que Trámites, y el puntero de la suite (`@flit/ui/cursor.css`) en todas las apps. | Corregido |
| H7 | Encender un producto (2026-10-02) | Encender Comparendos para una empresa no servía: (1) la pantalla mostraba el estado de la configuración de la compañía, no el de `platform.tenant_products` (sin configuración guardada salía todo apagado); (2) Comparendos y Diagnóstico no tenían ningún rol, y para ver un producto hace falta un rol en él, así que solo el SuperAdmin los veía; (3) Diagnóstico no tenía interruptor. Corregido: pestaña «Productos» en la consola de la compañía (estado real, los tres productos, quién y cuándo, apagar con confirmación); roles `admin_comparendos` y `admin_diagnostico` y el Admin de Compañía recibe el admin de cada producto (DDL 126). Probado en el navegador con Empresa Demo S.A.S (sin configuración): encender Diagnóstico y apagar Comparendos como SuperAdmin, y admin empresa ve exactamente Trámites y Diagnóstico. | Corregido |
| H8 | Entrar a Trámites tras salir desde el hub (2026-10-05) | Al salir desde el hub, la cookie de Trámites quedaba con la sesión revocada; al volver a entrar, sus primeras 5 llamadas daban 401, la página se recuperaba sola y algunas tarjetas pintaban «Tu sesión expiró» un instante. Reproducido 3 de 3; saliendo desde Trámites (0 de 3), navegando (0 en 8 idas y vueltas) o con tokens de 1 minuto (12 renovaciones) no pasaba. Corregido: (1) front-channel logout: al cerrar la sesión del hub, la página de cierre abre `/auth/frontchannel-logout` de cada producto que inició sesión desde ella y cada uno borra su cookie al instante; (2) si una sesión igual queda revocada (cerrada desde otro lado), Trámites muestra «Reconectando tu sesión…» y no lanza más llamadas: 1 solo 401 en vez de 5, sin errores en pantalla. Repetido en el navegador: salir desde el hub y volver a entrar, 0 errores 401. | Corregido |
