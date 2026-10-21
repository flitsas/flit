import "server-only";
import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

// Sesión de Diagnóstico (@flit/auth): cliente OIDC público `diagnostico` con PKCE contra el hub. El token que recibe
// la API trae aud=diagnostico, la compañía y los permisos `diagnostico.*` del usuario.
export const DIAGNOSTICO_PRODUCT = "diagnostico";

export const authRoutes = createAuthRoutes({ productCode: DIAGNOSTICO_PRODUCT });
export const apiProxy = createApiProxy({ productCode: DIAGNOSTICO_PRODUCT });
