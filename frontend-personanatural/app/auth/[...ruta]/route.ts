import { authRoutes } from "@/lib/caja/auth.server";
import { origenPermitido } from "@/lib/server/proxy";

// @flit/auth: sesión de la caja del organismo (producto `caja`). Rutas: /auth/login, /auth/callback, /auth/logout,
// /auth/refresh, /auth/session y /auth/frontchannel-logout. El login es una navegación (302) al hub, no un formulario.
export const dynamic = "force-dynamic";

type Contexto = { params: Promise<{ ruta: string[] }> };

const GET_RUTAS = {
  login: authRoutes.login,
  callback: authRoutes.callback,
  session: authRoutes.session,
  "frontchannel-logout": authRoutes.frontchannelLogout,
} as const;

/**
 * El cierre de sesión cambia estado: solo por POST y con Origin propio (un `<img src="/auth/logout">` de otro sitio no
 * cierra nada). Responde 200 `{ redirectTo }` con la URL de cierre del hub —y las cookies ya borradas— para que el
 * navegador navegue a ella: un `fetch` no sigue el 302 al hub y la CSP (`form-action 'self'`) veta un formulario.
 */
async function cerrarSesion(request: Request): Promise<Response> {
  if (!origenPermitido(request)) return new Response(null, { status: 403 });
  const salida = await authRoutes.logout(request);
  const headers = new Headers({ "content-type": "application/json", "cache-control": "no-store" });
  for (const cookie of salida.headers.getSetCookie()) headers.append("set-cookie", cookie);
  return new Response(JSON.stringify({ redirectTo: salida.headers.get("location") }), { status: 200, headers });
}

const POST_RUTAS = { logout: cerrarSesion, refresh: authRoutes.refresh } as const;

async function atender(request: Request, contexto: Contexto, rutas: Record<string, (r: Request) => Promise<Response>>): Promise<Response> {
  const { ruta } = await contexto.params;
  const handler = ruta.length === 1 ? rutas[ruta[0]] : undefined;
  return handler ? handler(request) : new Response(null, { status: 404 });
}

export function GET(request: Request, contexto: Contexto): Promise<Response> {
  return atender(request, contexto, GET_RUTAS);
}

export function POST(request: Request, contexto: Contexto): Promise<Response> {
  return atender(request, contexto, POST_RUTAS);
}
