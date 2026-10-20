// Marca Blanca con la suite (A-08): en el dominio de una red el emisor OIDC es el hub de ESA red (su dominio con
// purpose HUB), no el de FLIT. Por el hub de FLIT un usuario de la red recibe NETWORK_DOMAIN_REQUIRED y el servidor
// rechaza el retorno a un dominio de red («El redirect_uri no es válido para este cliente»). El hub de la red lo dice
// la API (GET /api/v1/public/network, con el sello del host de la petición): nunca sale del Host tal cual.
import { appOrigin, type AuthConfig } from "./config";

/** Igual que la caché del resolutor de dominios del servidor: un dominio nuevo o retirado se ve en un minuto. */
const TTL_MS = 60_000;

type NetworkHubs = Map<string, { hubUrl: string | null; until: number }>;
// En `globalThis`, como las renovaciones (store.ts): Next empaqueta cada ruta por separado.
const hubs: NetworkHubs = ((globalThis as { __flitAuthNetworkHubs?: NetworkHubs }).__flitAuthNetworkHubs ??= new Map());

/**
 * La configuración con el hub de la red del host de esta petición. Sin la bandera, en el host del propio hub de FLIT o
 * si la API no responde, devuelve la configuración tal cual (el hub de FLIT). Solo cambia `hubUrl`: el canje y la
 * renovación siguen por la red interna y se sellan con el host del hub de la red (tokens.ts), así el emisor del token es
 * el mismo del authorize.
 */
export async function withNetworkHub(request: Request, config: AuthConfig): Promise<AuthConfig> {
  if (!config.networkDomains) return config;
  const host = new URL(appOrigin(request, config)).hostname.toLowerCase();
  if (host === new URL(config.hubUrl).hostname) return config;
  const hubUrl = await networkHub(host, config);
  return hubUrl && hubUrl !== config.hubUrl ? { ...config, hubUrl } : config;
}

async function networkHub(host: string, config: AuthConfig): Promise<string | null> {
  const now = Date.now();
  const cached = hubs.get(host);
  if (cached && cached.until > now) return cached.hubUrl;

  const headers: Record<string, string> = { accept: "application/json", "x-flit-domain": host };
  if (config.internalApiKey) headers["x-internal-key"] = config.internalApiKey;
  try {
    const response = await fetch(`${config.apiOrigin}/api/v1/public/network`, { headers, cache: "no-store" });
    // Un fallo no se recuerda: la siguiente petición vuelve a preguntar.
    if (!response.ok) return null;
    const hubUrl = httpsOrigin(((await response.json()) as { hubUrl?: unknown }).hubUrl);
    hubs.set(host, { hubUrl, until: now + TTL_MS });
    return hubUrl;
  } catch {
    return null;
  }
}

/** Solo un origen https: es lo único que el servidor OIDC acepta en un dominio de red. */
function httpsOrigin(value: unknown): string | null {
  if (typeof value !== "string") return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.origin : null;
  } catch {
    return null;
  }
}

/** Solo para pruebas: olvida los hubs recordados. */
export function forgetNetworkHubs(): void {
  hubs.clear();
}
