import { authRoutes } from "@/lib/auth.server";

// /auth/frontchannel-logout de @flit/auth: cuando se cierra sesión en el hub o en otro producto, el hub abre esta
// ruta en segundo plano y Comparendos borra su propia sesión al instante.
export const dynamic = "force-dynamic";

export function GET(request: Request): Promise<Response> {
  return authRoutes.frontchannelLogout(request);
}
