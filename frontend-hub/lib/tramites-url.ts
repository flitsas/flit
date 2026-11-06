// URL de Trámites para el host de la petición. La administración de plataforma sigue en Trámites (hasta B-12) y el hub la
// enlaza: en el dominio de una red de Marca Blanca tiene que ser el Trámites de esa red, porque en el de FLIT su usuario
// no puede iniciar sesión (NETWORK_DOMAIN_REQUIRED). Sin server-only: también la usa el middleware.
import { networkTramitesUrl } from "@flit/auth/network";

/** El Trámites de la red del `host`, o `fallback` (TRAMITES_URL) en FLIT, en una red sin ese dominio o si la API no responde. */
export async function resolveTramitesUrl(host: string | null, fallback: string, env: NodeJS.ProcessEnv = process.env): Promise<string> {
  const network = await networkTramitesUrl(host, {
    apiOrigin: (env.CORE_API_ORIGIN || "http://localhost:4002").replace(/\/+$/, ""),
    internalApiKey: env.FLIT_INTERNAL_API_KEY || undefined,
    networkDomains: env.FLIT_NETWORK_DOMAINS_ENABLED === "true",
  });
  return network ?? fallback;
}
