# Matriz de pruebas de la suite

Feature #13221 (Epic #13217). Lo que hay que probar antes de encender la suite en un ambiente, sobre la forma final
(identidad aparte). Se prueba en local con `127.0.0.1` ([local.md](local.md), sección 4) y después en DEV.

Estado: **OK** probado y funciona · **Falla** probado y no funciona (ver hallazgo) · **—** sin probar.

## A. Los 9 casos base

| # | Caso | Local (identidad aparte) | DEV |
|---|---|---|---|
| A1 | Hub sin sesión → portada | — | — |
| A2 | Login con un solo producto → entra directo | OK (2026-10-01) | — |
| A3 | Inicio del hub con `?inicio=1` sin volver a iniciar sesión | — | — |
| A4 | Abrir Trámites sin sesión → login del hub → vuelve a Trámites | OK (2026-10-01) | — |
| A5 | Con sesión en el hub, abrir Trámites → entra sin contraseña | — | — |
| A6 | Producto apagado → 403 «Tu empresa no tiene Trámites» → «Ir a mis productos» | — | — |
| A7 | AdminCompany ve «Administración» y «Usuarios» | OK (2026-10-01) | — |
| A8 | Cerrar sesión en Trámites o en el hub cierra toda la suite | — | — |
| A9 | SuperAdmin con varios productos → una tarjeta por producto | — | — |

## B. Identidad aparte

| # | Caso | Local | DEV |
|---|---|---|---|
| B1 | `core-api` apagado → login, autorización y token funcionan; Trámites abre sin datos | OK (2026-10-01) | — |
| B2 | `core-api` vuelve → los datos aparecen sin volver a iniciar sesión | OK (2026-10-01) | — |
| B3 | `core-identity` apagado con la bandera encendida → el gateway pasa el login a `core-api` en segundos | OK (2026-10-01, gateway real: el login vuelve en ~3 s con chequeo cada 2 s) | — |
| B4 | Bandera del gateway apagada → todo como antes (vuelta atrás) | — | — |
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
