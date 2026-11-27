import "server-only";
import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

// Sesión de Impuestos (@flit/auth): cliente OIDC público `impuestos` con PKCE contra el hub, cookie
// `flit_session_impuestos`. El token que recibe la API trae aud=impuestos, la compañía y los permisos `impuestos.*`.
export const IMPUESTOS_PRODUCT = "impuestos";

export const authRoutes = createAuthRoutes({ productCode: IMPUESTOS_PRODUCT });
export const apiProxy = createApiProxy({ productCode: IMPUESTOS_PRODUCT });
