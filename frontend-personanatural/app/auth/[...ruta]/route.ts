import { authRoutes } from "@/lib/caja/auth.server";

// @flit/auth: sesión de la caja del organismo (producto `caja`). Rutas: /auth/login, /auth/callback, /auth/logout,
// /auth/refresh, /auth/session y /auth/frontchannel-logout. El login es una navegación (302) al hub, no un formulario.
export const dynamic = "force-dynamic";

type Contexto = { params: Promise<{ ruta: string[] }> };

const GET_RUTAS = {
  login: authRoutes.login,
  callback: authRoutes.callback,
  logout: authRoutes.logout,
  session: authRoutes.session,
  "frontchannel-logout": authRoutes.frontchannelLogout,
} as const;

const POST_RUTAS = { logout: authRoutes.logout, refresh: authRoutes.refresh } as const;

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
