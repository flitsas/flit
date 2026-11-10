import "server-only";
import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

// Sesión de la caja del organismo (ADR-0082, opción A): cliente OIDC público `caja` con code + PKCE contra el hub. El token
// que recibe el BFF trae aud=caja y el rol ot_caja. Solo las rutas de la caja llevan esta sesión; el resto del portal sigue
// anónimo (cookies pn_sesion / pn_punto).
export const CAJA_PRODUCT = "caja";

export const authRoutes = createAuthRoutes({ productCode: CAJA_PRODUCT });

/** Reenvía `/api/v1/persona-natural/caja/*` al BFF con el Bearer de la sesión (renovado si está por vencer). */
export const cajaApiProxy = createApiProxy({
  productCode: CAJA_PRODUCT,
  prefix: "/api/v1/persona-natural/caja",
  // 5 MB de prefactura + el sobre multipart.
  maxBodyBytes: 6 * 1024 * 1024,
});
