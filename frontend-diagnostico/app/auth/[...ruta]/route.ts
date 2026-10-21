import { authRoutes } from "@/lib/auth.server";

// @flit/auth (A-09): /auth/login, /auth/callback, /auth/logout, /auth/refresh, /auth/session y
// /auth/frontchannel-logout de Diagnóstico como cliente OIDC `diagnostico`. Cualquier otra ruta, 404.
export const dynamic = "force-dynamic";

type Context = { params: Promise<{ ruta: string[] }> };
type Accion = keyof typeof authRoutes;

const GETS: Record<string, Accion> = {
  login: "login",
  callback: "callback",
  logout: "logout",
  session: "session",
  "frontchannel-logout": "frontchannelLogout",
};
const POSTS: Record<string, Accion> = { logout: "logout", refresh: "refresh" };

async function despachar(acciones: Record<string, Accion>, request: Request, context: Context): Promise<Response> {
  const { ruta } = await context.params;
  const accion = ruta.length === 1 ? acciones[ruta[0]] : undefined;
  if (!accion) return new Response(null, { status: 404 });
  return authRoutes[accion](request);
}

export function GET(request: Request, context: Context): Promise<Response> {
  return despachar(GETS, request, context);
}

export function POST(request: Request, context: Context): Promise<Response> {
  return despachar(POSTS, request, context);
}
