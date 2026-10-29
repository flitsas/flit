// Proxy del navegador hacia el BFF del portal (HU #14052). Hace tres cosas que el navegador no debe hacer solo:
//  1. Guarda el token de la sesión del trámite en una cookie HttpOnly y lo quita de la respuesta, así ningún JavaScript
//     (ni una extensión, ni un XSS) puede leerlo.
//  2. Lo adjunta como `Authorization: Bearer` al reenviar.
//  3. Pasa la IP real del ciudadano en X-Forwarded-For para el límite por IP del BFF.
// Sin dependencias de Next: recibe y devuelve objetos planos para poder probarlo con un fetch falso.

export const COOKIE_SESION = "pn_sesion";
export const PREFIJO_BFF = "/api/v1/persona-natural";

/** Cabeceras del navegador que sí se reenvían al BFF. Todo lo demás (cookies, Host, Authorization) se descarta. */
const CABECERAS_PERMITIDAS = ["content-type", "accept", "x-captcha-token", "idempotency-key", "x-correlation-id"];

/** Contenido público del portal (HU #14073): anónimo, sin sesión; la imagen de los banners se revalida con ETag. */
function esConfiguracionPublica(path: string[]): boolean {
  return path[0] === "configuracion";
}

export interface OpcionesProxy {
  apiOrigin: string;
  tokenSesion?: string;
  ipCliente?: string;
  fetchImpl?: typeof fetch;
}

export type CookieOrden = { accion: "fijar"; valor: string; expiraEn: Date } | { accion: "borrar" };

export interface ResultadoProxy {
  status: number;
  headers: Headers;
  cuerpo: BodyInit | null;
  cookie?: CookieOrden;
}

/** Los segmentos de la ruta no pueden salirse del prefijo del BFF. */
export function rutaSegura(path: string[]): boolean {
  return path.length > 0 && path.every((s) => s.length > 0 && s !== "." && s !== ".." && !/[\\/?#%]/.test(s));
}

export async function reenviarAlBff(request: Request, path: string[], opciones: OpcionesProxy): Promise<ResultadoProxy> {
  if (!rutaSegura(path)) return problema(400, "ruta_invalida", "Ruta inválida");

  const destino = new URL(`${opciones.apiOrigin}${PREFIJO_BFF}/${path.map(encodeURIComponent).join("/")}`);
  destino.search = new URL(request.url).search;

  const cabeceras = new Headers();
  for (const nombre of CABECERAS_PERMITIDAS) {
    const valor = request.headers.get(nombre);
    if (valor) cabeceras.set(nombre, valor);
  }
  const publica = esConfiguracionPublica(path);
  if (publica) {
    const condicional = request.headers.get("if-none-match");
    if (condicional) cabeceras.set("if-none-match", condicional);
  }
  // La configuración pública es anónima: no lleva el token de la sesión del trámite.
  if (opciones.tokenSesion && !publica) cabeceras.set("authorization", `Bearer ${opciones.tokenSesion}`);
  if (opciones.ipCliente) cabeceras.set("x-forwarded-for", opciones.ipCliente);

  const tieneCuerpo = request.method !== "GET" && request.method !== "HEAD";
  let respuesta: Response;
  try {
    respuesta = await (opciones.fetchImpl ?? fetch)(destino, {
      method: request.method,
      headers: cabeceras,
      body: tieneCuerpo ? await request.arrayBuffer() : undefined,
      redirect: "manual",
      cache: "no-store",
    });
  } catch {
    return problema(503, "servicio_no_disponible", "El servicio no está disponible");
  }

  const headers = new Headers();
  const reenviables = ["content-type", "retry-after", "content-disposition", ...(publica ? ["etag", "cache-control"] : [])];
  for (const nombre of reenviables) {
    const valor = respuesta.headers.get(nombre);
    if (valor) headers.set(nombre, valor);
  }

  const esSesionNueva = respuesta.ok && /^sesiones(\/reanudar)?$/.test(path.join("/")) && request.method === "POST";
  if (esSesionNueva) return sesionNueva(respuesta, headers);

  const sesionPerdida = respuesta.status === 401 && (await codigoDe(respuesta)) !== null;
  return {
    status: respuesta.status,
    headers,
    cuerpo: respuesta.status === 204 || respuesta.status === 304 ? null : await respuesta.arrayBuffer(),
    cookie: sesionPerdida ? { accion: "borrar" } : undefined,
  };
}

async function sesionNueva(respuesta: Response, headers: Headers): Promise<ResultadoProxy> {
  const datos = (await respuesta.json().catch(() => null)) as Record<string, unknown> | null;
  const token = typeof datos?.token === "string" ? datos.token : null;
  if (!datos || !token) return problema(502, "respuesta_invalida", "Respuesta inválida del servicio");

  const { token: _token, ...sinToken } = datos;
  void _token;
  const expira = typeof datos.expiraEn === "string" ? new Date(datos.expiraEn) : new Date(Date.now() + 60 * 60 * 1000);
  headers.set("content-type", "application/json");
  return {
    status: respuesta.status,
    headers,
    cuerpo: JSON.stringify(sinToken),
    cookie: { accion: "fijar", valor: token, expiraEn: Number.isNaN(expira.getTime()) ? new Date(Date.now() + 60 * 60 * 1000) : expira },
  };
}

/** Código estable de un problem+json de sesión (`session_expired`, `session_invalid`), o null. */
async function codigoDe(respuesta: Response): Promise<string | null> {
  const datos = (await respuesta.clone().json().catch(() => null)) as { code?: unknown } | null;
  return typeof datos?.code === "string" && datos.code.startsWith("session_") ? datos.code : null;
}

function problema(status: number, code: string, title: string): ResultadoProxy {
  return {
    status,
    headers: new Headers({ "content-type": "application/problem+json" }),
    cuerpo: JSON.stringify({ type: "about:blank", title, status, code }),
  };
}
