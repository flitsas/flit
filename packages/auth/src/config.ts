// Configuración de @flit/auth, leída en el SERVIDOR en cada petición (una sola imagen por app para los tres
// ambientes, sin NEXT_PUBLIC_*).

export interface AuthConfig {
  /** Código del producto = client_id OIDC = aud del token. */
  productCode: string;
  /** URL pública del hub (emisor OIDC) a la que va el navegador: https://dev.flitsas.online. */
  hubUrl: string;
  /**
   * Todos los hubs del ambiente: el de FLIT_HUB_URL y los de FLIT_HUB_URLS (raíces alternativas, PDN:
   * https://app.flitsas.com). Lista cerrada: `forRequest` elige de aquí, nunca del Host de la petición tal cual.
   */
  hubUrls: string[];
  /** Adónde llama el servidor para canjear y renovar tokens (red interna); por defecto, el hub. */
  oidcInternalUrl: string;
  /** Destino del proxy /api/v1/* (el gateway). */
  apiOrigin: string;
  /** Clave para cifrar la cookie de sesión. Mínimo 32 caracteres. */
  sessionSecret: string;
  /** Clave compartida con el gateway para que acepte el sello X-Flit-Domain de este servidor. */
  internalApiKey: string | undefined;
  /** URL pública de esta app; si falta, sale del Host de la petición. */
  appUrl: string | undefined;
}

const DEV_SESSION_SECRET = "flit-dev-session-secret-solo-para-next-dev";

export function authConfig(productCode: string, env: NodeJS.ProcessEnv = process.env): AuthConfig {
  const hubUrl = trimSlash(env.FLIT_HUB_URL || "http://127.0.0.1:4022");
  // En `next dev` sin la variable se usa una clave fija de desarrollo; en un build de producción es obligatoria.
  const sessionSecret = env.FLIT_SESSION_SECRET || (env.NODE_ENV === "development" ? DEV_SESSION_SECRET : "");
  if (sessionSecret.length < 32) {
    throw new Error("FLIT_SESSION_SECRET debe tener al menos 32 caracteres para cifrar la sesión.");
  }
  const hubUrls = [hubUrl];
  for (const url of (env.FLIT_HUB_URLS ?? "").split(",")) {
    const trimmed = trimSlash(url.trim());
    if (trimmed && !hubUrls.includes(trimmed)) hubUrls.push(trimmed);
  }
  return {
    productCode,
    hubUrl,
    hubUrls,
    oidcInternalUrl: trimSlash(env.FLIT_OIDC_INTERNAL_URL || hubUrl),
    apiOrigin: trimSlash(env.CORE_API_ORIGIN || "http://localhost:4002"),
    sessionSecret,
    internalApiKey: env.FLIT_INTERNAL_API_KEY || undefined,
    appUrl: env.FLIT_APP_URL ? trimSlash(env.FLIT_APP_URL) : undefined,
  };
}

/** Origen público de la app para esta petición: FLIT_APP_URL o el Host (y el protocolo) que vio el borde. */
export function appOrigin(request: Request, config: AuthConfig): string {
  if (config.appUrl) return config.appUrl;
  const url = new URL(request.url);
  const host = request.headers.get("x-forwarded-host") ?? request.headers.get("host") ?? url.host;
  const proto = request.headers.get("x-forwarded-proto") ?? url.protocol.replace(":", "");
  return `${proto}://${host}`;
}

/**
 * La configuración con el hub de la raíz de esta petición (PDN: quien entra por tramites.flitsas.com usa
 * https://app.flitsas.com; quien entra por tramites.flitsas.online, https://flitsas.online). Así el authorize, el canje
 * del código, la renovación y el cierre de sesión van al mismo emisor y el usuario no cambia de dominio. Con un solo
 * hub (DEV, QA, local) devuelve la configuración tal cual.
 */
export function forRequest(request: Request, config: AuthConfig): AuthConfig {
  if (config.hubUrls.length < 2) return config;
  const hubUrl = hubUrlFor(new URL(appOrigin(request, config)).hostname, config);
  return hubUrl === config.hubUrl ? config : { ...config, hubUrl };
}

/** Hub de `hostname` entre `config.hubUrls`: el de la misma raíz (los dos últimos nombres); si ninguno, FLIT_HUB_URL. */
export function hubUrlFor(hostname: string, config: Pick<AuthConfig, "hubUrl" | "hubUrls">): string {
  const root = rootOf(hostname);
  return config.hubUrls.find((url) => rootOf(new URL(url).hostname) === root) ?? config.hubUrl;
}

function rootOf(hostname: string): string {
  return hostname.toLowerCase().replace(/\.$/, "").split(".").slice(-2).join(".");
}

function trimSlash(url: string): string {
  return url.replace(/\/+$/, "");
}
