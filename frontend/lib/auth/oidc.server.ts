// @flit/auth para Trámites (A-10, HU #13001): cliente OIDC `tramites` del hub.
import "server-only";

import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

export const TRAMITES_PRODUCT = "tramites";

export const tramitesAuthRoutes = createAuthRoutes({ productCode: TRAMITES_PRODUCT, errorPath: "/403" });

// Sin sesión → 401 SESSION_EXPIRED (páginas autenticadas).
export const tramitesApiProxy = createApiProxy({ productCode: TRAMITES_PRODUCT });

// HU #14546 — /api/v1/public/* (captura manual /verificacion, biometría, portal, banners): el token del enlace es la
// credencial; sin cookie OIDC se reenvía igual (como hub allowAnonymous / Comparendos proxy público).
export const tramitesPublicApiProxy = createApiProxy({
  productCode: TRAMITES_PRODUCT,
  allowAnonymous: true,
});
