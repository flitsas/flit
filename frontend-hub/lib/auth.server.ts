// El hub como cliente de su propio servidor OIDC (A-09): producto `plataforma`. Su sesión da el token para llamar a
// la API (me/apps, administración) sin guardarlo en el navegador.
import "server-only";

import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

export const HUB_PRODUCT = "plataforma";

export const hubAuthRoutes = createAuthRoutes({ productCode: HUB_PRODUCT });

// Las pantallas públicas del hub (recuperación, activación) llaman a la API sin sesión.
export const hubApiProxy = createApiProxy({ productCode: HUB_PRODUCT, allowAnonymous: true });
