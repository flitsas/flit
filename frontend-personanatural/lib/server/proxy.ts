// Proxy del navegador hacia el BFF del portal (HU #14052). Hace tres cosas que el navegador no debe hacer solo:
//  1. Guarda el token de la sesión del trámite en una cookie HttpOnly y lo quita de la respuesta, así ningún JavaScript
//     (ni una extensión, ni un XSS) puede leerlo.
//  2. Lo adjunta como `Authorization: Bearer` al reenviar.
//  3. Pasa la IP real del ciudadano en X-Forwarded-For para el límite por IP del BFF.
// Sin dependencias de Next: recibe y devuelve objetos planos para poder probarlo con un fetch falso.

export const COOKIE_SESION = "pn_sesion";
/** Sesión del canal del punto. El BFF la devuelve como `token` en el JSON o como cookie `pn_punto`. */
export const COOKIE_PUNTO = "pn_punto";
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
  /** Sesión del canal del punto. En rutas `/punto/*` reemplaza al token del trámite. */
  tokenPunto?: string;
  ipCliente?: string;
  fetchImpl?: typeof fetch;
}

export type CookieOrden = { accion: "fijar"; valor: string; expiraEn: Date } | { accion: "borrar" };

export interface ResultadoProxy {
  status: number;
  headers: Headers;
  cuerpo: BodyInit | null;
  cookie?: CookieOrden;
  /** Cookie HttpOnly del canal. No se mezcla con `pn_sesion`. */
  cookiePunto?: CookieOrden;
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
  const punto = esCanalPunto(path);
  if (publica) {
    const condicional = request.headers.get("if-none-match");
    if (condicional) cabeceras.set("if-none-match", condicional);
  }
  // La configuración pública es anónima: no lleva el token de la sesión del trámite.
  // El canal no usa Bearer: el BFF lee la cookie pn_punto (valor protegido) y la sesión del trámite no se mezcla.
  const token = publica || punto ? undefined : opciones.tokenSesion;
  if (token) cabeceras.set("authorization", `Bearer ${token}`);
  if (punto && opciones.tokenPunto) cabeceras.set("cookie", `${COOKIE_PUNTO}=${opciones.tokenPunto}`);
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

  if (respuesta.ok && request.method === "POST" && path.join("/") === "punto/entrada") {
    return sesionDePunto(respuesta, headers);
  }

  const codigo = respuesta.status === 401 ? await codigoDe(respuesta) : null;
  const sesionPerdida = !punto && codigo !== null;
  const puntoPerdido = punto && respuesta.status === 401;
  return {
    status: respuesta.status,
    headers,
    cuerpo: respuesta.status === 204 || respuesta.status === 304 ? null : await respuesta.arrayBuffer(),
    cookie: sesionPerdida ? { accion: "borrar" } : undefined,
    cookiePunto: puntoPerdido ? { accion: "borrar" } : undefined,
  };
}

function esCanalPunto(path: string[]): boolean {
  return path[0] === "punto";
}

/** Cookie `pn_punto` del BFF, si vino en Set-Cookie. */
function tokenDeCookiePunto(respuesta: Response): { valor: string; expiraEn: Date } | null {
  const lista = typeof respuesta.headers.getSetCookie === "function" ? respuesta.headers.getSetCookie() : [];
  const suelta = respuesta.headers.get("set-cookie");
  const lineas = lista.length > 0 ? lista : suelta ? [suelta] : [];
  for (const linea of lineas) {
    const coincidencia = /^pn_punto=([^;]+)/i.exec(linea.trim());
    if (!coincidencia) continue;
    const maxAge = /max-age=(\d+)/i.exec(linea);
    const expira = /expires=([^;]+)/i.exec(linea);
    let expiraEn = new Date(Date.now() + 8 * 60 * 60 * 1000);
    if (maxAge) expiraEn = new Date(Date.now() + Number(maxAge[1]) * 1000);
    else if (expira) {
      const fecha = new Date(expira[1]);
      if (!Number.isNaN(fecha.getTime())) expiraEn = fecha;
    }
    return { valor: decodeURIComponent(coincidencia[1]), expiraEn };
  }
  return null;
}

/**
 * La entrada al canal responde `{ organismoId, codigo, nombre }` y la sesión.
 * Si el cuerpo trae `token` (igual que /sesiones), se quita antes de llegar al navegador.
 * Si no, se acepta la cookie `pn_punto` que haya puesto el BFF.
 */
async function sesionDePunto(respuesta: Response, headers: Headers): Promise<ResultadoProxy> {
  const deCookie = tokenDeCookiePunto(respuesta);
  const texto = await respuesta.text();
  let datos: Record<string, unknown> | null = null;
  if (texto) {
    try {
      const parsed = JSON.parse(texto) as unknown;
      datos = parsed !== null && typeof parsed === "object" && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null;
    } catch {
      datos = null;
    }
  }
  const deCuerpo = typeof datos?.token === "string" ? datos.token : null;
  let cuerpo: BodyInit | null = texto || null;
  if (datos && deCuerpo) {
    const { token: _token, ...sinToken } = datos;
    void _token;
    cuerpo = JSON.stringify(sinToken);
    headers.set("content-type", "application/json");
  }
  const valor = deCuerpo ?? deCookie?.valor ?? null;
  return {
    status: respuesta.status,
    headers,
    cuerpo,
    cookiePunto: valor
      ? { accion: "fijar", valor, expiraEn: deCookie?.expiraEn ?? new Date(Date.now() + 8 * 60 * 60 * 1000) }
      : undefined,
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
