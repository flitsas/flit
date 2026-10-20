// Productos del usuario leídos en el servidor antes de pintar (B-11), con el token de la sesión del hub.
import "server-only";

import { getAccessToken } from "@flit/auth/server";
import type { SuiteApp } from "@flit/shell/apps";
import { HUB_PRODUCT } from "./auth.server";
import type { HubConfig } from "./config.server";

/**
 * `null` si no se pudieron leer (token por vencer o API caída): la página los pide desde el navegador.
 * `"unauthorized"` si la API rechazó el token: la sesión se cerró en otro producto o en el hub.
 */
export async function fetchMyAppsOnServer(config: HubConfig, host: string | null): Promise<SuiteApp[] | null | "unauthorized"> {
  const token = await getAccessToken(HUB_PRODUCT);
  if (!token) return null;

  const headers: Record<string, string> = { authorization: `Bearer ${token}`, accept: "application/json" };
  if (host) headers["x-flit-domain"] = host.split(":")[0].toLowerCase();
  if (config.internalApiKey) headers["x-internal-key"] = config.internalApiKey;
  try {
    const response = await fetch(`${config.apiOrigin}/api/v1/platform/me/apps`, { headers, cache: "no-store" });
    if (response.status === 401) return "unauthorized";
    return response.ok ? ((await response.json()) as SuiteApp[]) : null;
  } catch {
    return null;
  }
}
