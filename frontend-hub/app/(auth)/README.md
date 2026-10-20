# app/(auth)

Login del hub (tarea **A-06**, HU #12991) sobre el servidor OIDC de core-api (A-05):

| Ruta | Qué hace |
|---|---|
| `/login?returnUrl=…` | `POST /connect/login` abre la sesión del hub (cookie `flit_hub`, HttpOnly) y vuelve a `returnUrl` —normalmente `/connect/authorize` de un producto— o al inicio |
| `/auth/forgot-password` | `POST /api/v1/auth/forgot-password`, respuesta genérica |
| `/auth/reset-password?token=…` | `POST /api/v1/auth/reset-password` |
| `/invite/activate?token=…` | `POST /api/v1/auth/activate` |

Las rutas son las mismas que usan hoy los correos de Trámites: cuando la raíz de cada ambiente pase al hub (A-11),
los enlaces ya enviados siguen funcionando. La marca sale del host (`@flit/brand`). El navegador nunca recibe un
token: los productos lo obtienen por el flujo OIDC en su servidor (`@flit/auth`, A-09).
