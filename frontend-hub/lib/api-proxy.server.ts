// Proxy del hub hacia el gateway (B-09), resuelto en runtime con CORE_API_ORIGIN: /api/v1/* y, desde A-05, el
// servidor OIDC (/connect/* y /.well-known/*), que vive en core-api pero se sirve en el host del hub.
//
// Sella el dominio: descarta X-Flit-Domain y X-Internal-Key que mande el navegador y pone el host real de la
// petición con la clave interna, igual que la resolución de marca. Así el gateway (DomainSealTransform) confía
// en el sello y la API sabe en qué dominio y producto está la petición (ADR-0060 D2, B-08).
import "server-only";

import type { HubConfig } from "./config.server";

/** Cabeceras que no se reenvían: hop-by-hop, las que fija fetch y las que solo el servidor puede poner. */
const DROPPED_REQUEST_HEADERS = new Set([
  "host",
  "connection",
  "keep-alive",
  "transfer-encoding",
  "upgrade",
  "content-length",
  "x-flit-domain",
  "x-internal-key",
]);

/** fetch ya descomprime el cuerpo: reenviar estas cabeceras haría que el navegador lo intente de nuevo. */
const DROPPED_RESPONSE_HEADERS = new Set(["content-encoding", "content-length", "transfer-encoding", "connection"]);

/**
 * @param prefix ruta fija del lado de la API (`/api/v1`, `/connect`, `/.well-known`).
 * @param path segmentos que capturó la ruta del hub; se codifican uno por uno.
 */
export async function proxyToApi(request: Request, prefix: string, path: string[], config: HubConfig): Promise<Response> {
  const incoming = new URL(request.url);
  const target = `${config.apiOrigin}${prefix}/${path.map(encodeURIComponent).join("/")}${incoming.search}`;

  const headers = new Headers();
  request.headers.forEach((value, name) => {
    if (!DROPPED_REQUEST_HEADERS.has(name.toLowerCase())) headers.set(name, value);
  });
  const host = request.headers.get("host");
  if (host) {
    headers.set("x-flit-domain", host.split(":")[0].toLowerCase());
    headers.set("x-forwarded-host", host);
  }
  if (config.internalApiKey) headers.set("x-internal-key", config.internalApiKey);

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers,
      body: hasBody ? await request.arrayBuffer() : undefined,
      redirect: "manual",
      cache: "no-store",
    });
  } catch {
    return Response.json({ code: "API_UNAVAILABLE", message: "No fue posible contactar la API." }, { status: 502 });
  }

  const responseHeaders = new Headers();
  upstream.headers.forEach((value, name) => {
    if (!DROPPED_RESPONSE_HEADERS.has(name.toLowerCase())) responseHeaders.append(name, value);
  });
  return new Response(upstream.body, { status: upstream.status, statusText: upstream.statusText, headers: responseHeaders });
}
