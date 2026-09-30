// Proxy /api/v1/* de una app hacia el gateway con el Bearer de la sesión (contrato §8, createApiProxy). El navegador
// llama a su propio host; el token lo pone el servidor y se renueva aquí si está por vencer.
import { authConfig, type AuthConfig } from "./config";
import { clearSessionCookies, freshSession } from "./store";

const DROPPED_REQUEST = new Set([
  "host", "connection", "keep-alive", "transfer-encoding", "upgrade", "content-length",
  // La sesión y los sellos solo los pone este servidor.
  "cookie", "authorization", "x-flit-domain", "x-internal-key",
]);
const DROPPED_RESPONSE = new Set(["content-encoding", "content-length", "transfer-encoding", "connection"]);

export interface ApiProxyOptions {
  productCode: string;
  /** Prefijo del lado de la API; por defecto /api/v1. */
  prefix?: string;
  /**
   * Sin sesión, reenviar igual sin Bearer (la API decide). Para apps con pantallas públicas que llaman a la API, como
   * la recuperación de contraseña del hub. Por defecto, sin sesión responde 401 SESSION_EXPIRED sin llamar a la API.
   */
  allowAnonymous?: boolean;
  config?: () => AuthConfig;
}

/** Handler de una ruta catch-all: `(request, path) => Response`, con los segmentos capturados. */
export function createApiProxy(options: ApiProxyOptions): (request: Request, path: string[]) => Promise<Response> {
  const prefix = options.prefix ?? "/api/v1";
  const config = options.config ?? (() => authConfig(options.productCode));

  return async (request, path) => {
    const cfg = config();
    const { session, setCookies } = await freshSession(request, cfg);
    if (!session && !options.allowAnonymous) {
      // El frontend ya reconoce SESSION_EXPIRED y lleva al login.
      return withCookies(Response.json({ code: "SESSION_EXPIRED", message: "La sesión venció." }, { status: 401 }), setCookies);
    }

    const incoming = new URL(request.url);
    const headers = new Headers();
    request.headers.forEach((value, name) => {
      if (!DROPPED_REQUEST.has(name.toLowerCase())) headers.set(name, value);
    });
    if (session) headers.set("authorization", `Bearer ${session.accessToken}`);
    const host = request.headers.get("host");
    if (host) headers.set("x-flit-domain", host.split(":")[0].toLowerCase());
    if (cfg.internalApiKey) headers.set("x-internal-key", cfg.internalApiKey);

    const hasBody = request.method !== "GET" && request.method !== "HEAD";
    let upstream: Response;
    try {
      upstream = await fetch(`${cfg.apiOrigin}${prefix}/${path.map(encodeURIComponent).join("/")}${incoming.search}`, {
        method: request.method,
        headers,
        body: hasBody ? await request.arrayBuffer() : undefined,
        redirect: "manual",
        cache: "no-store",
      });
    } catch {
      return withCookies(Response.json({ code: "API_UNAVAILABLE", message: "No fue posible contactar la API." }, { status: 502 }), setCookies);
    }

    const responseHeaders = new Headers();
    upstream.headers.forEach((value, name) => {
      if (!DROPPED_RESPONSE.has(name.toLowerCase())) responseHeaders.append(name, value);
    });
    // La API rechazó el token de la sesión (cerrada en otro producto o en el hub, usuario suspendido): la sesión de esta
    // app ya no sirve y se borra, así la siguiente página va al login en lugar de mostrarse con una sesión muerta.
    const dead = session && upstream.status === 401 ? clearSessionCookies(request, cfg) : [];
    return withCookies(new Response(upstream.body, { status: upstream.status, statusText: upstream.statusText, headers: responseHeaders }), [...setCookies, ...dead]);
  };
}

function withCookies(response: Response, cookies: string[]): Response {
  for (const cookie of cookies) response.headers.append("set-cookie", cookie);
  return response;
}
