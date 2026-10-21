import "server-only";
import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

// Sesión de Comparendos (@flit/auth): cliente OIDC público `comparendos` con PKCE contra el hub. El token que recibe
// la API trae aud=comparendos, la empresa y los permisos `comparendos.*` del usuario.
export const COMPARENDOS_PRODUCT = "comparendos";

export const authRoutes = createAuthRoutes({ productCode: COMPARENDOS_PRODUCT });
export const apiProxy = createApiProxy({ productCode: COMPARENDOS_PRODUCT });
