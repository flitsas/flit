// @flit/auth para Trámites (A-10, HU #13001): cliente OIDC `tramites` del hub.
import "server-only";

import { createApiProxy, createAuthRoutes } from "@flit/auth/server";

export const TRAMITES_PRODUCT = "tramites";

export const tramitesAuthRoutes = createAuthRoutes({ productCode: TRAMITES_PRODUCT, errorPath: "/403" });

export const tramitesApiProxy = createApiProxy({ productCode: TRAMITES_PRODUCT });
